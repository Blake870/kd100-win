using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Kd100Win.Engine;

namespace Kd100Win.UI;

/// <summary>
/// The pie menu shown while a button is held and the dial is turned. A per-pixel-alpha
/// layered window, so it is translucent and anti-aliased, never takes focus and ignores clicks.
/// </summary>
internal sealed class RadialMenuForm : Form, IRadialMenuView
{
    const int WS_EX_LAYERED = 0x80000;
    const int WS_EX_TRANSPARENT = 0x20;
    const int WS_EX_TOOLWINDOW = 0x80;
    const int WS_EX_TOPMOST = 0x8;
    const int WS_EX_NOACTIVATE = 0x08000000;

    static readonly Color Segment = Color.FromArgb(215, 30, 32, 38);
    static readonly Color SegmentSelected = Color.FromArgb(235, 52, 150, 205);
    static readonly Color Edge = Color.FromArgb(70, 255, 255, 255);
    static readonly Color Center = Color.FromArgb(235, 22, 24, 28);

    public RadialMenuForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void Show(RadialMenu menu, int selected)
    {
        int size = LogicalToDeviceUnits(400);
        if (!Visible)
        {
            // Open around the mouse pointer, kept inside the screen.
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int x = Math.Clamp(Cursor.Position.X - size / 2, area.Left, area.Right - size);
            int y = Math.Clamp(Cursor.Position.Y - size / 2, area.Top, area.Bottom - size);
            Bounds = new Rectangle(x, y, size, size);
        }
        using (var bitmap = Render(menu, selected, size))
            Present(bitmap);
        if (!Visible)
            base.Show();
    }

    Bitmap Render(RadialMenu menu, int selected, int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        float c = size / 2f;
        float outer = c - LogicalToDeviceUnits(4);
        float inner = outer * 0.40f;
        int n = menu.Items.Count;
        float sweep = 360f / n;
        float gap = 2.5f;
        int iconSize = LogicalToDeviceUnits(32);

        using var labelFont = new Font("Segoe UI", 9f);
        using var centerFont = new Font("Segoe UI Semibold", 12f);
        using var smallFont = new Font("Segoe UI", 8f);
        using var edgePen = new Pen(Edge, LogicalToDeviceUnits(1));
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

        for (int i = 0; i < n; i++)
        {
            // Item 0 sits at the top; items go clockwise like the dial.
            float start = -90 - sweep / 2 + i * sweep + gap / 2;
            using var path = Ring(c, inner + LogicalToDeviceUnits(6), outer, start, sweep - gap);
            using (var fill = new SolidBrush(i == selected ? SegmentSelected : Segment))
                g.FillPath(fill, path);
            g.DrawPath(edgePen, path);

            double mid = (start + (sweep - gap) / 2) * Math.PI / 180;
            float r = (inner + outer) / 2 + LogicalToDeviceUnits(4);
            float x = c + (float)Math.Cos(mid) * r;
            float y = c + (float)Math.Sin(mid) * r;
            var item = menu.Items[i];
            var icon = IconResolver.For(item);
            float labelTop = y - LogicalToDeviceUnits(9);
            if (icon != null)
            {
                g.DrawImage(icon, x - iconSize / 2f, y - iconSize + LogicalToDeviceUnits(2), iconSize, iconSize);
                labelTop = y + LogicalToDeviceUnits(4);
            }
            float labelWidth = Math.Min(outer - inner, (float)(2 * Math.PI * r / n)) - LogicalToDeviceUnits(6);
            var labelRect = new RectangleF(x - labelWidth / 2, labelTop, labelWidth, LogicalToDeviceUnits(20));
            g.DrawString(item.Label, labelFont, Brushes.White, labelRect, center);
        }

        using (var fill = new SolidBrush(Center))
            g.FillEllipse(fill, c - inner, c - inner, inner * 2, inner * 2);
        g.DrawEllipse(edgePen, c - inner, c - inner, inner * 2, inner * 2);
        if (selected >= 0 && selected < n)
        {
            var box = new RectangleF(c - inner + 8, c - inner / 2, inner * 2 - 16, inner * 0.7f);
            g.DrawString(menu.Items[selected].Label, centerFont, Brushes.White, box, center);
        }
        using (var dim = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
        {
            var nameBox = new RectangleF(c - inner + 8, c + inner * 0.2f, inner * 2 - 16, inner * 0.5f);
            g.DrawString(menu.Name, smallFont, dim, nameBox, center);
        }
        return bmp;
    }

    static GraphicsPath Ring(float c, float inner, float outer, float start, float sweep)
    {
        var p = new GraphicsPath();
        p.AddArc(c - outer, c - outer, outer * 2, outer * 2, start, sweep);
        p.AddArc(c - inner, c - inner, inner * 2, inner * 2, start + sweep, -sweep);
        p.CloseFigure();
        return p;
    }

    void Present(Bitmap bitmap)
    {
        nint screen = GetDC(0);
        nint memory = CreateCompatibleDC(screen);
        nint hbitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        nint old = SelectObject(memory, hbitmap);
        try
        {
            var size = new SIZE { cx = bitmap.Width, cy = bitmap.Height };
            var source = new POINT();
            var dest = new POINT { x = Left, y = Top };
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screen, ref dest, ref size, memory, ref source, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(hbitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    void IRadialMenuView.Hide() => Hide();

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT pptDst, ref SIZE psize, nint hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("gdi32.dll")]
    static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(nint obj);
}
