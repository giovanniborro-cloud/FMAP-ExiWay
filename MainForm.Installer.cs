namespace SchneiderLoader;

internal sealed partial class MainForm
{
    private readonly bool _installer;
    private string? _connectedBluetoothName;
    private bool _installerAuthenticated;
    private bool _installerConnecting;
    private CancellationTokenSource? _installerWork;
    private readonly ComboBox _installerLine = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 54 };
    private readonly NumericUpDown _installerFirst = new() { Minimum = 0, Maximum = 63, Width = 56 };
    private readonly NumericUpDown _installerSecond = new() { Minimum = 0, Maximum = 63, Value = 1, Width = 56 };
    private readonly CheckBox _installerUseSecond = new() { Text = "Secondo numero", AutoSize = true };
    private readonly Button _installerReset = new() { Text = "Azzera centrale", AutoSize = true };
    private readonly Button _installerDelete = new() { Text = "Cancella un numero", AutoSize = true };
    private readonly Button _installerStart = new() { Text = "1. Avvia sessione", AutoSize = true };
    private readonly Button _installerPropose = new() { Text = "2. Invia numero", AutoSize = true };
    private readonly Button _installerConfirm = new() { Text = "3. Conferma laser", AutoSize = true };
    private readonly Button _installerFinish = new() { Text = "4. Chiudi e verifica", AutoSize = true };
    private int _sessionLine;
    private int[] _sessionNumbers = [];
    private int _laserConfirmed;
    private bool _waitingForLaser;
    private bool _sessionOpen;
    private bool _sessionStarted;

    private Control CreateInstallerControls()
    {
        _connect.Text = "Connessione";
        _disconnect.Text = "Disconnessione";
        _installerLine.Items.AddRange(["A", "B"]);
        _installerLine.SelectedIndex = 0;

        var connection = CreateCompactRow();
        connection.Controls.AddRange([new Label { Text = "Centralina Test", AutoSize = true, Anchor = AnchorStyles.Left },
            _connect, _disconnect, _installerReset]);
        var addressing = CreateCompactRow();
        AddCompactField(addressing, "Linea", _installerLine);
        AddCompactField(addressing, "Numero", _installerFirst);
        addressing.Controls.Add(_installerUseSecond);
        AddCompactField(addressing, "Numero 2", _installerSecond);
        addressing.Controls.Add(_installerDelete);
        var steps = CreateCompactRow();
        steps.Controls.AddRange([_installerStart, _installerPropose, _installerConfirm, _installerFinish]);

        _installerUseSecond.CheckedChanged += (_, _) => UpdateInstallerControls();
        _installerReset.Click += async (_, _) => await RunInstallerAsync(_ => ClearAddressesAsync(0, null, bothLines: true));
        _installerDelete.Click += async (_, _) => await RunInstallerAsync(_ =>
            ClearAddressesAsync(_installerLine.SelectedIndex, (int)_installerFirst.Value));
        _installerStart.Click += async (_, _) => await RunInstallerAsync(StartInstallerAddressingAsync);
        _installerPropose.Click += async (_, _) => await RunInstallerAsync(ProposeInstallerNumberAsync);
        _installerConfirm.Click += (_, _) =>
        {
            _laserConfirmed++;
            _waitingForLaser = false;
            Log($"Conferma laser dichiarata per {InstallerAddress(_sessionLine, _sessionNumbers[_laserConfirmed - 1])}.");
            UpdateInstallerControls();
        };
        _installerFinish.Click += async (_, _) => await RunInstallerAsync(FinishInstallerAddressingAsync);
        UpdateInstallerControls();
        return CreateSection("Tecnico Installatore", connection, addressing, steps);
    }

    private void UpdateInstallerControls()
    {
        bool idle = _installerWork is null;
        bool ready = idle && _installerAuthenticated;
        _connect.Enabled = idle && _client is null;
        _disconnect.Enabled = _installerConnecting
            ? _installerWork?.IsCancellationRequested == false
            : idle && _client is not null;
        _disconnect.Text = _installerConnecting ? "Annulla connessione" : "Disconnessione";
        _installerReset.Enabled = _installerDelete.Enabled = _installerStart.Enabled = ready && !_sessionOpen;
        _installerLine.Enabled = _installerFirst.Enabled = _installerUseSecond.Enabled = ready && !_sessionOpen;
        _installerSecond.Enabled = ready && !_sessionOpen && _installerUseSecond.Checked;
        _installerPropose.Enabled = ready && _sessionStarted && !_waitingForLaser && _laserConfirmed < _sessionNumbers.Length;
        _installerConfirm.Enabled = ready && _sessionOpen && _waitingForLaser;
        _installerFinish.Enabled = ready && _sessionOpen;
        _installerFinish.Text = _sessionOpen && _laserConfirmed < _sessionNumbers.Length
            ? "4. Chiudi sessione incompleta" : "4. Chiudi e verifica";
    }

    private Task ConnectInstallerAsync() => RunInstallerAsync(async token =>
    {
        if (_client is not null) return;
        var testProfiles = _profiles.Where(profile => profile.Building.Equals("TEST", StringComparison.OrdinalIgnoreCase)).ToList();
        var macs = testProfiles.Select(profile => profile.Mac).Distinct(StringComparer.Ordinal).ToList();
        if (macs.Count != 1)
            throw new IOException(macs.Count == 0 ? "Nessun profilo NCU salvato con Palazzo = TEST."
                : "Sono presenti profili con Palazzo = TEST e MAC diversi. Scegli un unico MAC nella rubrica in modalità Esperto.");

        string mac = macs[0];
        _status.Text = "Ricerca centralina Test…";
        Log($"Ricerca Bluetooth della centralina Test [{mac}].");
        BluetoothDiscovery.Device? detected = null;
        while (detected is null)
        {
            var devices = await Task.Run(BluetoothDiscovery.ScanNearby, token);
            token.ThrowIfCancellationRequested();
            detected = devices.FirstOrDefault(device => device.Mac == mac);
            if (detected is null)
            {
                Log("Test non ancora rilevata. La ricerca continua.");
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        Log($"Test rilevata [{detected.Mac}].");
        string? ncuName = new[] { detected.Name }
            .Concat(testProfiles.Select(profile => profile.BluetoothName))
            .Concat(_profiles.Where(profile => profile.Mac == mac).Select(profile => profile.Name))
            .FirstOrDefault(name => name is { Length: 11 } && name.StartsWith("NCU", StringComparison.OrdinalIgnoreCase));
        if (ncuName is null)
            throw new IOException("Nome Bluetooth NCU di Test non disponibile. Verifica che Test sia rilevabile o salvala di nuovo come Esperto.");

        _mac.Text = mac;
        _name.Text = testProfiles[0].Name;

        try
        {
            await ConnectAsync(token);
            token.ThrowIfCancellationRequested();
            if (_client?.Connected != true) return;
            await RequestReplyAsync(SchneiderProtocol.Login(ncuName[3..]), "Login automatico Test", token);
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
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or ArgumentException)
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

    private async Task StartInstallerAddressingAsync(CancellationToken token)
    {
        int line = _installerLine.SelectedIndex;
        int first = (int)_installerFirst.Value;
        int[] numbers = _installerUseSecond.Checked ? [first, (int)_installerSecond.Value] : [first];
        if (numbers.Length == 2 && numbers[0] == numbers[1])
            throw new IOException("Scegli due numeri distinti.");

        foreach (int number in numbers)
        {
            if ((await ReadInstallerAddressAsync(line, number, token)).Assigned)
                throw new IOException($"{InstallerAddress(line, number)} risulta già registrato.");
        }

        _sessionLine = line;
        _sessionNumbers = numbers;
        _laserConfirmed = 0;
        _waitingForLaser = false;
        _sessionOpen = true;
        await RequestReplyAsync(SchneiderProtocol.SequentialAddress(line, 1, first),
            $"Avvia sessione linea {(char)('A' + line)}", token);
        _sessionStarted = true;
        Log("Sessione aperta. Invia il primo numero.");
    }

    private async Task ProposeInstallerNumberAsync(CancellationToken token)
    {
        int number = _sessionNumbers[_laserConfirmed];
        string target = InstallerAddress(_sessionLine, number);
        await RequestReplyAsync(SchneiderProtocol.SequentialAddress(_sessionLine, 2, number), $"Proponi {target}", token);
        string reply = await RequestReplyAsync(SchneiderProtocol.Command(134), $"Verifica numero proposto {target}", token);
        if (!SchneiderProtocol.TryReadNextAddress(reply, out int proposed) || proposed != number)
            throw new IOException($"La centralina non ha confermato il numero {target}. Chiudi la sessione o riproponi il numero.");
        _waitingForLaser = true;
        Log($"Conferma {target} sulla lampada con il laser, poi premi Conferma laser.");
    }

    private async Task FinishInstallerAddressingAsync(CancellationToken token)
    {
        int line = _sessionLine;
        int[] numbers = _sessionNumbers;
        await RequestReplyAsync(SchneiderProtocol.SequentialAddress(line, 3, 0), "Chiudi sessione", token);
        ResetInstallerAddressing();

        int updated = 0;
        foreach (int number in numbers)
        {
            var status = await ReadInstallerAddressAsync(line, number, token);
            if (status.Assigned) updated++;
        }
        Log($"Verifica finale: {updated} di {numbers.Length} numeri risultano registrati.");
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
        _sessionOpen = _sessionStarted = _waitingForLaser = false;
        _sessionNumbers = [];
        _laserConfirmed = 0;
    }

    private async Task<string> RequestReplyAsync(string frame, string operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string command = frame.Substring(4, 3);
        var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _expectedCommand = command;
        _commandReply = reply;
        try
        {
            Log(operation);
            if (command != "323") _command.Text = frame;
            if (!Send(frame))
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
