using Kd100Win.Config;
using Kd100Win.Device;
using Kd100Win.Engine;

namespace Kd100Win.Tests;

public class ConfigTests
{
    [Fact]
    public void Sample_config_is_valid()
    {
        var config = ConfigStore.Parse(ConfigStore.SampleText());
        Assert.Equal("Default", config.Default.Name);
        Assert.Equal("Premiere Pro", config.ForProcess("Adobe Premiere Pro").Name);
        Assert.Equal("Browser", config.ForProcess("CHROME").Name);
        Assert.Same(config.Default, config.ForProcess("notepad"));
        Assert.Same(config.Default, config.ForProcess(null));
    }

    [Fact]
    public void Every_sample_profile_maps_every_button()
    {
        var config = ConfigStore.Parse(ConfigStore.SampleText());
        foreach (var p in config.Profiles)
        {
            for (int b = 0; b < Kd100Protocol.ButtonCount; b++)
                Assert.True((p.Buttons[b] ?? config.Default.Buttons[b]) != null, $"{p.Name}: button {b} is not mapped");
            Assert.NotEmpty(p.Dial);
        }
    }

    [Fact]
    public void Button_gestures_and_menus()
    {
        const string json = """
            {
              "menus": { "m": [ { "label": "A", "action": "run:notepad.exe" }, { "action": "win+e" } ] },
              "profiles": [ { "name": "D", "buttons": { "0": { "tap": "a", "double": "b", "hold": "layer", "menu": "M" } } } ]
            }
            """;
        var b = ConfigStore.Parse(json).Default.Buttons[0]!;
        Assert.False(b.IsSimple);
        Assert.IsType<ChordAction>(b.Tap);
        Assert.IsType<ChordAction>(b.Double);
        Assert.IsType<LayerAction>(b.Hold);
        Assert.Equal(["A", "win+e"], b.Menu!.Items.Select(i => i.Label));
    }

    [Theory]
    [InlineData("""{ "profiles": [ { "name": "D", "buttons": { "0": { "menu": "missing" } } } ] }""", "no menu named \"missing\"")]
    [InlineData("""{ "profiles": [ { "name": "D", "buttons": { "0": { "tapp": "a" } } } ] }""", "unknown button property")]
    [InlineData("""{ "menus": { "m": [ { "action": "a" } ] }, "profiles": [ { "name": "D" } ] }""", "2 to 12 items")]
    [InlineData("""{ "holdMs": 5, "profiles": [ { "name": "D" } ] }""", "holdMs")]
    public void Gesture_errors(string json, string message)
    {
        var e = Assert.Throws<FormatException>(() => ConfigStore.Parse(json));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void Errors_name_the_profile_and_button()
    {
        const string json = """
            { "profiles": [ { "name": "P", "buttons": { "3": "ctrl+nope", "25": "a" } } ] }
            """;
        var e = Assert.Throws<FormatException>(() => ConfigStore.Parse(json));
        Assert.Contains("profile \"P\", button 3", e.Message);
        Assert.Contains("unknown key \"nope\"", e.Message);
        Assert.Contains("button \"25\" does not exist", e.Message);
    }

    [Fact]
    public void A_default_profile_is_required()
    {
        const string json = """{ "profiles": [ { "name": "Only app", "apps": [ "x" ] } ] }""";
        var e = Assert.Throws<FormatException>(() => ConfigStore.Parse(json));
        Assert.Contains("without \"apps\"", e.Message);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        const string json = """
            // comment
            { "profiles": [ { "name": "D", "buttons": { "dial": "dial:next", }, }, ], }
            """;
        var config = ConfigStore.Parse(json);
        Assert.IsType<DialModeAction>(config.Default.Buttons[Kd100Protocol.DialButton]!.Tap);
    }

    [Fact]
    public void App_names_may_include_exe()
    {
        const string json = """{ "profiles": [ { "name": "D" }, { "name": "B", "apps": [ "Chrome.EXE" ] } ] }""";
        Assert.Equal("B", ConfigStore.Parse(json).ForProcess("chrome").Name);
    }
}
