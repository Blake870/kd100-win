using System.Runtime.InteropServices;
using System.Text;

namespace Kd100Win.Platform;

/// <summary>Moves the window a launched program opens onto a chosen monitor.</summary>
internal static class WindowPlacer
{
    const int TimeoutMs = 10000;

    /// <summary>Monitors numbered from 1, left to right (then top to bottom).</summary>
    public static IReadOnlyList<Screen> Screens =>
        Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToList();

    /// <summary>The visible top-level windows right now, taken just before launching.</summary>
    public static HashSet<nint> Snapshot()
    {
        var set = new HashSet<nint>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd))
                set.Add(hwnd);
            return true;
        }, 0);
        return set;
    }

    /// <summary>
    /// Waits (on the UI thread) for a new main window to appear and moves it to monitor
    /// <paramref name="screen"/> (1-based, null = leave it where it is), maximizing it if asked.
    /// </summary>
    public static void PlaceNextWindow(HashSet<nint> before, int? screen, bool maximize)
    {
        var started = Environment.TickCount64;
        int ownPid = Environment.ProcessId;
        var timer = new System.Windows.Forms.Timer { Interval = 150 };
        timer.Tick += (_, _) =>
        {
            nint found = 0;
            EnumWindows((hwnd, _) =>
            {
                if (!before.Contains(hwnd) && IsCandidate(hwnd, ownPid))
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, 0);

            if (found != 0)
            {
                timer.Dispose();
                Place(found, screen, maximize);
                // Some programs restore their saved position right after showing; place once more.
                var again = new System.Windows.Forms.Timer { Interval = 600 };
                again.Tick += (_, _) =>
                {
                    again.Dispose();
                    if (IsWindow(found))
                        Place(found, screen, maximize);
                };
                again.Start();
            }
            else if (Environment.TickCount64 - started > TimeoutMs)
            {
                timer.Dispose();
                Log.Info("No new window appeared to move to the chosen screen");
            }
        };
        timer.Start();
    }

    static bool IsCandidate(nint hwnd, int ownPid)
    {
        if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != 0)
            return false;
        if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0)
            return false;
        if (GetWindowTextLength(hwnd) == 0)
            return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid != ownPid;
    }

    static void Place(nint hwnd, int? screenNumber, bool maximize)
    {
        var screens = Screens;
        var target = screenNumber is int n && n >= 1 && n <= screens.Count ? screens[n - 1] : Screen.FromHandle(hwnd);
        bool wasMaximized = IsZoomed(hwnd);
        if (wasMaximized || IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);

        GetWindowRect(hwnd, out RECT r);
        var source = Screen.FromHandle(hwnd).WorkingArea;
        var area = target.WorkingArea;
        int w = Math.Min(r.Right - r.Left, area.Width);
        int h = Math.Min(r.Bottom - r.Top, area.Height);
        // Keep the window's offset within its monitor, clamped to the target monitor.
        int x = area.Left + Math.Clamp(r.Left - source.Left, 0, area.Width - w);
        int y = area.Top + Math.Clamp(r.Top - source.Top, 0, area.Height - h);
        SetWindowPos(hwnd, 0, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
        if (maximize || wasMaximized)
            ShowWindow(hwnd, SW_MAXIMIZE);
    }

    /// <summary>"1 — 2560×1440, primary" for the settings window.</summary>
    public static IEnumerable<object> Describe() => Screens.Select((s, i) => new
    {
        number = i + 1,
        width = s.Bounds.Width,
        height = s.Bounds.Height,
        primary = s.Primary,
    });

    const int GW_OWNER = 4;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x80;
    const int SW_RESTORE = 9;
    const int SW_MAXIMIZE = 3;
    const uint SWP_NOZORDER = 0x4;
    const uint SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] static extern nint GetWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] static extern long GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
