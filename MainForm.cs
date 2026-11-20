using System.Text;

namespace SchneiderLoader;

internal sealed class MainForm : Form
{
    private readonly ComboBox _devices = new() { Width = 285, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _mac = new() { Width = 160, PlaceholderText = "AA:BB:CC:DD:EE:FF" };
    private readonly TextBox _name = new() { Width = 135, Text = "NCU33fab915" };
    private readonly TextBox _password = new() { Width = 130, Text = "33fab915", UseSystemPasswordChar = true };
    private readonly Button _scan = new() { Text = "Cerca NCU", AutoSize = true };
    private readonly Button _connect = new() { Text = "Connetti RFCOMM 1", AutoSize = true };
    private readonly Button _disconnect = new() { Text = "Disconnetti", AutoSize = true, Enabled = false };
    private readonly Button _login = new() { Text = "Login 323", AutoSize = true, Enabled = false };
    private readonly CheckBox _autoLogin = new() { Text = "Login automatico", AutoSize = true };
    private readonly ComboBox _lineTarget = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly FlowLayoutPanel _lineCommands = CreateCompactRow();
    private readonly ComboBox _savedNcus = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 276 };
    private readonly TextBox _building = new() { Width = 145, MaxLength = 80 };
    private readonly Button _saveNcu = new() { Text = "Salva NCU connessa", AutoSize = true, Enabled = false };
    private List<NcuProfile> _profiles = [];
    private string? _connectedMac;
    private string? _locationMac;
    private readonly NumericUpDown _exportControllerAddress = new() { Minimum = 1, Maximum = 50, Value = 1, Width = 54 };
    private readonly ComboBox _exportLines = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 87 };
    private readonly Button _findAddresses = new() { Text = "Trova indirizzi → Excel", AutoSize = true, Enabled = false };
    private readonly Button _lampStatuses = new() { Text = "Stato lampade → Excel", AutoSize = true, Enabled = false };
    private CancellationTokenSource? _clearLine;
    private TaskCompletionSource<string>? _clearReply;
    private string? _expectedClearCommand;
    private CancellationTokenSource? _addressScan;
    private TaskCompletionSource<LampAddressStatus>? _addressReply;
    private int _requestedAddress;
    private readonly Button _send = new() { Text = "Invia", AutoSize = true, Enabled = false };
    private readonly TextBox _command = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };
    private readonly TextBox _results = CreateLogView(true);
    private readonly TextBox _telegrams = CreateLogView(false);
    private readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OVA-GEN3", "Logs", $"sessione-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
    private Control _benchCommands = null!;
    private bool _logWriteFailed;
    private readonly Label _status = new() { AutoSize = true, Text = "Disconnesso" };
    private BluetoothRfcommClient? _client;
    private CancellationTokenSource? _reading;

    public MainForm()
    {
        Text = "OVA-GEN3 · Banco prova Exiway";
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(245, 247, 250);
        ForeColor = Color.FromArgb(27, 43, 59);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Padding = new Padding(12, 8, 12, 4)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var devices = CreateCompactRow();
        devices.Margin = Padding.Empty;

        using var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("OvaGen3.ico")!;
        using var embeddedIcon = new Icon(iconStream, 32, 32);
        var windowIcon = (Icon)embeddedIcon.Clone();
        Icon = windowIcon;
        var logoImage = embeddedIcon.ToBitmap();
        var logo = new PictureBox
        {
            Image = logoImage, Size = new Size(27, 27), SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(2, 2, 5, 2), AccessibleName = "Logo OVA-GEN3"
        };
        devices.Controls.Add(logo);
        Disposed += (_, _) =>
        {
            logoImage.Dispose();
            windowIcon.Dispose();
        };
        AddCompactField(devices, "Connessione Bluetooth", _devices);
        var discovery = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Margin = Padding.Empty
        };
        discovery.Controls.AddRange([_scan, _autoLogin]);
        devices.Controls.Add(discovery);
        AddCompactField(devices, "MAC", _mac);
        header.Controls.Add(devices);

        var auth = CreateCompactRow();
        AddCompactField(auth, "Nome NCU", _name);
        AddCompactField(auth, "Password app", _password);
        auth.Controls.AddRange([_connect, _disconnect, _login]);
        header.Controls.Add(auth);

        _lineTarget.Items.AddRange(["A", "B", "A + B"]);
        _lineTarget.SelectedIndex = 0;
        _lineCommands.Enabled = false;
        AddCompactField(_lineCommands, "Tutta la linea", _lineTarget);
        AddLineCommand("Accendi al massimo", 5);
        AddLineCommand("Spegni", 0);
        AddLineCommand("Identifica", 240);
        header.Controls.Add(_lineCommands);

        _benchCommands = CreateBenchCommands();
        _benchCommands.Enabled = false;
        header.Controls.Add(_benchCommands);

        header.Controls.Add(CreateNcuProfilesRow());

        var inventory = CreateCompactRow();
        _exportLines.Items.AddRange(["A + B", "Solo A", "Solo B"]);
        _exportLines.SelectedIndex = 0;
        inventory.Controls.AddRange([_findAddresses, _lampStatuses]);
        header.Controls.Add(inventory);

        var manual = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        manual.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        manual.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        manual.Controls.Add(_command, 0, 0);
        manual.Controls.Add(_send, 1, 0);
        header.Controls.Add(CreateSection("Telegramma manuale", manual));

        var commandScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        commandScroll.Controls.Add(header);

        var logs = CreateSplitter(topMinimum: 56, bottomMinimum: 56);
        logs.Panel1.Controls.Add(CreateLogSection("Risultati e operazioni", _results));
        logs.Panel2.Controls.Add(CreateLogSection("Telegrammi TX / RX", _telegrams));

        var workspace = CreateSplitter(topMinimum: 72, bottomMinimum: 128);
        workspace.FixedPanel = FixedPanel.Panel1;
        workspace.Panel1.Controls.Add(commandScroll);
        workspace.Panel2.Controls.Add(logs);

        var footer = CreateRow();
        footer.Dock = DockStyle.Bottom;
        footer.Padding = new Padding(12, 4, 12, 4);
        var logFile = new LinkLabel { Text = "Apri log sessione", AutoSize = true, Margin = new Padding(18, 3, 3, 3) };
        logFile.LinkClicked += (_, _) => OpenLog();
        footer.Controls.AddRange([_status, logFile]);
        Controls.Add(workspace);
        Controls.Add(footer);

        Load += (_, _) =>
        {
            Rectangle screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(620, screen.Width), Math.Min(450, screen.Height));
            int width = Math.Min(1180, screen.Width);
            int height = Math.Min(800, screen.Height);
            Bounds = new Rectangle(screen.Left + (screen.Width - width) / 2,
                screen.Top + (screen.Height - height) / 2, width, height);
            PerformLayout();
            SetInitialSplit(workspace, Math.Min(header.Height + 4, workspace.ClientSize.Height * 3 / 5));
            SetInitialSplit(logs, logs.ClientSize.Height / 2);
        };

        _scan.Click += async (_, _) => await ScanAsync();
        _devices.SelectedIndexChanged += (_, _) => SelectDevice();
        _mac.TextChanged += (_, _) =>
        {
            if (_locationMac is null || _mac.Text.Equals(_locationMac, StringComparison.OrdinalIgnoreCase)) return;
            _locationMac = null;
            _building.Clear();
            _exportControllerAddress.Value = 1;
            _exportLines.SelectedIndex = 0;
        };
        _name.TextChanged += (_, _) =>
        {
            if (_name.Text.StartsWith("NCU", StringComparison.OrdinalIgnoreCase) && _name.Text.Length == 11)
                _password.Text = _name.Text[3..];
        };
        _connect.Click += async (_, _) => await ConnectAsync();
        _disconnect.Click += (_, _) => Disconnect();
        _login.Click += (_, _) => Login();
        _autoLogin.CheckedChanged += (_, _) => Log(_autoLogin.Checked ? "Login automatico attivo." : "Login manuale attivo.");
        _send.Click += (_, _) => Send(_command.Text);
        _findAddresses.Click += async (_, _) => await ToggleInventoryAsync(includeLampStatus: false);
        _lampStatuses.Click += async (_, _) => await ToggleInventoryAsync(includeLampStatus: true);
        FormClosing += (_, _) => Disconnect();
        Log("Pronto. Cerca un controller NCU oppure inserisci il MAC Bluetooth.");
        Log($"File di sessione: {_logPath}");
        try
        {
            if (File.Exists(NcuProfiles.LocalPath)) SetProfiles(NcuProfiles.Read(NcuProfiles.LocalPath));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log($"Rubrica NCU non caricata: {ex.Message}");
        }
    }

    private Control CreateNcuProfilesRow()
    {
        var row = CreateCompactRow();
        var load = new Button { Text = "Carica profilo", AutoSize = true };
        var import = new Button { Text = "Importa JSON", AutoSize = true };
        var export = new Button { Text = "Esporta JSON", AutoSize = true };
        AddCompactField(row, "NCU salvate", _savedNcus);
        row.Controls.Add(load);
        AddCompactField(row, "Palazzo", _building);
        AddCompactField(row, "Indirizzo centrale", _exportControllerAddress);
        AddCompactField(row, "Linee", _exportLines);
        row.Controls.AddRange([_saveNcu, import, export]);
        load.Click += (_, _) => LoadNcuProfile();
        _saveNcu.Click += (_, _) => SaveConnectedNcu();
        import.Click += (_, _) => ImportNcuProfiles();
        export.Click += (_, _) => ExportNcuProfiles();
        return row;
    }

    private NcuLines SelectedReportLines => _exportLines.SelectedIndex switch
    {
        1 => NcuLines.A,
        2 => NcuLines.B,
        _ => NcuLines.Both
    };

    private void SetProfiles(List<NcuProfile> profiles, NcuProfile? selection = null)
    {
        _profiles = profiles;
        _savedNcus.Items.Clear();
        _savedNcus.Items.AddRange(profiles
            .OrderBy(profile => profile.Building, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(profile => profile.ControllerAddress)
            .ThenBy(profile => profile.Mac, StringComparer.Ordinal)
            .ToArray());
        _savedNcus.SelectedIndex = selection is null ? (_savedNcus.Items.Count > 0 ? 0 : -1) : _savedNcus.Items.IndexOf(selection);
    }

    private void LoadNcuProfile()
    {
        if (_savedNcus.SelectedItem is not NcuProfile profile) return;
        if (_addressScan is not null || _clearLine is not null || (_client is null && !_connect.Enabled))
        {
            Log("Attendi la fine dell'operazione prima di caricare un profilo NCU.");
            return;
        }
        if (_connectedMac is not null && _connectedMac != profile.Mac)
        {
            Log("Disconnetti la NCU attiva prima di caricare un profilo con un MAC diverso.");
            return;
        }
        if (_connectedMac is null)
        {
            _devices.SelectedIndex = -1;
            _password.Clear();
            _name.Text = profile.Name;
            _mac.Text = profile.Mac;
            if (profile.Name.StartsWith("NCU", StringComparison.OrdinalIgnoreCase) && profile.Name.Length == 11)
                _password.Text = profile.Name[3..];
        }
        ApplyProfileLocation(profile);
        Log($"Profilo NCU caricato: {profile} [{profile.Mac}].");
    }

    private void ApplyProfileLocation(NcuProfile profile)
    {
        _locationMac = profile.Mac;
        _building.Text = profile.Building;
        _exportControllerAddress.Value = profile.ControllerAddress;
        _exportLines.SelectedIndex = profile.Lines switch { NcuLines.A => 1, NcuLines.B => 2, _ => 0 };
    }

    private void SaveConnectedNcu()
    {
        if (_client?.Connected != true || _connectedMac is null) return;
        try
        {
            var profile = NcuProfiles.Validate(new NcuProfile(_name.Text, _connectedMac, _building.Text,
                (int)_exportControllerAddress.Value, SelectedReportLines));
            var updated = File.Exists(NcuProfiles.LocalPath) ? NcuProfiles.Read(NcuProfiles.LocalPath) : [];
            NcuProfiles.Upsert(updated, profile);
            NcuProfiles.Save(NcuProfiles.LocalPath, updated);
            SetProfiles(updated, profile);
            _locationMac = profile.Mac;
            Log($"Profilo NCU salvato: {profile} [{profile.Mac}].");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log($"Profilo NCU non salvato: {ex.Message}");
        }
    }

    private void ImportNcuProfiles()
    {
        using var open = new OpenFileDialog { Title = "Importa rubrica NCU", Filter = "Rubrica NCU (*.json)|*.json" };
        if (open.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var imported = NcuProfiles.Read(open.FileName);
            var updated = File.Exists(NcuProfiles.LocalPath) ? NcuProfiles.Read(NcuProfiles.LocalPath) : [];
            foreach (var profile in imported) NcuProfiles.Upsert(updated, profile);
            NcuProfiles.Save(NcuProfiles.LocalPath, updated);
            SetProfiles(updated);
            Log($"Rubrica NCU importata: {imported.Count} profili sincronizzati.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log($"Importazione rubrica NCU non riuscita: {ex.Message}");
        }
    }

    private void ExportNcuProfiles()
    {
        using var save = new SaveFileDialog
        {
            Title = "Esporta rubrica NCU", Filter = "Rubrica NCU (*.json)|*.json",
            DefaultExt = "json", AddExtension = true, FileName = "centrali-ncu.json"
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            NcuProfiles.Save(save.FileName, _profiles);
            Log($"Rubrica NCU esportata: {save.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Esportazione rubrica NCU non riuscita: {ex.Message}");
        }
    }

    private Control CreateBenchCommands()
    {
        var panel = CreateCompactRow();
        var line = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 48 };
        line.Items.AddRange(["A", "B"]);
        line.SelectedIndex = 0;
        var number = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62 };
        number.Items.AddRange(Enumerable.Range(0, 64)
            .Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        number.SelectedIndex = 0;
        var function = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 144 };
        function.Items.AddRange(["Indirizzamento", "Cancellazione", "Accensione e luce", "Diagnostica e test"]);
        var operation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 252, DropDownWidth = 330 };
        var fromHour = new NumericUpDown { Minimum = 0, Maximum = 23, Width = 50 };
        var toHour = new NumericUpDown { Minimum = 0, Maximum = 23, Width = 50 };
        var send = new Button { Text = "Invia comando", AutoSize = true };

        int Line() => line.SelectedIndex;
        int Number() => number.SelectedIndex;
        string Lamp(int action) => SchneiderProtocol.LampCommand(Line(), Number(), action);
        string Address(int action) => SchneiderProtocol.SequentialAddress(Line(), action, action == 3 ? 0 : Number());
        string Clear(int step) => SchneiderProtocol.ClearLampStep(Line(), Number(), step);

        const string setSafeTime = "Imposta finestra test consentita (302)";
        (string Label, Func<string>? Format, bool UsesNumber)[][] groups =
        [
            [
                ("Avvia sessione (133 / 001)", () => Address(1), true),
                ("Proponi numero per una lampada (133 / 002)", () => Address(2), true),
                ("Termina sessione (133 / 003)", () => Address(3), false),
                ("Leggi numero e stato lampada (201)", () => SchneiderProtocol.ReadLamp(Line(), Number()), true)
            ],
            [
                ("Cancella tutti gli indirizzi della linea", null, false),
                ("Singola 1 · Prepara cancellazione (130)", () => Clear(1), false),
                ("Singola 2 · Cancella numero lampada (130)", () => Clear(2), true),
                ("Singola 3 · Interroga lampada (130)", () => Clear(3), true)
            ],
            [
                ("Accendi lampada al massimo (017)", () => Lamp(5), true),
                ("Spegni lampada (017)", () => Lamp(0), true),
                ("Identifica lampada una volta (017)", () => Lamp(240), true),
                ("Accendi tutta la linea al massimo (017)", () => SchneiderProtocol.LineCommand(Line(), 5), false),
                ("Spegni tutta la linea (017)", () => SchneiderProtocol.LineCommand(Line(), 0), false),
                ("Identifica tutta la linea una volta (017)", () => SchneiderProtocol.LineCommand(Line(), 240), false)
            ],
            [
                ("Avvia test funzionale (017)", () => Lamp(227), true),
                ("Avvia test autonomia (017)", () => Lamp(228), true),
                ("Interrompi test (017)", () => Lamp(229), true),
                ("Rest (017)", () => Lamp(224), true),
                ("Inhibit (017)", () => Lamp(225), true),
                ("Re-Light (017)", () => Lamp(226), true),
                ("Leggi informazioni centralina (208)", () => SchneiderProtocol.Command(208), false),
                ("Leggi orologio centralina (008)", () => SchneiderProtocol.Command(8), false),
                ("Leggi finestra test consentita (205)", () => SchneiderProtocol.Command(205), false),
                (setSafeTime, () => SchneiderProtocol.SafeTime((int)fromHour.Value, (int)toHour.Value), false)
            ]
        ];
        string? loadedCommand = null;
        void InvalidateCommand()
        {
            if (loadedCommand is null) return;
            if (_command.Text == loadedCommand)
            {
                _command.Clear();
            }
            loadedCommand = null;
        }

        void UpdateSelection()
        {
            InvalidateCommand();
            bool usesNumber = operation.SelectedIndex >= 0 && groups[function.SelectedIndex][operation.SelectedIndex].UsesNumber;
            number.Enabled = usesNumber;
            bool clearsLine = operation.SelectedIndex >= 0 && groups[function.SelectedIndex][operation.SelectedIndex].Format is null;
            send.Text = clearsLine ? $"Cancella tutta la linea {line.Text}"
                : usesNumber ? $"Invia a {line.Text}{Number():00}" : "Invia comando";
        }

        line.SelectedIndexChanged += (_, _) => UpdateSelection();
        number.SelectedIndexChanged += (_, _) => UpdateSelection();
        operation.SelectedIndexChanged += (_, _) => UpdateSelection();
        fromHour.ValueChanged += (_, _) => InvalidateCommand();
        toHour.ValueChanged += (_, _) => InvalidateCommand();

        send.Click += async (_, _) =>
        {
            var selected = groups[function.SelectedIndex][operation.SelectedIndex];
            if (selected.Format is null)
            {
                await ClearLineAsync(Line());
                return;
            }
            loadedCommand = selected.Format();
            string target = selected.UsesNumber ? $"lampada {line.Text}{Number():00}" : $"selezione linea {line.Text}";
            SendSelected(loadedCommand, $"{selected.Label} · {target}");
        };
        AddCompactField(panel, "Banco prova", line);
        AddCompactField(panel, "Numero", number);
        AddCompactField(panel, "Funzione", function);
        AddCompactField(panel, "Operazione", operation);
        var hours = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Visible = false, Margin = Padding.Empty
        };
        AddCompactField(hours, "Inizio", fromHour);
        AddCompactField(hours, "Fine", toHour);
        operation.SelectedIndexChanged += (_, _) =>
            hours.Visible = operation.SelectedItem is string label && label == setSafeTime;
        function.SelectedIndexChanged += (_, _) =>
        {
            InvalidateCommand();
            operation.Items.Clear();
            operation.Items.AddRange(groups[function.SelectedIndex].Select(option => option.Label).ToArray());
            operation.SelectedIndex = 0;
        };
        function.SelectedIndex = 0;
        panel.Controls.Add(hours);
        panel.Controls.Add(send);
        return panel;
    }

    private void AddLineCommand(string label, int operation)
    {
        var button = new Button { Text = label, AutoSize = true };
        button.Click += (_, _) =>
        {
            if (_lineTarget.SelectedIndex != 1 &&
                !SendSelected(SchneiderProtocol.LineCommand(0, operation), $"{label} · tutta la linea A"))
                return;
            if (_lineTarget.SelectedIndex != 0)
                SendSelected(SchneiderProtocol.LineCommand(1, operation), $"{label} · tutta la linea B");
        };
        _lineCommands.Controls.Add(button);
    }

    private bool SendSelected(string frame, string operation)
    {
        _command.Text = frame;
        Log(operation);
        return Send(frame);
    }

    private static FlowLayoutPanel CreateRow() => new()
    {
        AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 3)
    };

    private static FlowLayoutPanel CreateCompactRow() => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
        Dock = DockStyle.Top, BackColor = Color.White, Padding = new Padding(5, 2, 5, 2),
        Margin = new Padding(0, 0, 0, 4)
    };

    private static void AddCompactField(FlowLayoutPanel row, string caption, Control control)
    {
        var field = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Margin = new Padding(2, 0, 4, 0)
        };
        field.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left });
        field.Controls.Add(control);
        row.Controls.Add(field);
    }

    private static GroupBox CreateSection(string title, params Control[] rows)
    {
        var group = new GroupBox
        {
            Text = title, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 5, 8, 7),
            Margin = new Padding(0, 0, 0, 4), BackColor = Color.White
        };
        var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var row in rows) stack.Controls.Add(row);
        group.Controls.Add(stack);
        return group;
    }

    private static SplitContainer CreateSplitter(int topMinimum, int bottomMinimum) => new()
    {
        Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
        Size = new Size(800, 360), SplitterWidth = 8,
        Panel1MinSize = topMinimum, Panel2MinSize = bottomMinimum,
        IsSplitterFixed = false, BackColor = Color.FromArgb(211, 220, 229)
    };

    private static void SetInitialSplit(SplitContainer split, int preferred)
    {
        int max = split.ClientSize.Height - split.SplitterWidth - split.Panel2MinSize;
        if (max >= split.Panel1MinSize)
            split.SplitterDistance = Math.Clamp(preferred, split.Panel1MinSize, max);
    }

    private static GroupBox CreateLogSection(string title, TextBox view)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = Color.White };
        group.Controls.Add(view);
        return group;
    }

    private static TextBox CreateLogView(bool wrap) => new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = wrap ? ScrollBars.Vertical : ScrollBars.Both,
        WordWrap = wrap, Dock = DockStyle.Fill, Font = new Font("Consolas", 10),
        BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle
    };

    private void Log(string text, bool telegram = false)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => Log(text, telegram))); return; }

        var now = DateTime.Now;
        (telegram ? _telegrams : _results).AppendText($"[{now:HH:mm:ss.fff}] {text}\r\n");
        if (_logWriteFailed) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath, $"[{now:yyyy-MM-dd HH:mm:ss.fff zzz}] {text}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logWriteFailed = true;
            _results.AppendText($"Salvataggio del log non riuscito: {ex.Message}\r\n");
        }
    }

    private void OpenLog()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_logPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log($"Impossibile aprire il log: {ex.Message}");
        }
    }

    private void Login()
    {
        if (string.IsNullOrWhiteSpace(_password.Text))
        {
            Log("Inserisci la password della centralina per il login 323.");
            return;
        }

        try
        {
            Send(SchneiderProtocol.Login(_password.Text));
        }
        catch (ArgumentException ex)
        {
            Log($"Login non inviato: {ex.Message}");
        }
    }

    private async Task ScanAsync()
    {
        _scan.Enabled = false;
        _status.Text = "Ricerca Bluetooth Classic, anche non associati…";
        try
        {
            var list = await Task.Run(BluetoothDiscovery.Scan);
            if (IsDisposed || Disposing) return;
            _devices.Items.Clear();
            _devices.Items.AddRange(list.ToArray());
            _devices.SelectedIndex = _connect.Enabled
                ? list.FindIndex(device => device.IsNcu)
                : list.FindIndex(device => device.Mac == _connectedMac);
            foreach (var device in list)
                Log($"Bluetooth rilevato: {device} / connesso={device.Connected}");
            Log($"Trovati {list.Count} dispositivi Bluetooth Classic, di cui {list.Count(device => !device.Paired)} non associati.");
            if (_devices.SelectedIndex < 0 && list.Count > 0)
                Log("Nessun nome NCU riconosciuto. Seleziona il dispositivo corrispondente al MAC della centralina.");
            _status.Text = "Ricerca completata";
        }
        catch (Exception ex) { Log("Ricerca fallita: " + ex.Message); _status.Text = "Errore ricerca"; }
        finally { _scan.Enabled = true; }
    }

    private void SelectDevice()
    {
        if (!_connect.Enabled || _devices.SelectedItem is not BluetoothDiscovery.Device item) return;
        if (!item.IsNcu) _password.Clear();
        _name.Text = item.Name;
        _mac.Text = item.Mac;
        Log($"Selezionato {item}");
    }

    private async Task ConnectAsync()
    {
        _connect.Enabled = _devices.Enabled = false;
        _mac.ReadOnly = true;
        _status.Text = "Connessione RFCOMM 1...";
        Log("Apertura socket AF_BTH/RFCOMM canale 1. Il pairing Windows potrebbe comunque essere richiesto.");
        BluetoothRfcommClient? candidate = new();
        try
        {
            string mac = _mac.Text.Trim();
            ulong address = BluetoothRfcommClient.ParseAddress(mac);
            if (address == 0) throw new FormatException("MAC nullo: seleziona il controller con Cerca NCU.");
            Log($"Destinazione: NCU={_name.Text.Trim()}, MAC={BluetoothRfcommClient.FormatAddress(address)}, RFCOMM=1");
            if (_devices.SelectedItem is BluetoothDiscovery.Device chosen &&
                !string.Equals(chosen.Mac.Replace(":", ""), mac.Replace(":", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase))
                Log("ATTENZIONE: il MAC digitato è diverso da quello rilevato per il dispositivo selezionato.");
            await Task.Run(() => candidate.Connect(mac, 1, message => Log("BT: " + message)));
            if (IsDisposed || Disposing) return;
            _client = candidate;
            candidate = null;
            _connectedMac = BluetoothRfcommClient.FormatAddress(address);
            _locationMac = _connectedMac;
            _saveNcu.Enabled = true;
            var saved = _profiles.Where(profile => profile.Mac == _connectedMac).Take(2).ToList();
            if (saved.Count == 1 && string.IsNullOrWhiteSpace(_building.Text))
            {
                _savedNcus.SelectedItem = saved[0];
                ApplyProfileLocation(saved[0]);
            }
            _disconnect.Enabled = _login.Enabled = _send.Enabled = true;
            _status.Text = "Bluetooth connesso — login non ancora verificato";
            Log("Socket RFCOMM aperto.");
            _reading = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_client, _reading.Token);
            if (_autoLogin.Checked) Login();
        }
        catch (Exception ex)
        {
            Log($"ERRORE connessione: {ex.Message} (Win32 {((ex as System.ComponentModel.Win32Exception)?.NativeErrorCode.ToString() ?? "n/d")})");
            if (ex is System.ComponentModel.Win32Exception { NativeErrorCode: 10049 })
                Log("DIAGNOSI: controllare i due tentativi (canale diretto e SDP). Il pairing Windows e la pubblicazione SDP del servizio sono verifiche successive.");
            else if (ex is System.ComponentModel.Win32Exception { NativeErrorCode: 10013 })
                Log("DIAGNOSI 10013: autenticazione/associazione Bluetooth rifiutata da Windows.");
            else
                Log("La connessione RFCOMM può richiedere pairing Bluetooth; questo errore va distinto dall'autenticazione Schneider 323.");
            _status.Text = "Connessione fallita";
            _connect.Enabled = _devices.Enabled = true;
            _mac.ReadOnly = false;
        }
        finally { candidate?.Dispose(); }
    }

    private async Task ClearLineAsync(int line)
    {
        var client = _client;
        if (client?.Connected != true || _clearLine is not null || _addressScan is not null) return;

        char lineName = (char)('A' + line);
        if (MessageBox.Show(this, $"Cancellare tutti gli indirizzi della linea {lineName}?",
            "Cancellazione indirizzi", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        if (!ReferenceEquals(_client, client) || !client.Connected) return;

        using var clear = new CancellationTokenSource();
        _clearLine = clear;
        _benchCommands.Enabled = _lineCommands.Enabled = _login.Enabled = _send.Enabled =
            _findAddresses.Enabled = _lampStatuses.Enabled = false;
        try
        {
            for (int step = 1; step <= 3; step++)
            {
                clear.Token.ThrowIfCancellationRequested();
                string frame = SchneiderProtocol.ClearLineStep(line, step);
                string command = frame.Substring(4, 3);
                var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _expectedClearCommand = command;
                _clearReply = reply;
                _status.Text = $"Cancellazione linea {lineName} · passaggio {step}/3";
                if (!SendSelected(frame, $"Clear linea {lineName} · passaggio {step}/3"))
                    throw new IOException($"Invio del comando {command} fallito.");

                string result;
                try
                {
                    result = await reply.Task.WaitAsync(TimeSpan.FromSeconds(5), clear.Token);
                }
                catch (TimeoutException)
                {
                    throw new IOException($"Nessun esito per il comando {command}, passaggio {step}.");
                }
                if (result != "00")
                    throw new IOException($"Comando {command}, passaggio {step}: esito {result}.");
            }
            Log($"Clear linea {lineName}: tutti e tre i passaggi accettati.");
        }
        catch (OperationCanceledException)
        {
            Log($"Clear linea {lineName} interrotto.");
        }
        catch (IOException ex)
        {
            Log($"Clear linea {lineName} non completato: {ex.Message}");
        }
        finally
        {
            _clearReply = null;
            _expectedClearCommand = null;
            _clearLine = null;
            if (!IsDisposed && !Disposing && ReferenceEquals(_client, client) && client.Connected)
            {
                _benchCommands.Enabled = _lineCommands.Enabled = _login.Enabled = _send.Enabled =
                    _findAddresses.Enabled = _lampStatuses.Enabled = true;
                _status.Text = "Autenticazione 323: ACK ricevuto";
            }
        }
    }

    private async Task ToggleInventoryAsync(bool includeLampStatus)
    {
        if (_addressScan is { } activeScan) activeScan.Cancel();
        else await ExportInventoryAsync(includeLampStatus);
    }

    private async Task ExportInventoryAsync(bool includeLampStatus)
    {
        var client = _client;
        if (client is null || !client.Connected) return;

        int controllerAddress = (int)_exportControllerAddress.Value;
        string building = _building.Text;
        var reportLines = SelectedReportLines;
        int firstLine = _exportLines.SelectedIndex == 2 ? 1 : 0;
        int lastLine = _exportLines.SelectedIndex == 1 ? 0 : 1;
        if (AddressWorkbook.BusNumber(controllerAddress, lastLine) > 99)
        {
            Log("L'indirizzo 50 consente solo la linea A (BUS L99). La linea B produrrebbe BUS 100.");
            return;
        }
        string lines = _exportLines.Text;
        int firstIndex = firstLine * 64;
        int endIndex = (lastLine + 1) * 64;
        int count = endIndex - firstIndex;

        using var scan = new CancellationTokenSource();
        _addressScan = scan;
        var activeButton = includeLampStatus ? _lampStatuses : _findAddresses;
        string originalLabel = activeButton.Text;
        activeButton.Text = "Annulla ricerca";
        (includeLampStatus ? _findAddresses : _lampStatuses).Enabled = false;
        _building.Enabled = _exportControllerAddress.Enabled = _exportLines.Enabled = _saveNcu.Enabled = false;
        string reportTitle = includeLampStatus ? "Stato lampade" : "Indirizzi";
        _lineCommands.Enabled = _benchCommands.Enabled = _send.Enabled = _login.Enabled = _scan.Enabled = false;
        var addresses = new List<LampAddressStatus>();
        Log($"{reportTitle}: {lines} sulla connessione attiva · indirizzo centralina per Excel {controllerAddress:00}.");
        try
        {
            for (int index = firstIndex; index < endIndex; index++)
            {
                scan.Token.ThrowIfCancellationRequested();
                var target = new LampAddressStatus(index, false, false);
                _status.Text = $"Ricerca {target.Address} ({index - firstIndex + 1}/{count})";
                _requestedAddress = index;
                var reply = new TaskCompletionSource<LampAddressStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
                _addressReply = reply;
                if (!Send(SchneiderProtocol.ReadLamp(index / 64, index % 64)))
                    throw new IOException($"Richiesta {target.Address} non inviata.");

                LampAddressStatus status;
                try
                {
                    status = await reply.Task.WaitAsync(TimeSpan.FromSeconds(5), scan.Token);
                }
                catch (TimeoutException)
                {
                    throw new TimeoutException($"Nessuna risposta valida per {target.Address}.");
                }
                if (status.Assigned) addresses.Add(status);
            }

            scan.Token.ThrowIfCancellationRequested();
            Log($"Ricerca completata: {addresses.Count} indirizzi registrati su {count} verificati.");
            using var save = new SaveFileDialog
            {
                Title = $"Esporta in Excel · {reportTitle}", Filter = "Cartella di lavoro Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx", AddExtension = true, InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                FileName = NcuProfiles.ExcelName(reportTitle, building, controllerAddress, reportLines, DateTime.Now)
            };
            if (save.ShowDialog(this) == DialogResult.OK)
            {
                AddressWorkbook.Save(save.FileName, addresses, controllerAddress, includeLampStatus);
                Log($"Excel salvato: {save.FileName}");
            }
            else Log("Salvataggio Excel annullato.");
        }
        catch (OperationCanceledException)
        {
            Log("Ricerca indirizzi annullata.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            Log($"Ricerca o salvataggio non completati: {ex.Message}");
        }
        finally
        {
            _addressReply = null;
            _addressScan = null;
            if (!IsDisposed && !Disposing)
            {
                activeButton.Text = originalLabel;
                _building.Enabled = _exportControllerAddress.Enabled = _exportLines.Enabled = true;
                _scan.Enabled = true;
                if (ReferenceEquals(_client, client) && client.Connected)
                {
                    _lineCommands.Enabled = _benchCommands.Enabled = _send.Enabled = _login.Enabled = _findAddresses.Enabled = _lampStatuses.Enabled = true;
                    _saveNcu.Enabled = true;
                    _status.Text = "Autenticazione 323: ACK ricevuto";
                }
            }
        }
    }

    private async Task ReceiveLoopAsync(BluetoothRfcommClient client, CancellationToken token)
    {
        var pending = new StringBuilder();
        int expectedLength = 0;
        var buffer = new byte[1024];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int count = await Task.Run(() => client.Receive(buffer));
                if (token.IsCancellationRequested) break;
                if (count == -1) continue;
                if (count == 0) { Log("Il controller ha chiuso la connessione."); break; }
                for (int i = 0; i < count; i++)
                {
                    char c = (char)buffer[i];
                    pending.Append(c);
                    if (pending.Length == 12)
                        expectedLength = SchneiderProtocol.BinaryReplyLength(pending.ToString());
                    if (expectedLength > 0 && pending.Length < expectedLength) continue;
                    if (c == '*' || expectedLength > 0)
                    {
                        string frame = pending.ToString();
                        pending.Clear();
                        expectedLength = 0;
                        if (c != '*')
                        {
                            Log("Ricezione: terminatore mancante alla lunghezza dichiarata");
                            continue;
                        }
                        Log("RX  " + SchneiderProtocol.FormatReplyForLog(frame), telegram: true);
                        Log(SchneiderProtocol.DescribeReply(frame));
                        if (_addressReply is { } reply && SchneiderProtocol.TryReadLampStatus(frame, out var lamp) &&
                            lamp.Index == _requestedAddress)
                            reply.TrySetResult(lamp);
                        if (_clearReply is { } clearReply && _expectedClearCommand is { } expected &&
                            SchneiderProtocol.TryReadReplyStatus(frame, expected, out string result))
                            clearReply.TrySetResult(result);
                        if (frame == "$0013230000*" && ReferenceEquals(_client, client) &&
                            _addressScan is null && _clearLine is null)
                        {
                            _lineCommands.Enabled = _benchCommands.Enabled = _findAddresses.Enabled = _lampStatuses.Enabled = true;
                            _status.Text = "Autenticazione 323: ACK ricevuto";
                        }
                    }
                    if (pending.Length > 8192) { Log("Buffer RX troppo lungo, azzerato"); pending.Clear(); }
                }
            }
        }
        catch (Exception ex) { if (!token.IsCancellationRequested) Log("ERRORE RX: " + ex.Message); }
        finally
        {
            if (!token.IsCancellationRequested && !IsDisposed)
                BeginInvoke(new Action(() => { if (ReferenceEquals(_client, client)) Disconnect(); }));
        }
    }

    private bool Send(string frame)
    {
        if (_client is null || !_client.Connected) { Log("Nessuna connessione aperta"); return false; }
        if (!frame.StartsWith('$') || !frame.EndsWith('*') || frame.Any(c => c > 127))
        {
            Log("Telegramma non valido: richiesti ASCII e delimitatori $ ... *"); return false;
        }
        try
        {
            _client.SendAscii(frame);
            bool isLogin = frame.StartsWith("$001323", StringComparison.Ordinal);
            Log("TX  " + (isLogin ? "[LOGIN 323 — password oscurata]" : SchneiderProtocol.FormatReplyForLog(frame)), telegram: true);
            if (isLogin)
            {
                _lineCommands.Enabled = _benchCommands.Enabled = _findAddresses.Enabled = _lampStatuses.Enabled = false;
                _status.Text = "Login inviato — attesa ACK";
                Log("Login 323 inviato.");
            }
        }
        catch (Exception ex)
        {
            Log("ERRORE TX: " + ex.Message);
            return false;
        }
        return true;
    }

    private void Disconnect()
    {
        _addressScan?.Cancel();
        _clearLine?.Cancel();
        _reading?.Cancel(); _reading?.Dispose(); _reading = null;
        _client?.Dispose(); _client = null;
        _connectedMac = null;
        _mac.ReadOnly = false;
        _devices.Enabled = true;
        _saveNcu.Enabled = false;
        _lineCommands.Enabled = _benchCommands.Enabled = _findAddresses.Enabled = _lampStatuses.Enabled = false;
        _connect.Enabled = true;
        _disconnect.Enabled = _login.Enabled = _send.Enabled = false;
        _status.Text = "Disconnesso";
        Log("Socket chiuso.");
    }
}
