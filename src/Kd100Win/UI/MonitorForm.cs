namespace Kd100Win.UI;

/// <summary>Shows live button/dial events and the log, to help write the config.</summary>
internal sealed class MonitorForm : Form
{
    const int MaxLines = 500;

    const string ButtonMap = """
        Button numbers (dial on top):

                 ( dial: 18 )
          ┌────┬────┬────┬────┐
          │  0 │  1 │  2 │  3 │
          ├────┼────┼────┼────┤
          │  4 │  5 │  6 │  7 │
          ├────┼────┼────┼────┤
          │  8 │  9 │ 10 │ 11 │
          ├────┼────┼────┼────┤
          │ 12 │ 13 │ 14 │    │
          ├────┴────┼────┤ 15 │
          │   16    │ 17 │    │
          └─────────┴────┴────┘
        """;

    readonly ListBox _list;
    readonly Label _status;

    public MonitorForm(Icon icon)
    {
        Text = "kd100-win: input monitor";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(LogicalToDeviceUnits(900), LogicalToDeviceUnits(520));
        MinimumSize = new Size(LogicalToDeviceUnits(600), LogicalToDeviceUnits(360));

        var layout = new Label
        {
            Text = ButtonMap,
            Dock = DockStyle.Left,
            Width = LogicalToDeviceUnits(260),
            Font = new Font("Consolas", 10f),
            Padding = new Padding(LogicalToDeviceUnits(8)),
        };
        _status = new Label
        {
            Dock = DockStyle.Top,
            Height = LogicalToDeviceUnits(48),
            Padding = new Padding(LogicalToDeviceUnits(6)),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        };
        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.5f),
            IntegralHeight = false,
            HorizontalScrollbar = true,
        };
        Controls.Add(_list);
        Controls.Add(_status);
        Controls.Add(layout);
    }

    public void SetStatus(string text)
    {
        if (!IsDisposed)
            _status.Text = text;
    }

    /// <summary>Thread-safe.</summary>
    public void Append(string line)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(() => Append(line));
            return;
        }
        _list.BeginUpdate();
        _list.Items.Add($"{DateTime.Now:HH:mm:ss.fff}  {line}");
        while (_list.Items.Count > MaxLines)
            _list.Items.RemoveAt(0);
        _list.TopIndex = _list.Items.Count - 1;
        _list.EndUpdate();
    }
}
