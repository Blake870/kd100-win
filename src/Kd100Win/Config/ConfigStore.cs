using System.Text.Json;
using Kd100Win.Engine;

namespace Kd100Win.Config;

/// <summary>Locates, creates, loads and watches config.json.</summary>
internal sealed class ConfigStore : IDisposable
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    FileSystemWatcher? _watcher;
    System.Windows.Forms.Timer? _debounce;

    public string Path { get; }

    public string Folder => System.IO.Path.GetDirectoryName(Path)!;

    /// <summary>Raised on the UI thread when the file changed on disk.</summary>
    public event Action? Changed;

    public ConfigStore()
    {
        // Portable mode: a config.json next to the exe wins over the one in %APPDATA%.
        string portable = System.IO.Path.Combine(AppContext.BaseDirectory, "config.json");
        Path = File.Exists(portable) ? portable : System.IO.Path.Combine(AppPaths.DataFolder, "config.json");
    }

    public void EnsureExists()
    {
        if (File.Exists(Path))
            return;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path, SampleText());
        Log.Info($"Created {Path} from the sample config");
    }

    public static string SampleText()
    {
        using var s = typeof(ConfigStore).Assembly.GetManifestResourceStream("config.sample.json")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public CompiledConfig Load() => Parse(File.ReadAllText(Path));

    public static CompiledConfig Parse(string json)
    {
        AppConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"JSON error at line {e.LineNumber + 1}: {e.Message}", e);
        }
        return CompiledConfig.Compile(config ?? new AppConfig());
    }

    /// <summary>Start watching the file. Must be called on the UI thread.</summary>
    public void Watch()
    {
        _debounce = new System.Windows.Forms.Timer { Interval = 300 };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Changed?.Invoke();
        };
        var ui = SynchronizationContext.Current!;
        _watcher = new FileSystemWatcher(Folder, System.IO.Path.GetFileName(Path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        // Editors save in bursts (truncate, write, rename); wait for them to settle.
        FileSystemEventHandler restart = (_, _) => ui.Post(_ => { _debounce.Stop(); _debounce.Start(); }, null);
        _watcher.Changed += restart;
        _watcher.Created += restart;
        _watcher.Renamed += (s, e) => restart(s, e);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
