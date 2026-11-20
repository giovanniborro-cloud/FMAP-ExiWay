namespace SchneiderLoader;

internal sealed partial class MainForm
{
    private readonly bool _installer;
    private string? _connectedBluetoothName;
    private bool _installerAuthenticated;
    private bool _installerConnecting;
    private BluetoothDiscovery.Device? _confirmedTestNcu;
    private readonly Button _confirmTestNcu = new() { Text = "Conferma NCU", AutoSize = true };
    private CancellationTokenSource? _installerWork;
    private readonly ComboBox _installerLine = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 54 };
    private readonly ComboBox _installerFirst = CreateInstallerNumber(0);
    private readonly ComboBox _installerSecond = CreateInstallerNumber(1);
    private readonly CheckBox _installerUseSecond = new() { Text = "Secondo numero", AutoSize = true };
    private readonly Button _installerReset = new() { Text = "Azzera centrale", AutoSize = true };
    private readonly Button _installerDelete = new() { Text = "Cancella un numero", AutoSize = true };
    private readonly Button _installerAssign = new() { Text = "Assegna numeri", AutoSize = true };
    private readonly Button _installerConfirm = new() { Text = "Conferma laser", AutoSize = true, Visible = false };
    private readonly Button _installerCancel = new() { Text = "Annulla numerazione", AutoSize = true, Visible = false };
    private TaskCompletionSource<bool>? _laserConfirmation;
    private bool _installerAssigning;
    private int _sessionLine;
    private bool _sessionOpen;

    private static ComboBox CreateInstallerNumber(int selected)
    {
        var numbers = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62 };
        numbers.Items.AddRange(Enumerable.Range(0, 64).Select(number => number.ToString()).ToArray());
        numbers.SelectedIndex = selected;
        return numbers;
    }

    private Control CreateInstallerControls()
    {
        _connect.Text = "Connetti";
        _disconnect.Text = "Disconnessione";
        _name.Clear();
        _password.Clear();
        _name.ReadOnly = _password.ReadOnly = true;
        _installerLine.Items.AddRange(["A", "B"]);
        _installerLine.SelectedIndex = 0;

        var connection = CreateCompactRow();
        AddCompactField(connection, "Connessione Bluetooth", _devices);
        connection.Controls.AddRange([_scan, _confirmTestNcu]);
        var login = CreateCompactRow();
        AddCompactField(login, "UID", _name);
        AddCompactField(login, "Password app", _password);
        login.Controls.AddRange([_connect, _disconnect, _installerReset]);
        _confirmTestNcu.Click += (_, _) =>
        {
            if (_devices.SelectedItem is not BluetoothDiscovery.Device device) return;
            SetConnectionDevice(device);
            _confirmedTestNcu = device;
            Log("NCU Test confermata. Premi Connetti per aprire la connessione e fare il login.");
            UpdateInstallerControls();
        };
        var addressing = CreateCompactRow();
        AddCompactField(addressing, "Linea", _installerLine);
        AddCompactField(addressing, "Numero", _installerFirst);
        addressing.Controls.Add(_installerUseSecond);
        AddCompactField(addressing, "Numero 2", _installerSecond);
        addressing.Controls.Add(_installerDelete);
        var steps = CreateCompactRow();
        steps.Controls.AddRange([_installerAssign, _installerConfirm, _installerCancel]);

        _installerUseSecond.CheckedChanged += (_, _) => UpdateInstallerControls();
        _installerReset.Click += async (_, _) => await RunInstallerAsync(_ => ClearAddressesAsync(0, null, bothLines: true));
        _installerDelete.Click += async (_, _) => await RunInstallerAsync(_ =>
            ClearAddressesAsync(_installerLine.SelectedIndex, _installerFirst.SelectedIndex));
        _installerAssign.Click += async (_, _) => await RunInstallerAsync(AssignInstallerNumbersAsync);
        _installerConfirm.Click += (_, _) => _laserConfirmation?.TrySetResult(true);
        _installerCancel.Click += (_, _) =>
        {
            _installerWork?.Cancel();
            UpdateInstallerControls();
        };
        UpdateInstallerControls();
        return CreateSection("Tecnico Installatore", connection, login, addressing, steps);
    }

    private void UpdateInstallerControls()
    {
        bool idle = _installerWork is null;
        bool ready = idle && _installerAuthenticated;
        bool canSelectNcu = idle && _client is null;
        _scan.Enabled = canSelectNcu;
        _devices.Enabled = canSelectNcu && _devices.Items.Count > 0;
        _confirmTestNcu.Enabled = canSelectNcu && _devices.SelectedItem is BluetoothDiscovery.Device;
        _connect.Enabled = canSelectNcu && _confirmedTestNcu is not null;
        _disconnect.Enabled = _installerConnecting
            ? _installerWork?.IsCancellationRequested == false
            : idle && _client is not null;
        _disconnect.Text = _installerConnecting ? "Annulla connessione" : "Disconnessione";
        _installerReset.Enabled = _installerDelete.Enabled = _installerAssign.Enabled = ready && !_sessionOpen;
        _installerLine.Enabled = _installerFirst.Enabled = _installerUseSecond.Enabled = ready && !_sessionOpen;
        _installerSecond.Enabled = ready && !_sessionOpen && _installerUseSecond.Checked;
        _installerConfirm.Visible = _laserConfirmation is not null;
        _installerConfirm.Enabled = _laserConfirmation is { Task.IsCompleted: false } &&
            _installerWork?.IsCancellationRequested == false;
        _installerCancel.Visible = _installerAssigning;
        _installerCancel.Enabled = _installerAssigning && _installerWork?.IsCancellationRequested == false;
    }

    private NcuProfile ReadTestProfile()
    {
        _profiles = File.Exists(NcuProfiles.LocalPath) ? NcuProfiles.Read(NcuProfiles.LocalPath) : [];
        var profiles = _profiles.Where(profile => profile.Building.Equals("TEST", StringComparison.OrdinalIgnoreCase))
            .OrderBy(profile => profile.ControllerAddress).ToList();
        int macCount = profiles.Select(profile => profile.Mac).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (macCount != 1)
            throw new IOException(macCount == 0 ? "Nessun profilo NCU salvato con Palazzo = TEST."
                : "Sono presenti profili con Palazzo = TEST e MAC diversi. Scegli un unico MAC nella rubrica in modalità Esperto.");
        return profiles[0];
    }

    private Task SearchInstallerNcuAsync() => RunInstallerAsync(async token =>
    {
        if (_client is not null) return;
        _confirmedTestNcu = null;
        _devices.Items.Clear();
        _name.Clear();
        _mac.Clear();
        _password.Clear();
        var profile = ReadTestProfile();
        _status.Text = "Cerca NCU…";
        Log($"Cerca NCU · MAC del profilo con Palazzo TEST: {profile.Mac}.");
        var devices = await ScanControllersAsync(token, profile.Mac);
        token.ThrowIfCancellationRequested();
        if (devices.Count == 0)
            throw new IOException($"Cerca NCU non ha rilevato il MAC di Test ({profile.Mac}).");

        _devices.Items.Add(devices[0] with { SavedProfile = profile });
        _devices.SelectedIndex = 0;
        _status.Text = "NCU Test rilevata · conferma la selezione";
    }, connecting: true);

    private Task ConnectInstallerAsync() => RunInstallerAsync(async token =>
    {
        if (_client is not null || _confirmedTestNcu is not { } device) return;
        var profile = ReadTestProfile();
        if (!string.Equals(device.Mac, profile.Mac, StringComparison.OrdinalIgnoreCase))
        {
            _confirmedTestNcu = null;
            _devices.Items.Clear();
            _name.Clear();
            _mac.Clear();
            _password.Clear();
            throw new IOException("Il MAC di Test è cambiato. Esegui di nuovo Cerca NCU.");
        }
        if (string.IsNullOrWhiteSpace(_password.Text))
            throw new IOException("Password non disponibile. Verifica il nome Bluetooth NCU del profilo Test.");
        try
        {
            Log("Connetti RFCOMM 1.");
            await ConnectAsync(token);
            token.ThrowIfCancellationRequested();
            if (_client?.Connected != true) return;

            Log("Login 323.");
            await WaitForReplyAsync("323", Login, token);
            token.ThrowIfCancellationRequested();
            _installerAuthenticated = true;
            _status.Text = "Test connessa · login confermato";
            Log("Test connessa. Login 323 confermato dalla centralina.");
        }
        finally
        {
            if (!_installerAuthenticated && _client is not null) Disconnect();
        }
    }, connecting: true);

    private Task DisconnectInstallerAsync()
    {
        if (_installerConnecting && _installerWork is { } work)
        {
            work.Cancel();
            _status.Text = "Annullamento connessione in corso…";
            UpdateInstallerControls();
            return Task.CompletedTask;
        }
        return RunInstallerAsync(CloseInstallerConnectionAsync);
    }

    private async Task CloseInstallerConnectionAsync(CancellationToken token)
    {
        try
        {
            if (_sessionOpen && _client?.Connected == true)
                await RequestReplyAsync(SchneiderProtocol.SequentialAddress(_sessionLine, 3, 0),
                    "Chiudi sessione prima della disconnessione", token);
        }
        finally
        {
            Disconnect();
        }
    }

    private async Task RunInstallerAsync(Func<CancellationToken, Task> operation, bool connecting = false)
    {
        if (_installerWork is not null) return;
        using var work = new CancellationTokenSource();
        _installerWork = work;
        _installerConnecting = connecting;
        UpdateInstallerControls();
        try { await operation(work.Token); }
        catch (OperationCanceledException)
        {
            Log("Operazione interrotta.");
            if (_client is null && !IsDisposed && !Disposing) _status.Text = "Disconnesso";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or
            System.ComponentModel.Win32Exception or ArgumentException)
        {
            Log($"Operazione non completata: {ex.Message}");
            if (_client is null) _status.Text = "Disconnesso";
        }
        finally
        {
            _installerWork = null;
            _installerConnecting = false;
            if (!IsDisposed && !Disposing) UpdateInstallerControls();
        }
    }

    private async Task AssignInstallerNumbersAsync(CancellationToken token)
    {
        int line = _installerLine.SelectedIndex;
        int first = _installerFirst.SelectedIndex;
        int[] numbers = _installerUseSecond.Checked ? [first, _installerSecond.SelectedIndex] : [first];
        if (numbers.Length == 2 && numbers[0] == numbers[1])
            throw new IOException("Scegli due numeri distinti.");

        var client = _client;
        _installerAssigning = true;
        UpdateInstallerControls();
        try
        {
            foreach (int number in numbers)
            {
                if ((await ReadInstallerAddressAsync(line, number, token)).Assigned)
                    throw new IOException($"{InstallerAddress(line, number)} risulta già registrato.");
            }

            _sessionLine = line;
            _sessionOpen = true;
            try
            {
                await RequestReplyAsync(SchneiderProtocol.SequentialAddress(line, 1, first),
                    $"Avvia sessione linea {(char)('A' + line)}", token);
                foreach (int number in numbers)
                {
                    string target = InstallerAddress(line, number);
                    await RequestReplyAsync(SchneiderProtocol.SequentialAddress(line, 2, number), $"Proponi {target}", token);
                    string reply = await RequestReplyAsync(SchneiderProtocol.Command(134), $"Verifica numero proposto {target}", token);
                    if (!SchneiderProtocol.TryReadNextAddress(reply, out int proposed) || proposed != number)
                        throw new IOException($"La centralina non ha confermato il numero {target}.");

                    _status.Text = $"Conferma {target} sulla lampada con il laser";
                    Log($"{_status.Text}, poi premi Conferma laser.");
                    var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _laserConfirmation = confirmation;
                    _installerConfirm.Text = $"Laser confermato per {target}";
                    UpdateInstallerControls();
                    try { await confirmation.Task.WaitAsync(token); }
                    finally
                    {
                        _laserConfirmation = null;
                        UpdateInstallerControls();
                    }
                    token.ThrowIfCancellationRequested();
                    Log($"Conferma laser ricevuta per {target}.");
                }
            }
            finally
            {
                if (_sessionOpen && client is { Connected: true } && ReferenceEquals(_client, client))
                {
                    await RequestReplyAsync(SchneiderProtocol.SequentialAddress(line, 3, 0),
                        "Chiudi automaticamente la sessione", CancellationToken.None);
                    _sessionOpen = false;
                }
            }

            int updated = 0;
            foreach (int number in numbers)
            {
                if ((await ReadInstallerAddressAsync(line, number, token)).Assigned) updated++;
            }
            _status.Text = $"Verifica conclusa · {updated}/{numbers.Length} registrati";
            Log($"Verifica finale: {updated} di {numbers.Length} numeri risultano registrati.");
        }
        finally
        {
            _installerAssigning = false;
            _laserConfirmation = null;
        }
    }

    private async Task<LampAddressStatus> ReadInstallerAddressAsync(int line, int number, CancellationToken token)
    {
        string target = InstallerAddress(line, number);
        string reply = await RequestReplyAsync(SchneiderProtocol.ReadLamp(line, number), $"Leggi {target}", token);
        if (!SchneiderProtocol.TryReadLampStatus(reply, out var result) || result.Index != line * 64 + number)
            throw new IOException($"Risposta di lettura non valida per {target}.");
        return result;
    }

    private static string InstallerAddress(int line, int number) => $"{(char)('A' + line)}{number:00}";

    private void ResetInstallerAddressing()
    {
        _sessionOpen = false;
    }

    private Task<string> RequestReplyAsync(string frame, string operation, CancellationToken token) =>
        WaitForReplyAsync(frame.Substring(4, 3), () =>
        {
            Log(operation);
            _command.Text = frame;
            return Send(frame);
        }, token);

    private async Task<string> WaitForReplyAsync(string command, Func<bool> transmit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _expectedCommand = command;
        _commandReply = reply;
        try
        {
            if (!transmit())
                throw new IOException($"Invio del comando {command} fallito.");
            string response;
            var timeout = TimeSpan.FromSeconds(_installer && command == "323" ? 15 : 5);
            try { response = await reply.Task.WaitAsync(timeout, token); }
            catch (TimeoutException) { throw new IOException($"Nessuna risposta al comando {command}."); }
            SchneiderProtocol.TryReadReplyStatus(response, command, out string result);
            if (result != "00") throw new IOException($"Comando {command}: esito {result}.");
            return response;
        }
        finally
        {
            _commandReply = null;
            _expectedCommand = null;
        }
    }
}
