using System.Diagnostics;
using Kd100Win.Input;
using Kd100Win.Platform;

namespace Kd100Win.Engine;

/// <summary>What actions can ask of the engine.</summary>
internal interface IActionHost
{
    void HoldLayer(bool down);
    void ToggleLayer();
    void CycleDialMode(int delta);
    void SelectDialMode(string name);
}

/// <summary>
/// A button or dial action. Buttons call Press/Release (so keys can be held);
/// the dial calls Step with the number of repeats after acceleration.
/// </summary>
internal abstract class KdAction
{
    public required string Source { get; init; }

    public virtual void Press(IActionHost host) { }

    public virtual void Release(IActionHost host) { }

    public virtual void Step(IActionHost host, int repeats)
    {
        for (int i = 0; i < repeats; i++)
        {
            Press(host);
            Release(host);
        }
    }

    public override string ToString() => Source;
}

internal sealed class NoAction : KdAction
{
    public static readonly NoAction Instance = new() { Source = "none" };
}

/// <summary>"ctrl+shift+z": held down for as long as the button is.</summary>
internal sealed class ChordAction(IReadOnlyList<ushort> keys) : KdAction
{
    public IReadOnlyList<ushort> Keys { get; } = keys;

    public override void Press(IActionHost host) => InputSender.Keys(Keys, down: true);

    public override void Release(IActionHost host) => InputSender.Keys(Keys, down: false);
}

/// <summary>"keys:ctrl+a ctrl+c": taps several chords in a row on press.</summary>
internal sealed class SequenceAction(IReadOnlyList<ChordAction> chords) : KdAction
{
    public IReadOnlyList<ChordAction> Chords { get; } = chords;

    public override void Press(IActionHost host)
    {
        foreach (var c in Chords)
        {
            c.Press(host);
            c.Release(host);
        }
    }
}

/// <summary>"text:Hello": types literal text, independent of the keyboard layout.</summary>
internal sealed class TextAction(string text) : KdAction
{
    public string Text { get; } = text;

    public override void Press(IActionHost host) => InputSender.Text(Text);
}

/// <summary>"wheel:down" / "wheel:up:40" / "wheel:left": scrolls by delta (120 = one notch).</summary>
internal sealed class WheelAction(int delta, bool horizontal) : KdAction
{
    public int Delta { get; } = delta;
    public bool Horizontal { get; } = horizontal;

    public override void Press(IActionHost host) => InputSender.Wheel(Delta, Horizontal);

    public override void Step(IActionHost host, int repeats) => InputSender.Wheel(Delta * repeats, Horizontal);
}

/// <summary>"mouse:left" etc.: held down for as long as the button is.</summary>
internal sealed class MouseAction(MouseButtons button) : KdAction
{
    public MouseButtons Button { get; } = button;

    public override void Press(IActionHost host) => InputSender.MouseButton(Button, down: true);

    public override void Release(IActionHost host) => InputSender.MouseButton(Button, down: false);
}

/// <summary>
/// "run:notepad.exe" / "run:\"C:\path with spaces\app.exe\" --flag" / "run:https://..." / "run:chrome https://...".
/// "run@2,max:..." opens the program's window on monitor 2, maximized.
/// </summary>
internal sealed class RunAction(string file, string arguments) : KdAction
{
    static readonly string[] BrowserNames = ["chrome", "msedge", "edge", "firefox", "opera", "brave", "vivaldi", "browser", "yandex"];

    public string File { get; } = file;
    public string Arguments { get; } = arguments;

    /// <summary>1-based monitor, left to right; null = wherever the program opens.</summary>
    public int? Screen { get; init; }
    public bool Maximize { get; init; }

    public override void Press(IActionHost host)
    {
        bool place = Screen != null || Maximize;
        var (file, args) = place ? InNewWindow() : (File, Arguments);
        try
        {
            var before = place ? WindowPlacer.Snapshot() : null;
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true })?.Dispose();
            if (before != null)
                WindowPlacer.PlaceNextWindow(before, Screen, Maximize);
        }
        catch (Exception e)
        {
            Log.Error($"run: {file} {args}", e);
        }
    }

    static bool IsUrl(string s) => s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A link would open as a tab in an existing browser window, leaving nothing new to move,
    /// so ask the browser for a new window.
    /// </summary>
    (string File, string Arguments) InNewWindow()
    {
        if (IsUrl(File) && DefaultApps.Browser() is { } browser)
            return (browser, $"--new-window {File}");
        string name = Path.GetFileNameWithoutExtension(File).ToLowerInvariant();
        if (IsUrl(Arguments) && BrowserNames.Any(name.Contains))
            return (File, $"--new-window {Arguments}");
        return (File, Arguments);
    }
}

/// <summary>"dial:next" / "dial:prev" / "dial:Zoom".</summary>
internal sealed class DialModeAction(string target) : KdAction
{
    public string Target { get; } = target;

    public override void Press(IActionHost host)
    {
        switch (Target.ToLowerInvariant())
        {
            case "next": host.CycleDialMode(1); break;
            case "prev": host.CycleDialMode(-1); break;
            default: host.SelectDialMode(Target); break;
        }
    }
}

/// <summary>"layer": second layer while held. "layer:toggle": switches it on/off.</summary>
internal sealed class LayerAction(bool toggle) : KdAction
{
    public bool Toggle { get; } = toggle;

    public override void Press(IActionHost host)
    {
        if (Toggle)
            host.ToggleLayer();
        else
            host.HoldLayer(true);
    }

    public override void Release(IActionHost host)
    {
        if (!Toggle)
            host.HoldLayer(false);
    }
}
