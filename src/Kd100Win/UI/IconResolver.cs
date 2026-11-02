using System.Runtime.InteropServices;
using Kd100Win.Engine;
using Microsoft.Win32;

namespace Kd100Win.UI;

/// <summary>Finds an icon for a radial-menu item: its "icon" setting, or the program/folder/browser it opens.</summary>
internal static class IconResolver
{
    static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? For(RadialMenuItem item) =>
        item.Icon != null ? Get(item.Icon, () => FromFile(item.Icon))
        : item.Action is RunAction run ? ForTarget(run.File)
        : null;

    /// <summary>Icon of a program, folder, file or URL (the default browser's icon).</summary>
    public static Bitmap? ForTarget(string target) => Get(target, () => FromTarget(target));

    /// <summary>Icon of an image/.ico/.exe file given explicitly.</summary>
    public static Bitmap? ForIconFile(string path) => Get("file:" + path, () => FromFile(path));

    public static string? ToDataUrl(Bitmap? bitmap)
    {
        if (bitmap == null)
            return null;
        using var ms = new MemoryStream();
        lock (bitmap)
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    static Bitmap? Get(string key, Func<Bitmap?> load)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
        }
        Bitmap? bitmap;
        try
        {
            bitmap = load();
        }
        catch (Exception e)
        {
            Log.Error($"icon for {key}", e);
            bitmap = null;
        }
        lock (Cache)
            Cache[key] = bitmap;
        return bitmap;
    }

    public static void Clear()
    {
        // Bitmaps may still be in use by an open menu, so just drop them for the GC.
        lock (Cache)
            Cache.Clear();
    }

    static Bitmap? FromFile(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"
            ? new Bitmap(path)
            : ShellIcon(path);
    }

    static Bitmap? FromTarget(string target)
    {
        target = Environment.ExpandEnvironmentVariables(target);
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return ShellItemImage(target, 48);
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return Platform.DefaultApps.Browser() is { } browser ? ShellIcon(browser) : null;
        return ResolveProgram(target) is { } path ? ShellIcon(path) : null;
    }

    /// <summary>Like the Run dialog: full paths, App Paths registrations (chrome, msedge...), then PATH.</summary>
    public static string? ResolveProgram(string name)
    {
        if (Path.IsPathRooted(name))
            return File.Exists(name) || Directory.Exists(name) ? name : null;

        string exe = Path.HasExtension(name) ? name : name + ".exe";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string registered)
            {
                registered = registered.Trim('"');
                if (File.Exists(registered))
                    return registered;
            }
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir.Trim(), exe);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>Icon of a file, folder, shortcut or shell:AppsFolder item, with transparency.</summary>
    public static Bitmap? ShellIcon(string path) => ShellItemImage(path, 48) ?? SmallShellIcon(path);

    static Bitmap? ShellItemImage(string parsingName, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(parsingName, 0, ref iid, out var factory) != 0 || factory == null)
            return null;
        try
        {
            if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out nint hbitmap) != 0 || hbitmap == 0)
                return null;
            try
            {
                return WithAlpha(hbitmap);
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>Image.FromHbitmap drops the alpha channel; re-read the same pixels as premultiplied ARGB.</summary>
    static Bitmap WithAlpha(nint hbitmap)
    {
        using var opaque = Image.FromHbitmap(hbitmap);
        var rect = new Rectangle(0, 0, opaque.Width, opaque.Height);
        var data = opaque.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, opaque.PixelFormat);
        try
        {
            if (Image.GetPixelFormatSize(opaque.PixelFormat) != 32)
                return new Bitmap(opaque);
            using var view = new Bitmap(data.Width, data.Height, data.Stride, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, data.Scan0);
            return new Bitmap(view);
        }
        finally
        {
            opaque.UnlockBits(data);
        }
    }

    static Bitmap? SmallShellIcon(string path)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON) == 0 || info.hIcon == 0)
            return null;
        try
        {
            using var icon = Icon.FromHandle(info.hIcon);
            return icon.ToBitmap();
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    const int SIIGBF_BIGGERSIZEOK = 0x1;
    const int SIIGBF_ICONONLY = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out nint phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? item);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(nint obj);

    const uint SHGFI_ICON = 0x100;
    const uint SHGFI_LARGEICON = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern nint SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(nint icon);

}
