namespace SchneiderLoader;

internal sealed class LampAddressMap : Form
{
    private const int Columns = 8;
    private const int CellPitch = 30;
    private const int GridSize = Columns * CellPitch;
    private const int LineWidth = GridSize + 22;
    private const int LineHeight = GridSize + 36;
    private static readonly Color Registered = Color.FromArgb(20, 119, 69);
    private readonly Label[] _cells = new Label[128];
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 32, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolTip _tips = new();

    public LampAddressMap()
    {
        Text = "Mappa indirizzi lampade";
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(562, 338);
        MinimumSize = new Size(300, 220);
        Padding = new Padding(8);
        BackColor = Color.White;

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var lineA = CreateLine(0);
        var lineB = CreateLine(1);
        lineB.Left = LineWidth + 8;
        scroll.Controls.AddRange([lineA, lineB]);
        Controls.Add(scroll);
        Controls.Add(_status);
        ShowProgress("Grigio: da leggere · Bianco: libero · Verde: registrato");
        Disposed += (_, _) => _tips.Dispose();
    }

    private Control CreateLine(int line)
    {
        char lineName = (char)('A' + line);
        var group = new GroupBox { Text = $"Linea {lineName}", Size = new Size(LineWidth, LineHeight) };
        var grid = new TableLayoutPanel
        {
            Location = new Point(11, 25), Size = new Size(GridSize, GridSize),
            ColumnCount = Columns, RowCount = Columns, GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        for (int i = 0; i < Columns; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / Columns));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / Columns));
        }
        for (int number = 0; number < 64; number++)
        {
            var cell = new Label
            {
                Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Gainsboro, ForeColor = Color.FromArgb(27, 43, 59),
                Margin = new Padding(2), Padding = Padding.Empty, AccessibleName = $"{lineName}{number:00}: da leggere"
            };
            _cells[line * 64 + number] = cell;
            _tips.SetToolTip(cell, cell.AccessibleName);
            grid.Controls.Add(cell, number % Columns, number / Columns);
        }
        group.Controls.Add(grid);
        return group;
    }

    public void SetAddress(LampAddressStatus status)
    {
        var cell = _cells[status.Index];
        cell.BackColor = status.Assigned ? Registered : Color.White;
        cell.ForeColor = status.Assigned ? Color.White : Color.FromArgb(27, 43, 59);
        cell.AccessibleName = SchneiderProtocol.DeviceStatus(status);
        _tips.SetToolTip(cell, cell.AccessibleName);
    }

    public void ShowProgress(string text) => _status.Text = text;
}
