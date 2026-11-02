using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kd100Win.Config;

// The on-disk shape of config.json. Property names are matched case-insensitively.

internal sealed class AppConfig
{
    /// <summary>"auto" (follow Windows), "en" or "ru".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Show the on-screen popup when the profile, dial mode or layer changes.</summary>
    public bool ShowOsd { get; set; } = true;

    /// <summary>How long a button must be held for its "hold" action.</summary>
    public int HoldMs { get; set; } = 400;

    /// <summary>How long to wait for the second press of a "double" action.</summary>
    public int DoubleMs { get; set; } = 300;

    /// <summary>Radial menus by name, opened by turning the dial while holding a button with "menu".</summary>
    public Dictionary<string, List<MenuItemConfig>> Menus { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ProfileConfig> Profiles { get; set; } = [];
}

internal sealed class ProfileConfig
{
    public string Name { get; set; } = "";

    /// <summary>Process names (e.g. "chrome", "Adobe Premiere Pro.exe"). Empty = the default profile.</summary>
    public List<string> Apps { get; set; } = [];

    /// <summary>Button number ("0".."18", or "dial" for 18) to what it does.</summary>
    public Dictionary<string, ButtonConfig> Buttons { get; set; } = [];

    public List<DialModeConfig> Dial { get; set; } = [];

    /// <summary>Second layer, active while a "layer" button is held (or toggled).</summary>
    public LayerConfig? Layer { get; set; }
}

/// <summary>
/// Either a plain action string (fires on press, held while the button is held) or an object
/// with gestures: { "tap": ..., "double": ..., "hold": ..., "menu": "name" }.
/// </summary>
[JsonConverter(typeof(ButtonConfigConverter))]
internal sealed class ButtonConfig
{
    public string? Tap { get; set; }
    public string? Double { get; set; }
    public string? Hold { get; set; }
    public string? Menu { get; set; }
}

internal sealed class LayerConfig
{
    public Dictionary<string, ButtonConfig> Buttons { get; set; } = [];

    /// <summary>Dial modes while the layer is active; empty = keep the profile's dial modes.</summary>
    public List<DialModeConfig> Dial { get; set; } = [];
}

internal sealed class DialModeConfig
{
    public string Name { get; set; } = "";
    public string Cw { get; set; } = "";
    public string Ccw { get; set; } = "";

    /// <summary>How many times the action fires per dial detent.</summary>
    public int Step { get; set; } = 1;

    /// <summary>Extra repeats when spinning fast: 0 = off, 2 = up to 3x at full speed.</summary>
    public double Acceleration { get; set; }
}

internal sealed class MenuItemConfig
{
    public string Label { get; set; } = "";
    public string Action { get; set; } = "";

    /// <summary>Optional .ico/.exe/.png for the menu; by default taken from the program being run.</summary>
    public string? Icon { get; set; }
}

internal sealed class ButtonConfigConverter : JsonConverter<ButtonConfig>
{
    public override ButtonConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new ButtonConfig { Tap = reader.GetString() };
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("a button must be an action string or an object like { \"tap\": ..., \"hold\": ... }");

        var result = new ButtonConfig();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            string? value = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            switch (name.ToLowerInvariant())
            {
                case "tap": result.Tap = value; break;
                case "double": result.Double = value; break;
                case "hold": result.Hold = value; break;
                case "menu": result.Menu = value; break;
                default: throw new JsonException($"unknown button property \"{name}\" (use tap, double, hold or menu)");
            }
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, ButtonConfig value, JsonSerializerOptions options) =>
        throw new NotSupportedException();
}
