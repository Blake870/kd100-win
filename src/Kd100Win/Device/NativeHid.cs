using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Kd100Win.Device;

internal static partial class NativeHid
{
    const uint GENERIC_READ = 0x80000000;
    const uint GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1;
    const uint FILE_SHARE_WRITE = 2;
    const uint OPEN_EXISTING = 3;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetIndexedString(SafeFileHandle device, uint index, Span<byte> buffer, uint length);

    /// <summary>
    /// Reads a USB string descriptor by index through the HID class driver.
    /// Reading index 200 is what flips the KD100 into vendor mode.
    /// </summary>
    public static void GetIndexedString(string devicePath, int index)
    {
        using var handle = Open(devicePath);
        Span<byte> buffer = stackalloc byte[256];
        if (!HidD_GetIndexedString(handle, (uint)index, buffer, (uint)buffer.Length))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    static SafeFileHandle Open(string path)
    {
        // Same fallback as hidapi: some collections only open without access rights.
        var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);
        if (!h.IsInvalid)
            return h;
        h.Dispose();
        h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);
        if (h.IsInvalid)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return h;
    }
}
