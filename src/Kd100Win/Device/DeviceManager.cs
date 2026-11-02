using HidSharp;

namespace Kd100Win.Device;

/// <summary>
/// Finds every connected KD100 (cable and/or 2.4 GHz receiver), switches it into
/// vendor mode and reads its reports on a background thread per device.
/// Events are raised on the synchronization context the manager was created on.
/// </summary>
internal sealed class DeviceManager : IDisposable
{
    readonly SynchronizationContext _ui;
    readonly Dictionary<string, Connection> _open = new(StringComparer.OrdinalIgnoreCase);
    readonly object _lock = new();
    bool _disposed;

    public event Action<Kd100Report>? ReportReceived;
    public event Action<int>? ConnectionCountChanged;

    public DeviceManager()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        DeviceList.Local.Changed += (_, _) => Rescan();
    }

    public int ConnectionCount
    {
        get { lock (_lock) return _open.Count; }
    }

    public void Rescan()
    {
        List<HidDevice> found;
        try
        {
            found = DeviceList.Local.GetHidDevices(Kd100Protocol.VendorId, Kd100Protocol.ProductId)
                .Where(IsVendorCollection)
                .ToList();
        }
        catch (Exception e)
        {
            Log.Error("Device enumeration failed", e);
            return;
        }

        bool changed = false;
        lock (_lock)
        {
            if (_disposed)
                return;
            foreach (var dev in found)
            {
                if (_open.ContainsKey(dev.DevicePath))
                    continue;
                var conn = Connection.TryOpen(dev, this);
                if (conn != null)
                {
                    _open[dev.DevicePath] = conn;
                    changed = true;
                }
            }
        }
        if (changed)
            RaiseCount();
    }

    /// <summary>Send the vendor-mode switch again, e.g. after the device fell back to default mode.</summary>
    public void Reswitch()
    {
        List<Connection> conns;
        lock (_lock) conns = _open.Values.ToList();
        foreach (var c in conns)
            c.SwitchMode();
    }

    static bool IsVendorCollection(HidDevice d)
    {
        // MI_00 holds a single vendor-defined collection with a 12-byte input report.
        if (d.DevicePath.IndexOf("&mi_00", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        try
        {
            return d.GetMaxInputReportLength() == Kd100Protocol.ReportLength;
        }
        catch
        {
            return false;
        }
    }

    void OnClosed(Connection c)
    {
        bool removed;
        lock (_lock) removed = _open.Remove(c.Path);
        if (removed)
        {
            Log.Info($"Disconnected: {c.Path}");
            RaiseCount();
        }
    }

    void RaiseCount()
    {
        int n = ConnectionCount;
        _ui.Post(_ => ConnectionCountChanged?.Invoke(n), null);
    }

    void OnReport(Kd100Report r) => _ui.Post(_ => ReportReceived?.Invoke(r), null);

    public void Dispose()
    {
        List<Connection> conns;
        lock (_lock)
        {
            _disposed = true;
            conns = _open.Values.ToList();
            _open.Clear();
        }
        foreach (var c in conns)
            c.Dispose();
    }

    sealed class Connection : IDisposable
    {
        readonly HidStream _stream;
        readonly DeviceManager _owner;
        volatile bool _closing;

        public string Path { get; }

        Connection(HidDevice device, HidStream stream, DeviceManager owner)
        {
            Path = device.DevicePath;
            _stream = stream;
            _owner = owner;
        }

        public static Connection? TryOpen(HidDevice device, DeviceManager owner)
        {
            if (!device.TryOpen(out HidStream stream))
            {
                Log.Info($"Could not open {device.DevicePath}");
                return null;
            }
            stream.ReadTimeout = Timeout.Infinite;
            var c = new Connection(device, stream, owner);
            Log.Info($"Connected: {c.Path}");
            c.SwitchMode();
            new Thread(c.ReadLoop) { IsBackground = true, Name = "KD100 reader" }.Start();
            return c;
        }

        public void SwitchMode()
        {
            try
            {
                NativeHid.GetIndexedString(Path, Kd100Protocol.ModeSwitchStringIndex);
                Log.Info("Vendor mode requested");
            }
            catch (Exception e)
            {
                Log.Error("Vendor mode switch failed", e);
            }
        }

        void ReadLoop()
        {
            var buf = new byte[_stream.Device.GetMaxInputReportLength()];
            try
            {
                while (!_closing)
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n <= 0)
                        continue;
                    var report = Kd100Protocol.Parse(buf.AsSpan(0, n));
                    if (report.Kind != ReportKind.Unknown)
                        _owner.OnReport(report);
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or TimeoutException)
            {
                // Unplugged, receiver lost, or we are shutting down.
            }
            finally
            {
                _stream.Dispose();
                if (!_closing)
                    _owner.OnClosed(this);
            }
        }

        public void Dispose()
        {
            _closing = true;
            _stream.Dispose();
        }
    }
}
