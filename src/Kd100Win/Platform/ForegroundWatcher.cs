using System.Runtime.InteropServices;

namespace Kd100Win.Platform;

/// <summary>Reports the process name (without .exe) of the foreground window whenever it changes.</summary>
internal sealed class ForegroundWatcher : IDisposable
{
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    const uint WINEVENT_OUTOFCONTEXT = 0;

    readonly WinEventDelegate _callback; // keep alive: the hook holds a raw pointer to it
    readonly nint _hook;
    readonly int _ownPid = Environment.ProcessId;

    /// <summary>Raised on the UI thread. Our own windows (menus, settings) are ignored.</summary>
    public event Action<string?>? Changed;

    public ForegroundWatcher()
    {
        _callback = OnEvent;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public string? Current => ProcessNameOf(GetForegroundWindow());

    void OnEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownPid || pid == 0)
            return;
        Changed?.Invoke(ProcessNameOf(hwnd));
    }

    static string? ProcessNameOf(nint hwnd)
    {
        if (hwnd == 0)
            return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return Processes.ImagePath(pid) is { } path ? Path.GetFileNameWithoutExtension(path) : null;
    }

    public void Dispose()
    {
        if (_hook != 0)
            UnhookWinEvent(_hook);
    }

    delegate void WinEventDelegate(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventDelegate callback, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
