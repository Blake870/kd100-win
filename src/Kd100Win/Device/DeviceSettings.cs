using Microsoft.Win32.SafeHandles;

namespace Kd100Win.Device;

/// <summary>LED brightness and sleep time as stored in the KD100.</summary>
internal sealed record DeviceSettingsValues(int Brightness, int SleepMinutes);

/// <summary>
/// Reads and changes the settings the KD100 keeps itself. Like the vendor-mode switch they are
/// string-descriptor reads (see docs/protocol.md). They only work over the USB cable: through
/// the receiver the steps are ignored and the queries answer with stale values.
/// </summary>
internal static class DeviceSettings
{
    /// <summary>Brightness levels in step order; 0 turns the LED off.</summary>
    public static readonly int[] BrightnessLevels = [0, 1, 2, 4, 8, 16];

    /// <summary>Sleep times in minutes in step order; 0 means never.</summary>
    public static readonly int[] SleepLevels = [0, 15, 30, 60, 90, 120];

    const int BrightnessUp = 0xD7, BrightnessDown = 0xD8, BrightnessQuery = 0xD9;
    const int SleepUp = 0xDA, SleepDown = 0xDB, SleepQuery = 0xDC;

    public static DeviceSettingsValues Read(string devicePath)
    {
        using var handle = NativeHid.Open(devicePath);
        return Read(handle);
    }

    /// <summary>Steps each given setting to its target value and returns what the device reports afterwards.</summary>
    public static DeviceSettingsValues Apply(string devicePath, int? brightness, int? sleepMinutes)
    {
        using var handle = NativeHid.Open(devicePath);
        if (brightness is int b)
            StepTo(handle, BrightnessLevels, Query(handle, BrightnessQuery), b, BrightnessUp, BrightnessDown);
        if (sleepMinutes is int s)
            StepTo(handle, SleepLevels, Query(handle, SleepQuery), s, SleepUp, SleepDown);
        return Read(handle);
    }

    static DeviceSettingsValues Read(SafeFileHandle handle) =>
        new(Query(handle, BrightnessQuery), Query(handle, SleepQuery));

    static void StepTo(SafeFileHandle handle, int[] levels, int current, int target, int up, int down)
    {
        int to = Array.IndexOf(levels, target);
        if (to < 0)
            throw new ArgumentOutOfRangeException(nameof(target), target, $"expected one of {string.Join(", ", levels)}");
        int from = Array.IndexOf(levels, current);
        if (from < 0)
            throw new InvalidOperationException($"The device reported an unexpected value {current}");
        for (int i = from; i != to; i += Math.Sign(to - from))
        {
            NativeHid.GetIndexedString(handle, to > from ? up : down);
            Thread.Sleep(50); // the Huion driver waits as long between steps
        }
    }

    // Each request is sometimes answered with the reply to the previous one (seen after other
    // strings, such as the product name, were read), so ask twice and use the second answer.
    static int Query(SafeFileHandle handle, int index)
    {
        NativeHid.GetIndexedString(handle, index);
        return NativeHid.GetIndexedString(handle, index)[0];
    }
}
