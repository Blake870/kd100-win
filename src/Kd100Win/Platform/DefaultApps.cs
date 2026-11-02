using System.Runtime.InteropServices;
using System.Text;

namespace Kd100Win.Platform;

internal static class DefaultApps
{
    const int ASSOCSTR_EXECUTABLE = 2;

    /// <summary>Full path of the program that opens https:// links.</summary>
    public static string? Browser()
    {
        uint size = 1024;
        var sb = new StringBuilder((int)size);
        return AssocQueryString(0, ASSOCSTR_EXECUTABLE, "https", "open", sb, ref size) == 0 ? sb.ToString() : null;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int AssocQueryString(int flags, int str, string assoc, string? extra, StringBuilder? output, ref uint size);
}
