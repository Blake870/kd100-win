namespace Kd100Win.UI;

/// <summary>A small click-through popup near the bottom of the screen, e.g. "Premiere · Zoom".</summary>
internal sealed class OsdForm : Form
{
    const int WS_EX_TOPMOST = 0x8;
    const int WS_EX_TRANSPARENT = 0x20;
    const int WS_EX_TOOLWINDOW = 0x80;
    const int WS_EX_NOACTIVATE = 0x08000000;

    readonly System.Windows.Forms.Timer _hide = new() { Interval = 1300 };
    string _text = "";

    public OsdForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 32, 38);
        ForeColor = Color.White;
        Opacity = 0.92;
        Font = new Font("Segoe UI", 14f, FontStyle.Regular);
        DoubleBuffered = true;
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void ShowText(string text)
    {
        _text = text;
        var size = TextRenderer.MeasureText(text, Font);
        int pad = LogicalToDeviceUnits(18);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var bounds = new Rectangle(0, 0, size.Width + pad * 2, size.Height + pad);
        bounds.X = area.Left + (area.Width - bounds.Width) / 2;
        bounds.Y = area.Bottom - bounds.Height - LogicalToDeviceUnits(80);
        Bounds = bounds;
        using (var path = RoundedRect(new Rectangle(Point.Empty, bounds.Size), LogicalToDeviceUnits(12)))
            Region = new Region(path);
        Invalidate();
        if (!Visible)
            Show();
        _hide.Stop();
        _hide.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, _text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _hide.Dispose();
        base.Dispose(disposing);
    }
}
