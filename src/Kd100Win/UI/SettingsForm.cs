using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kd100Win.Config;
using Kd100Win.Device;
using Kd100Win.Engine;
using Kd100Win.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Kd100Win.UI;

/// <summary>
/// The settings window: an HTML UI (settings.html, embedded) in WebView2. The page edits the
/// config as a JSON object and calls back into this class for files, icons, processes and saving.
/// </summary>
internal sealed class SettingsForm : Form
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Cyrillic and "+" readable in the file
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    static readonly (string Exe, string Label)[] KnownBrowsers =
    [
        ("chrome", "Google Chrome"), ("msedge", "Microsoft Edge"), ("firefox", "Firefox"),
        ("browser", "Yandex Browser"), ("opera", "Opera"), ("brave", "Brave"), ("vivaldi", "Vivaldi"),
    ];

    readonly ConfigStore _store;
    readonly DeviceManager _devices;
    readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    bool _ready;
    bool _dirty;

    public SettingsForm(ConfigStore store, DeviceManager devices, Icon icon)
    {
        _store = store;
        _devices = devices;
        Text = "kd100-win";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(LogicalToDeviceUnits(1240), LogicalToDeviceUnits(820));
        MinimumSize = new Size(LogicalToDeviceUnits(980), LogicalToDeviceUnits(640));
        BackColor = Color.FromArgb(24, 25, 29);
        Controls.Add(_web);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.AppName, "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(Strings.WebView2Missing, "kd100-win", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        var settings = _web.CoreWebView2.Settings;
        settings.AreBrowserAcceleratorKeysEnabled = false; // so F5, Ctrl+R... can be recorded as shortcuts
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
#if !DEBUG
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = false;
#endif
        _web.CoreWebView2.WebMessageReceived += OnMessage;
        _web.CoreWebView2.NavigateToString(ReadResource("settings.html"));
    }

    /// <summary>True while the user is working in this window; the tray app then stops sending input.</summary>
    public bool IsForeground => ActiveForm == this;

    /// <summary>Lets the page highlight the button pressed on the real device.</summary>
    public void OnDeviceReport(Kd100Report r)
    {
        if (!_ready)
            return;
        object message = r.Kind == ReportKind.Dial
            ? new { @event = "dial", direction = r.DialDirection }
            : new { @event = "buttons", mask = r.Buttons };
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }

    /// <summary>Tells the page the file changed on disk (e.g. edited by hand) so it can offer to reload.</summary>
    public void OnConfigChangedOnDisk()
    {
        if (_ready)
            _web.CoreWebView2.PostWebMessageAsJson("""{"event":"configChanged"}""");
    }

    /// <summary>Tells the page a KD100 was plugged in or out, so it can refresh the device settings.</summary>
    public void OnDeviceConnectionChanged()
    {
        if (_ready)
            _web.CoreWebView2.PostWebMessageAsJson("""{"event":"deviceChanged"}""");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && _ready && e.CloseReason == CloseReason.UserClosing)
        {
            var answer = MessageBox.Show(this, Strings.SaveChanges, "kd100-win", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                e.Cancel = true;
            }
            else if (answer == DialogResult.Yes)
            {
                // The page saves and then calls "close"; if saving fails it shows the errors and stays open.
                e.Cancel = true;
                _web.CoreWebView2.PostWebMessageAsJson("""{"event":"saveAndClose"}""");
            }
        }
        base.OnFormClosing(e);
    }

    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonNode? id = null;
        try
        {
            var request = JsonNode.Parse(e.WebMessageAsJson)!.AsObject();
            id = request["id"]?.DeepClone();
            string method = request["method"]!.GetValue<string>();
            var args = request["args"] as JsonObject ?? new JsonObject();
            object? result = Dispatch(method, args);
            if (result is Task<object?> task)
                result = await task;
            if (IsDisposed)
                return;
            Reply(id, true, result, null);
        }
        catch (Exception ex)
        {
            Log.Error("settings bridge", ex);
            if (!IsDisposed)
                Reply(id, false, null, ex.Message);
        }
    }

    void Reply(JsonNode? id, bool ok, object? result, string? error)
    {
        var json = JsonSerializer.Serialize(new { id, ok, result, error }, Json);
        _web.CoreWebView2.PostWebMessageAsJson(json);
    }

    object? Dispatch(string method, JsonObject args)
    {
        switch (method)
        {
            case "init":
                _ready = true;
                return Init();
            case "sample":
                return JsonNode.Parse(ConfigStore.SampleText(), null, ReadOptions);
            case "save":
                return Save(args["config"]!);
            case "validateAction":
                try
                {
                    ActionParser.Parse(args["action"]?.GetValue<string>());
                    return new { ok = true };
                }
                catch (FormatException ex)
                {
                    return new { ok = false, error = ex.Message };
                }
            case "processes":
                return Processes.Windowed().Select(p => new
                {
                    p.Name,
                    p.Title,
                    icon = p.Path == null ? null : IconResolver.ToDataUrl(IconResolver.ForTarget(p.Path)),
                });
            case "screens":
                return WindowPlacer.Describe();
            case "installedApps":
                return InstalledApps.Get().Select(a => new { a.Name, a.Target, a.Exe });
            case "browsers":
                return KnownBrowsers
                    .Select(b => (b.Exe, b.Label, Path: IconResolver.ResolveProgram(b.Exe)))
                    .Where(b => b.Path != null)
                    .Select(b => new { exe = b.Exe, label = b.Label, icon = IconResolver.ToDataUrl(IconResolver.ShellIcon(b.Path!)) });
            case "icon":
                {
                    string target = args["target"]!.GetValue<string>();
                    bool isFile = args["file"]?.GetValue<bool>() == true;
                    return IconResolver.ToDataUrl(isFile ? IconResolver.ForIconFile(target) : IconResolver.ForTarget(target));
                }
            case "browse":
                return Browse(args["kind"]?.GetValue<string>() ?? "program");
            case "readImage":
                return ReadImage(args["path"]!.GetValue<string>());
            case "saveIcon":
                return SaveIcon(args["name"]!.GetValue<string>(), args["data"]!.GetValue<string>());
            case "setAutostart":
                Autostart.Enabled = args["on"]!.GetValue<bool>();
                return Autostart.Enabled;
            case "device":
                return DeviceSettingsAsync(null, null);
            case "setDevice":
                return DeviceSettingsAsync(args["brightness"]?.GetValue<int>(), args["sleep"]?.GetValue<int>());
            case "dirty":
                _dirty = args["dirty"]!.GetValue<bool>();
                return null;
            case "close":
                _dirty = false;
                BeginInvoke(Close);
                return null;
            case "openFile":
                Launch(_store.Path);
                return null;
            default:
                throw new InvalidOperationException($"unknown method {method}");
        }
    }

    object Init()
    {
        JsonNode? config = null;
        string? error = null;
        try
        {
            string text = File.ReadAllText(_store.Path);
            config = JsonNode.Parse(text, null, ReadOptions);
            ConfigStore.Parse(text);
        }
        catch (Exception e) when (e is JsonException or FormatException or IOException)
        {
            error = e.Message;
            config ??= JsonNode.Parse(ConfigStore.SampleText(), null, ReadOptions);
        }
        return new
        {
            config,
            error,
            path = _store.Path,
            autostart = Autostart.Enabled,
            systemLanguage = Strings.Resolve("auto"),
            keys = Input.KeyNames.AllNames.OrderBy(k => k).ToList(),
            version = typeof(SettingsForm).Assembly.GetName().Version?.ToString(3),
        };
    }

    object Save(JsonNode config)
    {
        string json = config.ToJsonString(Json);
        try
        {
            ConfigStore.Parse(json);
        }
        catch (FormatException e)
        {
            return new { ok = false, error = e.Message };
        }
        string text = "// kd100-win configuration, written by the settings window. Format: see README.md." + Environment.NewLine + json + Environment.NewLine;
        File.WriteAllText(_store.Path, text);
        return new { ok = true };
    }

    /// <summary>Reads the brightness and sleep time from a KD100 on the cable, changing them first if given.</summary>
    async Task<object?> DeviceSettingsAsync(int? brightness, int? sleep)
    {
        string? path = _devices.CablePath;
        if (path == null)
            return new { cable = false, connected = _devices.ConnectionCount > 0 };
        var values = await Task.Run(() => brightness == null && sleep == null
            ? DeviceSettings.Read(path)
            : DeviceSettings.Apply(path, brightness, sleep));
        if (brightness != null || sleep != null)
            Log.Info($"Device settings: brightness {values.Brightness}, sleep {values.SleepMinutes} min");
        return new { cable = true, connected = true, brightness = values.Brightness, sleep = values.SleepMinutes };
    }

    string? Browse(string kind)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = kind == "icon"
                ? "Icons and images|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.svg;*.avif;*.exe;*.dll|All files|*.*"
                : "Programs|*.exe;*.lnk;*.bat;*.cmd;*.ps1|All files|*.*",
            DereferenceLinks = false,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <summary>Hands an image to the page, which can decode formats GDI+ cannot (WebP, SVG, AVIF).</summary>
    static string ReadImage(string path)
    {
        string mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".avif" => "image/avif",
            ".gif" => "image/gif",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "image/png",
        };
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    /// <summary>Stores a PNG rendered by the page in %APPDATA%\kd100-win\icons and returns its path.</summary>
    static string SaveIcon(string name, string dataUrl)
    {
        const string prefix = "data:image/png;base64,";
        if (!dataUrl.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException("expected a PNG data URL");
        string folder = Path.Combine(AppPaths.DataFolder, "icons");
        Directory.CreateDirectory(folder);
        string safe = string.Concat(Path.GetFileNameWithoutExtension(name).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string path = Path.Combine(folder, (safe.Length > 0 ? safe : "icon") + ".png");
        File.WriteAllBytes(path, Convert.FromBase64String(dataUrl[prefix.Length..]));
        return path;
    }

    static void Launch(string target) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    static string ReadResource(string name)
    {
        using var s = typeof(SettingsForm).Assembly.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
