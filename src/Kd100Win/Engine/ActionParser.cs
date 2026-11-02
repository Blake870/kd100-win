using Kd100Win.Input;

namespace Kd100Win.Engine;

/// <summary>Parses the action strings used in config.json. See README "Actions".</summary>
internal static class ActionParser
{
    public static KdAction Parse(string? text)
    {
        string s = (text ?? "").Trim();
        if (s.Length == 0 || s.Equals("none", StringComparison.OrdinalIgnoreCase))
            return NoAction.Instance;

        int colon = s.IndexOf(':');
        string prefix = colon > 0 ? s[..colon].ToLowerInvariant() : "";
        string arg = colon > 0 ? s[(colon + 1)..].Trim() : "";

        return prefix switch
        {
            "keys" => new SequenceAction(arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(ParseChord).ToList()) { Source = s },
            "text" => new TextAction(s[(colon + 1)..]) { Source = s },
            "wheel" => ParseWheel(arg, s),
            "mouse" => new MouseAction(ParseMouseButton(arg)) { Source = s },
            "run" => ParseRun(arg, s, ""),
            _ when prefix.StartsWith("run@") => ParseRun(arg, s, prefix[4..]),
            "dial" => arg.Length > 0 ? new DialModeAction(arg) { Source = s } : throw Error(s, "dial: needs next, prev or a mode name"),
            "layer" => arg.Equals("toggle", StringComparison.OrdinalIgnoreCase)
                ? new LayerAction(true) { Source = s }
                : throw Error(s, "use \"layer\" (hold) or \"layer:toggle\""),
            _ when s.Equals("layer", StringComparison.OrdinalIgnoreCase) => new LayerAction(false) { Source = s },
            _ => ParseChord(s),
        };
    }

    public static ChordAction ParseChord(string chord)
    {
        var keys = new List<ushort>();
        // "ctrl+plus" is fine; a bare "+" key cannot be written, use "plus".
        foreach (var part in chord.Split('+'))
        {
            if (!KeyNames.TryGet(part, out ushort vk))
                throw Error(chord, $"unknown key \"{part.Trim()}\"");
            keys.Add(vk);
        }
        // Modifiers first, whatever order the user wrote them in.
        var ordered = keys.Where(KeyNames.Modifiers.Contains).Concat(keys.Where(k => !KeyNames.Modifiers.Contains(k))).ToList();
        return new ChordAction(ordered) { Source = chord };
    }

    static WheelAction ParseWheel(string arg, string source)
    {
        var parts = arg.Split(':', StringSplitOptions.TrimEntries);
        int amount = 120;
        if (parts.Length > 1 && (!int.TryParse(parts[1], out amount) || amount <= 0))
            throw Error(source, "wheel amount must be a positive number (120 = one notch)");
        return parts[0].ToLowerInvariant() switch
        {
            // Windows: positive vertical delta scrolls up, positive horizontal scrolls right.
            "up" => new WheelAction(amount, false) { Source = source },
            "down" => new WheelAction(-amount, false) { Source = source },
            "right" => new WheelAction(amount, true) { Source = source },
            "left" => new WheelAction(-amount, true) { Source = source },
            _ => throw Error(source, "wheel direction must be up, down, left or right"),
        };
    }

    static MouseButtons ParseMouseButton(string arg) => arg.ToLowerInvariant() switch
    {
        "left" => MouseButtons.Left,
        "right" => MouseButtons.Right,
        "middle" => MouseButtons.Middle,
        "back" => MouseButtons.XButton1,
        "forward" => MouseButtons.XButton2,
        _ => throw Error("mouse:" + arg, "mouse button must be left, right, middle, back or forward"),
    };

    /// <param name="options">After "run@": a monitor number and/or "max", comma-separated, e.g. "2,max".</param>
    static RunAction ParseRun(string arg, string source, string options)
    {
        if (arg.Length == 0)
            throw Error(source, "run: needs a program");
        int? screen = null;
        bool maximize = false;
        foreach (var option in options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (option == "max")
                maximize = true;
            else if (int.TryParse(option, out int n) && n is >= 1 and <= 16)
                screen = n;
            else
                throw Error(source, $"unknown run option \"{option}\" (use a monitor number and/or max, e.g. run@2,max:)");
        }
        if (arg[0] == '"')
        {
            int end = arg.IndexOf('"', 1);
            if (end < 0)
                throw Error(source, "missing closing quote");
            return new RunAction(arg[1..end], arg[(end + 1)..].Trim()) { Source = source, Screen = screen, Maximize = maximize };
        }
        int space = arg.IndexOf(' ');
        return space < 0
            ? new RunAction(arg, "") { Source = source, Screen = screen, Maximize = maximize }
            : new RunAction(arg[..space], arg[(space + 1)..].Trim()) { Source = source, Screen = screen, Maximize = maximize };
    }

    static FormatException Error(string action, string message) => new($"\"{action}\": {message}");
}
