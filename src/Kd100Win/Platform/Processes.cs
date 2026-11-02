using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Kd100Win.Platform;

internal sealed record WindowedProcess(string Name, string Title, string? Path);

internal static class Processes
{
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>Full path of a process's exe; works for most elevated processes too.</summary>
    public static string? ImagePath(uint pid)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
            return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(process, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Processes with a visible main window, one entry per process name, for picking profile apps.</summary>
    public static List<WindowedProcess> Windowed()
    {
        var result = new Dictionary<string, WindowedProcess>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == self || p.MainWindowHandle == 0 || string.IsNullOrWhiteSpace(p.MainWindowTitle))
                        continue;
                    string? path = ImagePath((uint)p.Id);
                    string name = path != null ? System.IO.Path.GetFileNameWithoutExtension(path) : p.ProcessName;
                    result.TryAdd(name, new WindowedProcess(name, p.MainWindowTitle, path));
                }
                catch (InvalidOperationException)
                {
                    // The process exited while we looked at it.
                }
            }
        }
        return result.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    [DllImport("kernel32.dll")]
    static extern nint OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(nint handle);
}
