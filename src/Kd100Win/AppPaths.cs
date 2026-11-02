namespace Kd100Win;

internal static class AppPaths
{
    public const string AppName = "kd100-win";

    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string LogFile => Path.Combine(DataFolder, AppName + ".log");
}

/// <summary>Tiny append-only log in %APPDATA%\kd100-win, trimmed when it grows past 1 MB.</summary>
internal static class Log
{
    static readonly object Lock = new();
    const long MaxSize = 1 << 20;

    /// <summary>Turned off by the unit tests.</summary>
    public static bool ToFile { get; set; } = true;

    public static event Action<string>? Written;

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception e) => Write("ERROR", $"{message}: {e.GetType().Name}: {e.Message}");

    static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
        lock (Lock)
        {
            if (ToFile)
                Append(line);
        }
        Written?.Invoke(line);
    }

    static void Append(string line)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            var file = new FileInfo(AppPaths.LogFile);
            if (file.Exists && file.Length > MaxSize)
                file.Delete();
            File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
    }
}
