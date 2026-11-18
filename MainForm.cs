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
    private readonly Button _findAddresses = new() { Text = "Trova indirizzi → Excel", AutoSize = true, Enabled = false };
    private readonly Button _lampStatuses = new() { Text = "Stato lampade → Excel", AutoSize = true, Enabled = false };
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

        var inventory = CreateCompactRow();
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
    }

    private Control CreateBenchCommands()
    {
        var panel = CreateCompactRow();
        var line = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 48 };
        line.Items.AddRange(["A", "B"]);
        line.SelectedIndex = 0;
        var number = new NumericUpDown { Minimum = 0, Maximum = 63, Width = 62 };
        var function = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 144 };
        function.Items.AddRange(["Indirizzamento", "Cancellazione", "Accensione e luce", "Diagnostica e test"]);
        var operation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 252, DropDownWidth = 330 };
        var fromHour = new NumericUpDown { Minimum = 0, Maximum = 23, Width = 50 };
        var toHour = new NumericUpDown { Minimum = 0, Maximum = 23, Width = 50 };
        var send = new Button { Text = "Invia comando", AutoSize = true };

        int Line() => line.SelectedIndex;
        int Number() => (int)number.Value;
        string Lamp(int action) => SchneiderProtocol.LampCommand(Line(), Number(), action);
        string Address(int action) => SchneiderProtocol.SequentialAddress(Line(), action, action == 3 ? 0 : Number());
        string Clear(int step) => SchneiderProtocol.ClearLampStep(Line(), Number(), step);

        const string setSafeTime = "Imposta finestra test consentita (302)";
        (string Label, Func<string> Format)[][] groups =
        [
            [
                ("Avvia sessione (133 / 001)", () => Address(1)),
                ("Proponi numero per una lampada (133 / 002)", () => Address(2)),
                ("Termina sessione (133 / 003)", () => Address(3)),
                ("Leggi numero e stato lampada (201)", () => SchneiderProtocol.ReadLamp(Line(), Number()))
            ],
            [
                ("Linea 1 · Prepara (130)", () => SchneiderProtocol.ClearLineStep(Line(), 1)),
                ("Linea 2 · Cancella tutti gli indirizzi (130)", () => SchneiderProtocol.ClearLineStep(Line(), 2)),
                ("Linea 3 · Aggiorna elenco centralina (045)", () => SchneiderProtocol.ClearLineStep(Line(), 3)),
                ("Singola 1 · Prepara cancellazione (130)", () => Clear(1)),
                ("Singola 2 · Cancella numero lampada (130)", () => Clear(2)),
                ("Singola 3 · Interroga lampada (130)", () => Clear(3)),
                ("Leggi numero e stato lampada (201)", () => SchneiderProtocol.ReadLamp(Line(), Number()))
            ],
            [
                ("Accendi lampada al massimo (017)", () => Lamp(5)),
                ("Spegni lampada (017)", () => Lamp(0)),
                ("Identifica lampada una volta (017)", () => Lamp(240)),
                ("Accendi tutta la linea al massimo (017)", () => SchneiderProtocol.LineCommand(Line(), 5)),
                ("Spegni tutta la linea (017)", () => SchneiderProtocol.LineCommand(Line(), 0)),
                ("Identifica tutta la linea una volta (017)", () => SchneiderProtocol.LineCommand(Line(), 240))
            ],
            [
                ("Avvia test funzionale (017)", () => Lamp(227)),
                ("Avvia test autonomia (017)", () => Lamp(228)),
                ("Interrompi test (017)", () => Lamp(229)),
                ("Rest (017)", () => Lamp(224)),
                ("Inhibit (017)", () => Lamp(225)),
                ("Re-Light (017)", () => Lamp(226)),
                ("Leggi informazioni centralina (208)", () => SchneiderProtocol.Command(208)),
                ("Leggi orologio centralina (008)", () => SchneiderProtocol.Command(8)),
                ("Leggi finestra test consentita (205)", () => SchneiderProtocol.Command(205)),
                (setSafeTime, () => SchneiderProtocol.SafeTime((int)fromHour.Value, (int)toHour.Value))
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

        line.SelectedIndexChanged += (_, _) => InvalidateCommand();
        number.ValueChanged += (_, _) => InvalidateCommand();
        operation.SelectedIndexChanged += (_, _) => InvalidateCommand();
        fromHour.ValueChanged += (_, _) => InvalidateCommand();
        toHour.ValueChanged += (_, _) => InvalidateCommand();

        send.Click += (_, _) =>
        {
            var selected = groups[function.SelectedIndex][operation.SelectedIndex];
            loadedCommand = selected.Format();
            SendSelected(loadedCommand, $"{selected.Label} · selezione {line.Text}{Number():00}");
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
        _status.Text = "Ricerca Bluetooth Classic...";
        try
        {
            var list = await Task.Run(BluetoothDiscovery.Scan);
            _devices.Items.Clear();
            foreach (var item in list) _devices.Items.Add(item);
            if (_devices.Items.Count > 0)
            {
                int firstNcu = list.FindIndex(d => d.Name.StartsWith("NCU", StringComparison.OrdinalIgnoreCase));
                _devices.SelectedIndex = firstNcu >= 0 ? firstNcu : 0;
            }
            foreach (var device in list.Where(d => d.Name.StartsWith("NCU", StringComparison.OrdinalIgnoreCase)))
                Log($"NCU rilevato: {device.Name} / MAC {device.Mac} / paired={device.Paired} / connected={device.Connected}");
            Log($"Trovati {list.Count} dispositivi Bluetooth Classic, {list.Count(x => x.Name.StartsWith("NCU", StringComparison.OrdinalIgnoreCase))} NCU.");
            _status.Text = "Ricerca completata";
        }
        catch (Exception ex) { Log("Ricerca fallita: " + ex.Message); _status.Text = "Errore ricerca"; }
        finally { _scan.Enabled = true; }
    }

    private void SelectDevice()
    {
        if (_devices.SelectedItem is not BluetoothDiscovery.Device item) return;
        _name.Text = item.Name;
        _mac.Text = item.Mac;
        Log($"Selezionato {item.Name} / {item.Mac} / paired={item.Paired}");
    }

    private async Task ConnectAsync()
    {
        _connect.Enabled = false;
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
            _connect.Enabled = true;
        }
        finally { candidate?.Dispose(); }
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

        using var scan = new CancellationTokenSource();
        _addressScan = scan;
        var activeButton = includeLampStatus ? _lampStatuses : _findAddresses;
        string originalLabel = activeButton.Text;
        activeButton.Text = "Annulla ricerca";
        (includeLampStatus ? _findAddresses : _lampStatuses).Enabled = false;
        string reportTitle = includeLampStatus ? "Stato lampade" : "Indirizzi";
        _lineCommands.Enabled = _benchCommands.Enabled = _send.Enabled = _login.Enabled = _scan.Enabled = false;
        var addresses = new List<LampAddressStatus>();
        Log($"{reportTitle}: lettura avviata sulle linee A e B.");
        try
        {
            for (int index = 0; index < 128; index++)
            {
                scan.Token.ThrowIfCancellationRequested();
                var target = new LampAddressStatus(index, false, false);
                _status.Text = $"Ricerca {target.Address} ({index + 1}/128)";
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
            Log($"Ricerca completata: {addresses.Count} indirizzi registrati su 128 verificati.");
            using var save = new SaveFileDialog
            {
                Title = $"Esporta in Excel · {reportTitle}", Filter = "Cartella di lavoro Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx", AddExtension = true, InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                FileName = $"{reportTitle.Replace(' ', '-')}-Exiway-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx"
            };
            if (save.ShowDialog(this) == DialogResult.OK)
            {
                AddressWorkbook.Save(save.FileName, addresses, includeLampStatus);
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
                _scan.Enabled = true;
                if (ReferenceEquals(_client, client) && client.Connected)
                {
                    _lineCommands.Enabled = _benchCommands.Enabled = _send.Enabled = _login.Enabled = _findAddresses.Enabled = _lampStatuses.Enabled = true;
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
                        if (frame == "$0013230000*" && ReferenceEquals(_client, client) && _addressScan is null)
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
        _reading?.Cancel(); _reading?.Dispose(); _reading = null;
        _client?.Dispose(); _client = null;
        _lineCommands.Enabled = _benchCommands.Enabled = _findAddresses.Enabled = _lampStatuses.Enabled = false;
        _connect.Enabled = true;
        _disconnect.Enabled = _login.Enabled = _send.Enabled = false;
        _status.Text = "Disconnesso";
        Log("Socket chiuso.");
    }
}
