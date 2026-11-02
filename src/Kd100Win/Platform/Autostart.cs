using Microsoft.Win32;

namespace Kd100Win.Platform;

/// <summary>"Start with Windows" via the per-user Run key (no admin rights needed).</summary>
internal static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppPaths.AppName) is string value
                && value.Equals(Command, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(AppPaths.AppName, Command);
            else
                key.DeleteValue(AppPaths.AppName, throwOnMissingValue: false);
        }
    }
}
