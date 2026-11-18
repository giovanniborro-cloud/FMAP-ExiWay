using System.Text;

namespace SchneiderLoader;

internal sealed class MainForm : Form
{
    private readonly ComboBox _devices = new() { Width = 285, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _mac = new() { Width = 160, PlaceholderText = "AA:BB:CC:DD:EE:FF" };
    private readonly TextBox _name = new() { Width = 135, Text = "NCU33fab915" };
    private readonly TextBox _password = new() { Width = 125, Text = "33fab915" };
    private readonly Button _scan = new() { Text = "Cerca NCU", AutoSize = true };
    private readonly Button _connect = new() { Text = "Connetti RFCOMM 1", AutoSize = true };
    private readonly Button _disconnect = new() { Text = "Disconnetti", AutoSize = true, Enabled = false };
    private readonly Button _login = new() { Text = "Login 323", AutoSize = true, Enabled = false };
    private readonly Button _read = new() { Text = "Lettura 007", AutoSize = true, Enabled = false };
    private readonly Button _next = new() { Text = "Leggi 134", AutoSize = true, Enabled = false };
    private readonly FlowLayoutPanel _lineCommands = new()
    {
        AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Enabled = false
    };
    private readonly Button _send = new() { Text = "Invia TX", AutoSize = true, Enabled = false };
    private readonly TextBox _command = new() { Width = 530, Text = "$00113400000*" };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };
    private readonly Label _status = new() { AutoSize = true, Text = "Disconnesso" };
    private BluetoothRfcommClient? _client;
    private CancellationTokenSource? _reading;

    public MainForm()
    {
        Text = "OVA-GEN3 / Exitwai2 — Schneider Exiway Bluetooth RFCOMM";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1070, 650);
        MinimumSize = new Size(800, 500);

        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(10) };
        var devices = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
        AddLabeled(devices, "Controller rilevati", _devices);
        devices.Controls.Add(_scan);
        AddLabeled(devices, "MAC Bluetooth", _mac);
        header.Controls.Add(devices);

        var auth = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
        AddLabeled(auth, "Nome NCU", _name);
        AddLabeled(auth, "Password app (8 caratteri)", _password);
        auth.Controls.AddRange([_connect, _disconnect, _login]);
        header.Controls.Add(auth);

        _lineCommands.Controls.Add(new Label { Text = "Tutta la linea A", AutoSize = true, Margin = new Padding(5, 8, 8, 0) });
        AddLineCommand("Accendi al massimo", 5);
        AddLineCommand("Spegni", 0);
        AddLineCommand("Identifica", 240);
        header.Controls.Add(_lineCommands);

        var commands = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
        commands.Controls.AddRange([_read, _next]);
        AddLabeled(commands, "Telegramma ASCII ($...*)", _command);
        commands.Controls.Add(_send);
        header.Controls.Add(commands);
        var caution = new Label { AutoSize = true, Text = "Test: RFCOMM Bluetooth Classic canale 1; nessuna porta COM/TCP. Comandi di scrittura agiscono sul controller reale.", ForeColor = Color.DarkRed };
        header.Controls.Add(caution);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10) };
        footer.Controls.Add(_status);
        Controls.Add(_log);
        Controls.Add(header);
        Controls.Add(footer);

        _scan.Click += async (_, _) => await ScanAsync();
        _devices.SelectedIndexChanged += (_, _) => SelectDevice();
        _name.TextChanged += (_, _) =>
        {
            if (_name.Text.StartsWith("NCU", StringComparison.OrdinalIgnoreCase) && _name.Text.Length == 11)
                _password.Text = _name.Text[3..];
        };
        _connect.Click += async (_, _) => await ConnectAsync();
        _disconnect.Click += (_, _) => Disconnect();
        _login.Click += (_, _) => Send(SchneiderProtocol.Login(_password.Text));
        _read.Click += (_, _) => Send(SchneiderProtocol.Command(7));
        _next.Click += (_, _) => Send(SchneiderProtocol.Command(134));
        _send.Click += (_, _) => Send(_command.Text);
        FormClosing += (_, _) => Disconnect();
        Log("Pronto. Avvia 'Cerca NCU', seleziona il controller e connettiti. In alternativa inserisci il MAC Bluetooth manualmente.");
        Log("La password Schneider viene inviata con comando 323 DOPO la connessione Bluetooth; non e' il PIN di Windows.");
    }

    private void AddLineCommand(string label, int operation)
    {
        var button = new Button { Text = label, AutoSize = true };
        button.Click += (_, _) => Send(SchneiderProtocol.LineACommand(operation));
        _lineCommands.Controls.Add(button);
    }

    private static void AddLabeled(FlowLayoutPanel panel, string caption, Control control)
    {
        var group = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(5, 2, 8, 2) };
        group.Controls.Add(new Label { Text = caption, AutoSize = true });
        group.Controls.Add(control);
        panel.Controls.Add(group);
    }

    private void Log(string text)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => Log(text))); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {text}\r\n");
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
            _client = candidate;
            candidate = null;
            _disconnect.Enabled = _login.Enabled = _read.Enabled = _next.Enabled = _send.Enabled = true;
            _status.Text = "Bluetooth connesso — login non ancora verificato";
            Log("Socket RFCOMM aperto. Avvio ricezione. Ora premi Login 323.");
            _reading = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_client, _reading.Token);
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
                if (count == -1) continue;
                if (count == 0) { Log("Il controller ha chiuso la connessione."); break; }
                for (int i = 0; i < count; i++)
                {
                    char c = (char)buffer[i];
                    pending.Append(c);
                    if (pending.Length == 12)
                        expectedLength = SchneiderProtocol.DeviceReplyLength(pending.ToString());
                    if (expectedLength > 0 && pending.Length < expectedLength) continue;
                    if (c == '*' || expectedLength > 0)
                    {
                        string frame = pending.ToString();
                        pending.Clear();
                        expectedLength = 0;
                        if (c != '*')
                        {
                            Log("RX 201: terminatore mancante alla lunghezza dichiarata");
                            continue;
                        }
                        Log("RX  " + SchneiderProtocol.FormatReplyForLog(frame));
                        if (frame == "$0013230000*")
                        {
                            if (!IsDisposed) BeginInvoke(new Action(() =>
                            {
                                if (!ReferenceEquals(_client, client)) return;
                                _lineCommands.Enabled = true;
                                _status.Text = "Autenticazione 323: ACK ricevuto";
                            }));
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

    private void Send(string frame)
    {
        if (_client is null || !_client.Connected) { Log("Nessuna connessione aperta"); return; }
        if (!frame.StartsWith('$') || !frame.EndsWith('*') || frame.Any(c => c > 127))
        {
            Log("Telegramma non valido: richiesti ASCII e delimitatori $ ... *"); return;
        }
        try
        {
            _client.SendAscii(frame);
            Log("TX  " + (frame.StartsWith("$001323") ? "[LOGIN 323 — password oscurata]" : frame));
            if (frame.StartsWith("$001323"))
            {
                _lineCommands.Enabled = false;
                _status.Text = "Login inviato — attesa ACK";
            }
        }
        catch (Exception ex) { Log("ERRORE TX: " + ex.Message); }
    }

    private void Disconnect()
    {
        _reading?.Cancel(); _reading?.Dispose(); _reading = null;
        _client?.Dispose(); _client = null;
        _lineCommands.Enabled = false;
        _connect.Enabled = true;
        _disconnect.Enabled = _login.Enabled = _read.Enabled = _next.Enabled = _send.Enabled = false;
        _status.Text = "Disconnesso";
        Log("Socket chiuso.");
    }
}
