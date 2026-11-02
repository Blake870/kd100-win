namespace Kd100Win.Input;

/// <summary>Maps key names used in the config ("ctrl", "f5", "[", "volume_up"...) to virtual-key codes.</summary>
internal static class KeyNames
{
    static readonly Dictionary<string, ushort> Map = Build();

    /// <summary>Keys that need KEYEVENTF_EXTENDEDKEY so apps see the navigation block, not the numpad.</summary>
    static readonly HashSet<ushort> Extended =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // pgup pgdn end home arrows
        0x2D, 0x2E, // insert delete
        0x5B, 0x5C, 0x5D, // win, apps
        0x6F, // numpad divide
        0xA3, 0xA5, // right ctrl, right alt
    ];

    public static readonly HashSet<ushort> Modifiers = [0x10, 0x11, 0x12, 0x5B, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];

    public static bool TryGet(string name, out ushort vk) => Map.TryGetValue(name.Trim().ToLowerInvariant(), out vk);

    public static bool IsExtended(ushort vk) => Extended.Contains(vk);

    public static IEnumerable<string> AllNames => Map.Keys;

    static Dictionary<string, ushort> Build()
    {
        var m = new Dictionary<string, ushort>
        {
            ["ctrl"] = 0x11, ["control"] = 0x11, ["shift"] = 0x10, ["alt"] = 0x12, ["win"] = 0x5B,
            ["lctrl"] = 0xA2, ["rctrl"] = 0xA3, ["lshift"] = 0xA0, ["rshift"] = 0xA1, ["lalt"] = 0xA4, ["ralt"] = 0xA5,
            ["enter"] = 0x0D, ["return"] = 0x0D, ["esc"] = 0x1B, ["escape"] = 0x1B, ["tab"] = 0x09,
            ["space"] = 0x20, ["backspace"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E,
            ["insert"] = 0x2D, ["ins"] = 0x2D, ["home"] = 0x24, ["end"] = 0x23,
            ["pageup"] = 0x21, ["pgup"] = 0x21, ["pagedown"] = 0x22, ["pgdn"] = 0x22,
            ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
            ["capslock"] = 0x14, ["printscreen"] = 0x2C, ["pause"] = 0x13, ["menu"] = 0x5D, ["apps"] = 0x5D,
            ["numlock"] = 0x90, ["scrolllock"] = 0x91,
            ["multiply"] = 0x6A, ["add"] = 0x6B, ["subtract"] = 0x6D, ["decimal"] = 0x6E, ["divide"] = 0x6F,
            // Punctuation by US-layout position, so shortcuts work whatever layout is active.
            ["-"] = 0xBD, ["minus"] = 0xBD, ["="] = 0xBB, ["equals"] = 0xBB, ["plus"] = 0xBB,
            ["["] = 0xDB, ["]"] = 0xDD, [";"] = 0xBA, ["'"] = 0xDE, [","] = 0xBC, ["comma"] = 0xBC,
            ["."] = 0xBE, ["period"] = 0xBE, ["/"] = 0xBF, ["slash"] = 0xBF, ["\\"] = 0xDC, ["backslash"] = 0xDC,
            ["`"] = 0xC0, ["backtick"] = 0xC0,
            ["volume_up"] = 0xAF, ["volume_down"] = 0xAE, ["volume_mute"] = 0xAD, ["mute"] = 0xAD,
            ["media_next"] = 0xB0, ["media_prev"] = 0xB1, ["media_stop"] = 0xB2, ["media_play_pause"] = 0xB3, ["play_pause"] = 0xB3,
            ["browser_back"] = 0xA6, ["browser_forward"] = 0xA7, ["browser_refresh"] = 0xA8, ["browser_home"] = 0xAC,
        };
        for (char c = 'a'; c <= 'z'; c++)
            m[c.ToString()] = (ushort)char.ToUpperInvariant(c);
        for (char c = '0'; c <= '9'; c++)
        {
            m[c.ToString()] = c;
            m["num" + c] = (ushort)(0x60 + (c - '0'));
        }
        for (int i = 1; i <= 24; i++)
            m["f" + i] = (ushort)(0x6F + i);
        return m;
    }
}
