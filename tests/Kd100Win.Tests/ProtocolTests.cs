using Kd100Win.Device;

namespace Kd100Win.Tests;

public class ProtocolTests
{
    static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    [Theory]
    [InlineData("08 e0 01 01 01 00 00 00 00 00 00 00", 0)]
    [InlineData("08 e0 01 01 80 00 00 00 00 00 00 00", 7)]
    [InlineData("08 e0 01 01 00 01 00 00 00 00 00 00", 8)]
    [InlineData("08 e0 01 01 00 80 00 00 00 00 00 00", 15)]
    [InlineData("08 e0 01 01 00 00 01 00 00 00 00 00", 16)]
    [InlineData("08 e0 01 01 00 00 02 00 00 00 00 00", 17)]
    [InlineData("08 e0 01 01 00 00 04 00 00 00 00 00", 18)]
    public void Button_bits_map_to_button_numbers(string report, int button)
    {
        var r = Kd100Protocol.Parse(Hex(report));
        Assert.Equal(ReportKind.Buttons, r.Kind);
        Assert.Equal(1 << button, r.Buttons);
    }

    [Fact]
    public void Chords_report_all_held_buttons()
    {
        var r = Kd100Protocol.Parse(Hex("08 e0 01 01 03 00 04 00 00 00 00 00"));
        Assert.Equal((1 << 0) | (1 << 1) | (1 << 18), r.Buttons);
    }

    [Theory]
    [InlineData("08 f1 01 01 00 01 00 00 00 00 00 00", 1)]
    [InlineData("08 f1 01 01 00 02 00 00 00 00 00 00", -1)]
    public void Dial_steps(string report, int direction)
    {
        var r = Kd100Protocol.Parse(Hex(report));
        Assert.Equal(ReportKind.Dial, r.Kind);
        Assert.Equal(direction, r.DialDirection);
    }

    [Theory]
    [InlineData("11 00 01 00 00 00 00 00 00")]
    [InlineData("08 f1 01 01 00 00 00 00 00 00 00 00")]
    [InlineData("08")]
    public void Other_reports_are_ignored(string report) =>
        Assert.Equal(ReportKind.Unknown, Kd100Protocol.Parse(Hex(report)).Kind);
}
