using Kd100Win.Config;
using Kd100Win.Device;

namespace Kd100Win.Engine;

internal sealed record DialMode(string Name, KdAction Cw, KdAction Ccw, int Step, double Acceleration);

internal sealed record RadialMenuItem(string Label, KdAction Action, string? Icon);

internal sealed record RadialMenu(string Name, IReadOnlyList<RadialMenuItem> Items);

/// <summary>What a button does. A binding with only Tap acts immediately and can be held.</summary>
internal sealed record ButtonBinding(KdAction Tap, KdAction? Double, KdAction? Hold, RadialMenu? Menu)
{
    public bool IsSimple => Double == null && Hold == null && Menu == null;

    public override string ToString()
    {
        if (IsSimple)
            return Tap.ToString();
        var parts = new List<string>();
        if (Tap != NoAction.Instance)
            parts.Add($"tap: {Tap}");
        if (Double != null)
            parts.Add($"double: {Double}");
        if (Hold != null)
            parts.Add($"hold: {Hold}");
        if (Menu != null)
            parts.Add($"menu: {Menu.Name}");
        return string.Join(", ", parts);
    }
}

/// <summary>A validated profile with every action parsed, ready for the engine.</summary>
internal sealed class Profile
{
    public required string Name { get; init; }
    public required HashSet<string> Apps { get; init; }
    public required ButtonBinding?[] Buttons { get; init; }
    public required ButtonBinding?[] LayerButtons { get; init; }
    public required IReadOnlyList<DialMode> Dial { get; init; }
    public required IReadOnlyList<DialMode> LayerDial { get; init; }

    public bool IsDefault => Apps.Count == 0;
}

internal sealed class CompiledConfig
{
    public required string Language { get; init; }
    public required bool ShowOsd { get; init; }
    public required int HoldMs { get; init; }
    public required int DoubleMs { get; init; }
    public required Profile Default { get; init; }
    public required IReadOnlyList<Profile> Profiles { get; init; }

    public Profile ForProcess(string? processName)
    {
        if (processName != null)
        {
            foreach (var p in Profiles)
            {
                if (p.Apps.Contains(processName))
                    return p;
            }
        }
        return Default;
    }

    /// <summary>Validates and parses a config. Throws FormatException listing every problem found.</summary>
    public static CompiledConfig Compile(AppConfig config)
    {
        var errors = new List<string>();
        var menus = CompileMenus(config.Menus, errors);
        var profiles = new List<Profile>();
        foreach (var pc in config.Profiles)
        {
            string where = string.IsNullOrWhiteSpace(pc.Name) ? $"profile #{profiles.Count + 1}" : $"profile \"{pc.Name}\"";
            profiles.Add(new Profile
            {
                Name = string.IsNullOrWhiteSpace(pc.Name) ? $"Profile {profiles.Count + 1}" : pc.Name,
                Apps = pc.Apps.Select(NormalizeApp).Where(a => a.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase),
                Buttons = CompileButtons(pc.Buttons, menus, where, errors),
                LayerButtons = CompileButtons(pc.Layer?.Buttons ?? [], menus, where + " layer", errors),
                Dial = CompileDial(pc.Dial, where, errors),
                LayerDial = CompileDial(pc.Layer?.Dial ?? [], where + " layer", errors),
            });
        }

        var defaults = profiles.Where(p => p.IsDefault).ToList();
        if (defaults.Count == 0)
            errors.Add("there must be one profile without \"apps\" (the default profile)");
        else if (defaults.Count > 1)
            errors.Add($"only one profile may have no \"apps\", found: {string.Join(", ", defaults.Select(p => p.Name))}");
        string language = (config.Language ?? "auto").Trim().ToLowerInvariant();
        if (language is not ("auto" or "en" or "ru"))
            errors.Add("language must be \"auto\", \"en\" or \"ru\"");
        if (config.HoldMs is < 100 or > 5000)
            errors.Add("holdMs must be between 100 and 5000");
        if (config.DoubleMs is < 100 or > 2000)
            errors.Add("doubleMs must be between 100 and 2000");

        if (errors.Count > 0)
            throw new FormatException(string.Join(Environment.NewLine, errors));

        return new CompiledConfig
        {
            Language = language,
            ShowOsd = config.ShowOsd,
            HoldMs = config.HoldMs,
            DoubleMs = config.DoubleMs,
            Default = defaults[0],
            Profiles = profiles,
        };
    }

    public static string NormalizeApp(string app)
    {
        string a = app.Trim();
        return a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a[..^4] : a;
    }

    static Dictionary<string, RadialMenu> CompileMenus(Dictionary<string, List<MenuItemConfig>> menus, List<string> errors)
    {
        var result = new Dictionary<string, RadialMenu>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, items) in menus)
        {
            var compiled = new List<RadialMenuItem>();
            foreach (var item in items)
            {
                try
                {
                    var action = ActionParser.Parse(item.Action);
                    string label = string.IsNullOrWhiteSpace(item.Label) ? item.Action : item.Label;
                    compiled.Add(new RadialMenuItem(label, action, item.Icon));
                }
                catch (FormatException e)
                {
                    errors.Add($"menu \"{name}\", item \"{item.Label}\": {e.Message}");
                }
            }
            if (items.Count is < 2 or > 12)
                errors.Add($"menu \"{name}\" must have 2 to 12 items");
            result[name] = new RadialMenu(name, compiled);
        }
        return result;
    }

    static ButtonBinding?[] CompileButtons(Dictionary<string, ButtonConfig> buttons, Dictionary<string, RadialMenu> menus,
        string where, List<string> errors)
    {
        var result = new ButtonBinding?[Kd100Protocol.ButtonCount];
        foreach (var (key, value) in buttons)
        {
            int index = key.Equals("dial", StringComparison.OrdinalIgnoreCase) ? Kd100Protocol.DialButton
                : int.TryParse(key, out int n) ? n : -1;
            if (index < 0 || index >= Kd100Protocol.ButtonCount)
            {
                errors.Add($"{where}: button \"{key}\" does not exist (use 0-18 or \"dial\")");
                continue;
            }
            try
            {
                RadialMenu? menu = null;
                if (value.Menu != null && !menus.TryGetValue(value.Menu, out menu))
                    throw new FormatException($"there is no menu named \"{value.Menu}\" in \"menus\"");
                result[index] = new ButtonBinding(
                    ActionParser.Parse(value.Tap),
                    value.Double == null ? null : ActionParser.Parse(value.Double),
                    value.Hold == null ? null : ActionParser.Parse(value.Hold),
                    menu);
            }
            catch (FormatException e)
            {
                errors.Add($"{where}, button {key}: {e.Message}");
            }
        }
        return result;
    }

    static List<DialMode> CompileDial(List<DialModeConfig> modes, string where, List<string> errors)
    {
        var result = new List<DialMode>();
        foreach (var m in modes)
        {
            string name = string.IsNullOrWhiteSpace(m.Name) ? $"Mode {result.Count + 1}" : m.Name;
            try
            {
                if (m.Step < 1)
                    throw new FormatException("step must be 1 or more");
                if (m.Acceleration < 0)
                    throw new FormatException("acceleration must be 0 or more");
                result.Add(new DialMode(name, ActionParser.Parse(m.Cw), ActionParser.Parse(m.Ccw), m.Step, m.Acceleration));
            }
            catch (FormatException e)
            {
                errors.Add($"{where}, dial mode \"{name}\": {e.Message}");
            }
        }
        return result;
    }
}
