using System.Runtime.InteropServices;

namespace Kd100Win.Device;

/// <summary>
/// Watches the KD100's keyboard, consumer and radial-controller collections via Raw Input.
/// In vendor mode they are silent, so any input there means the device has dropped back
/// to its factory mode (e.g. after the wireless link woke up) and needs the switch again.
/// </summary>
internal sealed class FallbackDetector : NativeWindow, IDisposable
{
    const int WM_INPUT = 0x00FF;
    const uint RIDEV_INPUTSINK = 0x100;
    const uint RID_HEADER = 0x10000005;
    const uint RIDI_DEVICENAME = 0x20000007;
    static readonly nint HWND_MESSAGE = -3;

    readonly Dictionary<nint, bool> _isKd100 = new();
    long _lastFire;

    public event Action? FallbackDetected;

    public FallbackDetector()
    {
        CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        var devices = new RAWINPUTDEVICE[]
        {
            new() { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_INPUTSINK, Target = Handle }, // keyboard
            new() { UsagePage = 0x01, Usage = 0x0E, Flags = RIDEV_INPUTSINK, Target = Handle }, // radial controller
            new() { UsagePage = 0x0C, Usage = 0x01, Flags = RIDEV_INPUTSINK, Target = Handle }, // consumer control
        };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            Log.Info($"RegisterRawInputDevices failed: {Marshal.GetLastPInvokeError()}");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
            OnInput(m.LParam);
        base.WndProc(ref m);
    }

    void OnInput(nint hRawInput)
    {
        uint size = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        if (GetRawInputData(hRawInput, RID_HEADER, out RAWINPUTHEADER header, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == uint.MaxValue)
            return;
        // hDevice is 0 for injected input, including our own SendInput calls.
        if (header.Device == 0 || !IsKd100(header.Device))
            return;

        long now = Environment.TickCount64;
        if (now - _lastFire < 1000)
            return;
        _lastFire = now;
        Log.Info("KD100 input on a factory-mode collection; switching to vendor mode again");
        FallbackDetected?.Invoke();
    }

    bool IsKd100(nint device)
    {
        if (_isKd100.TryGetValue(device, out bool known))
            return known;
        uint chars = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, null, ref chars);
        var buf = new char[chars + 1];
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buf, ref chars);
        string name = new string(buf).TrimEnd('\0');
        bool match = name.Contains("VID_256C&PID_006D", StringComparison.OrdinalIgnoreCase);
        _isKd100[device] = match;
        return match;
    }

    public void Dispose() => DestroyHandle();

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    static extern uint GetRawInputData(nint hRawInput, uint command, out RAWINPUTHEADER data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetRawInputDeviceInfo(nint device, uint command, char[]? data, ref uint size);
}
