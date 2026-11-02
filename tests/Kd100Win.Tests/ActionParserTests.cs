using Kd100Win.Engine;

namespace Kd100Win.Tests;

public class ActionParserTests
{
    [Fact]
    public void Chords_put_modifiers_first()
    {
        var a = Assert.IsType<ChordAction>(ActionParser.Parse("z+shift+Ctrl"));
        Assert.Equal(new ushort[] { 0x10, 0x11, 0x5A }, a.Keys);
    }

    [Theory]
    [InlineData("[", 0xDB)]
    [InlineData("equals", 0xBB)]
    [InlineData("f13", 0x7C)]
    [InlineData("num5", 0x65)]
    [InlineData("volume_up", 0xAF)]
    [InlineData("pgdn", 0x22)]
    public void Key_names(string name, int vk) =>
        Assert.Equal((ushort)vk, Assert.IsType<ChordAction>(ActionParser.Parse(name)).Keys.Single());

    [Fact]
    public void Wheel_directions_and_amounts()
    {
        var down = Assert.IsType<WheelAction>(ActionParser.Parse("wheel:down"));
        Assert.Equal(-120, down.Delta);
        Assert.False(down.Horizontal);
        var right = Assert.IsType<WheelAction>(ActionParser.Parse("wheel:right:40"));
        Assert.Equal(40, right.Delta);
        Assert.True(right.Horizontal);
    }

    [Fact]
    public void Sequences_text_and_run()
    {
        Assert.Equal(2, Assert.IsType<SequenceAction>(ActionParser.Parse("keys:ctrl+a ctrl+c")).Chords.Count);
        Assert.Equal(" Hi: there", Assert.IsType<TextAction>(ActionParser.Parse("text: Hi: there")).Text);
        var run = Assert.IsType<RunAction>(ActionParser.Parse("run:\"C:\\Program Files\\App\\app.exe\" --flag x"));
        Assert.Equal("C:\\Program Files\\App\\app.exe", run.File);
        Assert.Equal("--flag x", run.Arguments);
    }

    [Fact]
    public void Run_on_a_chosen_screen()
    {
        var run = Assert.IsType<RunAction>(ActionParser.Parse("run@2,max:chrome https://youtube.com"));
        Assert.Equal(("chrome", "https://youtube.com", 2, true), (run.File, run.Arguments, run.Screen, run.Maximize));
        var plain = Assert.IsType<RunAction>(ActionParser.Parse("run:notepad.exe"));
        Assert.Equal((null, false), (plain.Screen, plain.Maximize));
        Assert.Throws<FormatException>(() => ActionParser.Parse("run@left:notepad.exe"));
    }

    [Fact]
    public void Layers_dial_mouse_none()
    {
        Assert.False(Assert.IsType<LayerAction>(ActionParser.Parse("layer")).Toggle);
        Assert.True(Assert.IsType<LayerAction>(ActionParser.Parse("layer:toggle")).Toggle);
        Assert.Equal("next", Assert.IsType<DialModeAction>(ActionParser.Parse("dial:next")).Target);
        Assert.Equal(MouseButtons.XButton1, Assert.IsType<MouseAction>(ActionParser.Parse("mouse:back")).Button);
        Assert.Same(NoAction.Instance, ActionParser.Parse(""));
        Assert.Same(NoAction.Instance, ActionParser.Parse("none"));
    }

    [Theory]
    [InlineData("ctrl+")]
    [InlineData("hyper+a")]
    [InlineData("wheel:sideways")]
    [InlineData("wheel:up:-5")]
    [InlineData("mouse:fourth")]
    [InlineData("layer:sometimes")]
    [InlineData("run:")]
    public void Invalid_actions_throw(string text) =>
        Assert.Throws<FormatException>(() => ActionParser.Parse(text));
}
