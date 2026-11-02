namespace Kd100Win.Platform;

/// <param name="Name">Name shown in the Start menu.</param>
/// <param name="Target">What "run:" should open: the Start-menu shortcut, or shell:AppsFolder\{AUMID} for Store apps.</param>
/// <param name="Exe">Process name of the program when known (for profiles), else null.</param>
internal sealed record InstalledApp(string Name, string Target, string? Exe);

/// <summary>Programs from the Start menu: desktop apps via their shortcuts, Store apps via shell:AppsFolder.</summary>
internal static class InstalledApps
{
    static readonly string[] Junk = ["uninstall", "деинсталл", "удалить", "удаление", "readme", "read me", "help", "documentation", "license", "website", "web site", "release notes", "what's new"];

    static List<InstalledApp>? _cache;

    /// <summary>Must be called on an STA thread (the UI thread): it uses the Shell COM objects.</summary>
    public static List<InstalledApp> Get()
    {
        if (_cache != null)
            return _cache;
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
        }
        catch (Exception e)
        {
            Log.Error("Shell.Application", e);
        }

        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        })
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(lnk);
                if (IsJunk(name) || apps.ContainsKey(name))
                    continue;
                string? target = shell == null ? null : ShortcutTarget(shell, lnk);
                if (target != null && !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue; // links to web pages, documents, control panel items
                apps[name] = new InstalledApp(name, lnk, target == null ? null : Path.GetFileNameWithoutExtension(target));
            }
        }

        if (shell != null)
        {
            try
            {
                foreach (dynamic item in shell.NameSpace("shell:AppsFolder").Items())
                {
                    string name = item.Name;
                    string id = item.Path;
                    // Store apps have an AppUserModelID like "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App".
                    if (!id.Contains('!') || IsJunk(name) || apps.ContainsKey(name))
                        continue;
                    apps[name] = new InstalledApp(name, @"shell:AppsFolder\" + id, null);
                }
            }
            catch (Exception e)
            {
                Log.Error("AppsFolder", e);
            }
        }

        _cache = apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return _cache;
    }

    static bool IsJunk(string name) => Junk.Any(j => name.Contains(j, StringComparison.OrdinalIgnoreCase));

    static string? ShortcutTarget(dynamic shell, string lnk)
    {
        try
        {
            dynamic folder = shell.NameSpace(Path.GetDirectoryName(lnk));
            dynamic item = folder.ParseName(Path.GetFileName(lnk));
            string path = item.GetLink.Path;
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }
}
