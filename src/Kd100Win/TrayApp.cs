using System.Diagnostics;
using Kd100Win.Config;
using Kd100Win.Device;
using Kd100Win.Engine;
using Kd100Win.Platform;
using Kd100Win.UI;
using Microsoft.Win32;

namespace Kd100Win;

internal sealed class TrayApp : ApplicationContext
{
    static readonly string[] HuionProcesses = ["HuionTablet", "HuionServer", "HuionTabletCore"];

    readonly Icon _icon;
    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _menu = new();
    readonly ConfigStore _store = new();
    readonly Mapper _mapper;
    readonly DeviceManager _devices;
    readonly FallbackDetector _fallback;
    readonly ForegroundWatcher _foreground;
    readonly OsdForm _osd = new();
    readonly RadialMenuForm _radial = new();
    MonitorForm? _monitor;
    SettingsForm? _settings;

    public TrayApp()
    {
        using (var s = typeof(TrayApp).Assembly.GetManifestResourceStream("kd100-win.ico")!)
            _icon = new Icon(s);

        _tray = new NotifyIcon { Icon = _icon, Text = AppPaths.AppName, ContextMenuStrip = _menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowSettings();
        _menu.Opening += (_, _) => BuildMenu();

        _store.EnsureExists();
        _mapper = new Mapper(LoadConfigOrSample(), new UiScheduler(), _radial);
        Strings.SetLanguage(_mapper.Config.Language);
        _mapper.StateChanged += text =>
        {
            UpdateTooltip();
            if (text == null)
                _osd.Hide();
            else if (_mapper.Config.ShowOsd)
                _osd.ShowText(text);
        };
        _mapper.Activity += line => _monitor?.Append(line);

        _devices = new DeviceManager();
        _devices.ReportReceived += OnReport;
        _devices.ConnectionCountChanged += _ =>
        {
            UpdateTooltip();
            _settings?.OnDeviceConnectionChanged();
        };

        _foreground = new ForegroundWatcher();
        _foreground.Changed += _mapper.OnForegroundProcess;
        _mapper.OnForegroundProcess(_foreground.Current);

        _fallback = new FallbackDetector();
        _fallback.FallbackDetected += _devices.Reswitch;

        _store.Changed += ReloadConfig;
        _store.Watch();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _devices.Rescan();
        UpdateTooltip();
        WarnIfHuionRunning();
        if (_devices.ConnectionCount == 0)
            Notify(Strings.NotFoundTitle, Strings.NotFoundText, ToolTipIcon.Info);
        Log.Info("Started");
    }

    void OnReport(Kd100Report report)
    {
        if (_settings is { IsDisposed: false } settings)
        {
            settings.OnDeviceReport(report);
            // While the user is configuring, the device only highlights buttons in the window.
            if (settings.IsForeground)
                return;
        }
        _mapper.OnReport(report);
    }

    CompiledConfig LoadConfigOrSample()
    {
        try
        {
            return _store.Load();
        }
        catch (Exception e) when (e is FormatException or IOException)
        {
            Notify(Strings.ConfigErrorTitle, e.Message, ToolTipIcon.Error);
            Log.Error("Config", e);
            return ConfigStore.Parse(ConfigStore.SampleText());
        }
    }

    void ReloadConfig()
    {
        try
        {
            var config = _store.Load();
            _mapper.SetConfig(config, _foreground.Current);
            Strings.SetLanguage(config.Language);
            IconResolver.Clear();
            UpdateTooltip();
            if (config.ShowOsd)
                _osd.ShowText($"{Strings.ConfigReloaded} · {_mapper.Describe()}");
            Log.Info("Config reloaded");
        }
        catch (Exception e) when (e is FormatException or IOException)
        {
            Notify(Strings.ConfigNotReloaded, e.Message, ToolTipIcon.Error);
            Log.Error("Config", e);
        }
        _settings?.OnConfigChangedOnDisk();
    }

    void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;
        // The device re-enumerates after sleep; rescan, then re-send the mode switch in case it did not.
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            _devices.Rescan();
            _devices.Reswitch();
        };
        timer.Start();
    }

    void WarnIfHuionRunning()
    {
        var running = HuionProcesses.Where(n => Process.GetProcessesByName(n).Length > 0).ToList();
        if (running.Count > 0)
            Notify(Strings.HuionTitle, Strings.HuionText(string.Join(", ", running)), ToolTipIcon.Warning);
    }

    void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(new ToolStripMenuItem(_devices.ConnectionCount > 0 ? Strings.Connected : Strings.NotConnected) { Enabled = false });
        _menu.Items.Add(new ToolStripMenuItem($"{Strings.Profile}: {_mapper.Profile.Name}") { Enabled = false });

        var dial = new ToolStripMenuItem(Strings.DialMode);
        var modes = _mapper.DialModes;
        for (int i = 0; i < modes.Count; i++)
        {
            int index = i;
            dial.DropDownItems.Add(new ToolStripMenuItem(modes[i].Name, null, (_, _) => _mapper.SelectDialMode(index))
            {
                Checked = modes[i] == _mapper.DialMode,
            });
        }
        dial.Enabled = modes.Count > 0;
        _menu.Items.Add(dial);
        _menu.Items.Add(new ToolStripSeparator());

        var settings = new ToolStripMenuItem(Strings.Settings, null, (_, _) => ShowSettings());
        settings.Font = new Font(settings.Font, FontStyle.Bold);
        _menu.Items.Add(settings);
        _menu.Items.Add(Strings.Monitor, null, (_, _) => ShowMonitor());
        _menu.Items.Add(Strings.EditJson, null, (_, _) => OpenConfig());
        _menu.Items.Add(Strings.Reload, null, (_, _) => ReloadConfig());
        _menu.Items.Add(Strings.OpenFolder, null, (_, _) => Shell(_store.Folder));
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(new ToolStripMenuItem(Strings.Autostart, null, (_, _) => ToggleAutostart()) { Checked = Autostart.Enabled });
        _menu.Items.Add(new ToolStripMenuItem(Strings.Pause, null, (_, _) =>
        {
            _mapper.Paused = !_mapper.Paused;
            UpdateTooltip();
        })
        { Checked = _mapper.Paused });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Strings.Exit, null, (_, _) => ExitThread());
    }

    void ToggleAutostart()
    {
        try
        {
            Autostart.Enabled = !Autostart.Enabled;
        }
        catch (Exception e)
        {
            Notify(Strings.AutostartFailed, e.Message, ToolTipIcon.Error);
        }
    }

    void OpenConfig()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_store.Path) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No app is associated with .json.
            Process.Start("notepad.exe", $"\"{_store.Path}\"")?.Dispose();
        }
    }

    static void Shell(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    void ShowSettings()
    {
        if (_settings == null || _settings.IsDisposed)
        {
            _settings = new SettingsForm(_store, _devices, _icon);
            _settings.FormClosed += (_, _) => _settings = null;
        }
        _settings.Show();
        if (_settings.WindowState == FormWindowState.Minimized)
            _settings.WindowState = FormWindowState.Normal;
        _settings.Activate();
    }

    void ShowMonitor()
    {
        if (_monitor == null || _monitor.IsDisposed)
        {
            _monitor = new MonitorForm(_icon);
            Log.Written += _monitor.Append;
            _monitor.FormClosed += (_, _) =>
            {
                Log.Written -= _monitor!.Append;
                _monitor = null;
            };
        }
        _monitor.Show();
        _monitor.Activate();
        UpdateTooltip();
    }

    void UpdateTooltip()
    {
        string device = _devices.ConnectionCount > 0 ? Strings.Connected : Strings.NotConnected;
        string state = $"{device} · {_mapper.Describe()}{(_mapper.Paused ? $" · {Strings.Paused}" : "")}";
        // NotifyIcon.Text is limited to 127 characters.
        _tray.Text = state.Length > 127 ? state[..127] : state;
        _monitor?.SetStatus($"{state}{Environment.NewLine}config: {_store.Path}");
    }

    void Notify(string title, string text, ToolTipIcon icon) => _tray.ShowBalloonTip(5000, title, text, icon);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _mapper.ReleaseAll();
            _devices.Dispose();
            _fallback.Dispose();
            _foreground.Dispose();
            _store.Dispose();
            _settings?.Dispose();
            _monitor?.Dispose();
            _osd.Dispose();
            _radial.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
