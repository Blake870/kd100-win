namespace Kd100Win.Device;

internal enum ReportKind
{
    Unknown,
    Buttons,
    Dial,
}

/// <summary>A decoded vendor-mode report. See docs/protocol.md.</summary>
internal readonly record struct Kd100Report(ReportKind Kind, int Buttons, int DialDirection);

internal static class Kd100Protocol
{
    public const int VendorId = 0x256C;
    public const int ProductId = 0x006D;

    /// <summary>Reading this USB string descriptor switches the KD100 into vendor mode.</summary>
    public const int ModeSwitchStringIndex = 200;

    public const int ReportId = 0x08;
    public const int ReportLength = 12;
    public const int ButtonCount = 19;
    public const int DialButton = 18;

    const byte TypeButtons = 0xE0;
    const byte TypeDial = 0xF1;

    public static Kd100Report Parse(ReadOnlySpan<byte> r)
    {
        if (r.Length < 7 || r[0] != ReportId)
            return new(ReportKind.Unknown, 0, 0);

        switch (r[1])
        {
            case TypeButtons:
                int mask = r[4] | (r[5] << 8) | (r[6] << 16);
                return new(ReportKind.Buttons, mask & ((1 << ButtonCount) - 1), 0);
            case TypeDial:
                int dir = r[5] switch { 1 => 1, 2 => -1, _ => 0 };
                return dir == 0 ? new(ReportKind.Unknown, 0, 0) : new(ReportKind.Dial, 0, dir);
            default:
                return new(ReportKind.Unknown, 0, 0);
        }
    }

    /// <summary>Human-readable position of a button, matching the layout in docs/protocol.md.</summary>
    public static string Describe(int button) => button switch
    {
        DialButton => "dial button",
        15 => "tall right key",
        16 => "wide bottom key",
        17 => "bottom middle key",
        >= 0 and < 15 => $"row {button / 4 + 1}, column {button % 4 + 1}",
        _ => $"button {button}",
    };
}
