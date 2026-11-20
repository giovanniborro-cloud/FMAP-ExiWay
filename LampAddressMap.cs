namespace SchneiderLoader;

internal sealed class LampAddressMap : Form
{
    private readonly Label[] _cells = new Label[128];
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 34, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolTip _tips = new();

    public LampAddressMap()
    {
        Text = "Mappa indirizzi lampade";
        Font = new Font("Segoe UI", 9);
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 280);
        MinimumSize = new Size(300, 220);
        Padding = new Padding(8);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var lines = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
        lines.Controls.AddRange([CreateLine(0), CreateLine(1)]);
        scroll.Controls.Add(lines);
        Controls.Add(scroll);
        Controls.Add(_status);
        ShowProgress("Grigio: da leggere · Bianco: libero · Verde: registrato");
        Disposed += (_, _) => _tips.Dispose();
    }

    private Control CreateLine(int line)
    {
        char lineName = (char)('A' + line);
        var group = new GroupBox
        {
            Text = $"Linea {lineName}", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 5, 6, 6)
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 8, RowCount = 8,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        for (int number = 0; number < 64; number++)
        {
            var cell = new Label
            {
                Text = number.ToString(), Size = new Size(20, 20), TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Gainsboro, Margin = new Padding(2),
                AccessibleName = $"{lineName}{number:00}: da leggere"
            };
            _cells[line * 64 + number] = cell;
            _tips.SetToolTip(cell, cell.AccessibleName);
            grid.Controls.Add(cell, number % 8, number / 8);
        }
        group.Controls.Add(grid);
        return group;
    }

    public void SetAddress(LampAddressStatus status)
    {
        var cell = _cells[status.Index];
        cell.BackColor = status.Assigned ? Color.FromArgb(183, 231, 196) : Color.White;
        cell.AccessibleName = SchneiderProtocol.DeviceStatus(status);
        _tips.SetToolTip(cell, cell.AccessibleName);
    }

    public void ShowProgress(string text) => _status.Text = text;
}
