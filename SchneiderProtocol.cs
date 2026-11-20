namespace SchneiderLoader;

internal readonly record struct LampAddressStatus(int Index, bool Assigned, bool CommunicationError,
    byte Faults = 0, byte EmergencyMode = 0, byte EmergencyStatus = 0)
{
    public char Line => (char)('A' + Index / 64);
    public int Number => Index % 64;
    public string Address => FormattableString.Invariant($"{Line}{Number:00}");
}

internal static class SchneiderProtocol
{
    // Proven by the PEAQ PET-101 capture on 2026-11-17:
    // TX $00132301533fab915       00*   RX $0013230000*
    public static string Login(string password)
    {
        if (password.Length > 15 || password.Any(c => c > 0x7F))
            throw new ArgumentException("La password deve essere ASCII, al massimo 15 caratteri");
        return $"$001323015{password.PadRight(15)}00*";
    }

    public static string Command(int id, string payload = "")
    {
        if (id is < 0 or > 999) throw new ArgumentOutOfRangeException(nameof(id));
        if (payload.Length > 999 || payload.Any(c => c > 127)) throw new ArgumentException("Payload ASCII troppo lungo");
        return $"$001{id:000}{payload.Length:000}{payload}00*";
    }

    public static string LineCommand(int line, int operation) =>
        Command(17, FormattableString.Invariant($"{line:00}00000{operation:000}"));

    public static string SafeTime(int startHour, int endHour) =>
        Command(302, FormattableString.Invariant($"{startHour * 60:x4}{endHour * 60:x4}"));

    public static string LampCommand(int line, int address, int operation) =>
        Command(17, FormattableString.Invariant($"{line:00}02{address:000}{operation:000}"));

    public static string ReadLamp(int line, int address) =>
        Command(201, FormattableString.Invariant($"{line * 64 + address:000}"));

    public static string ClearLampStep(int line, int address, int step)
    {
        if (step == 4)
        {
            int recordIndex = line * 64 + address + 1;
            // eCommission clears the controller's lamp record after the three DALI frames.
            return Command(41, FormattableString.Invariant($"{recordIndex:000}320001") + new string(' ', 20));
        }

        int daliAddress = (address << 1) | 1;
        string payload = step switch
        {
            1 => "A3A3FF000",
            2 => FormattableString.Invariant($"A3{daliAddress:X2}80001"),
            3 => FormattableString.Invariant($"A3{daliAddress:X2}91000"),
            _ => throw new ArgumentOutOfRangeException(nameof(step))
        };
        return Command(130, FormattableString.Invariant($"{line}{payload}"));
    }

    public static string ClearLineStep(int line, int step) => step switch
    {
        1 => ClearLampStep(line, 0, 1),
        2 => Command(130, FormattableString.Invariant($"{line}A3FF80001")),
        3 => Command(45, FormattableString.Invariant($"{line + 1}")),
        _ => throw new ArgumentOutOfRangeException(nameof(step))
    };

    private static readonly string[] FaultNames =
    [
        "Guasto circuito",
        "Autonomia batteria insufficiente",
        "Guasto batteria",
        "Guasto lampada di emergenza",
        "Test funzionale in ritardo",
        "Test autonomia in ritardo",
        "Test funzionale fallito",
        "Test autonomia fallito"
    ];

    public static string DescribeLampFaults(byte faults) => faults == 0
        ? "Nessun guasto registrato"
        : string.Join("; ", FaultNames.Where((_, bit) => (faults & (1 << bit)) != 0));

    public static bool TryReadLampStatus(string frame, out LampAddressStatus status)
    {
        status = default;
        if (frame.Length != 130 || !frame.AsSpan(0, 12).SequenceEqual("$00120100115") ||
            !frame.EndsWith("00*", StringComparison.Ordinal))
            return false;

        // eCommission stores mode, status and fault flags in payload bytes 38–40.
        status = new LampAddressStatus(frame[12], (frame[18] & 0x80) != 0, frame[19] == 1,
            (byte)frame[52], (byte)frame[50], (byte)frame[51]);
        return true;
    }

    public static string DeviceStatus(LampAddressStatus status)
    {
        string assigned = status.Assigned ? "indirizzo registrato" : "indirizzo libero";
        string communication = status.CommunicationError ? ", errore di comunicazione" : "";
        return $"{status.Address}: {assigned}{communication}.";
    }

    public static int BinaryReplyLength(string header)
    {
        // These successful replies carry binary data, which may contain '*'.
        if (header.Length != 12 || header[0] != '$' || !header.AsSpan(7, 2).SequenceEqual("00"))
            return 0;
        string code = header.Substring(4, 3);
        if (code is not ("201" or "208")) return 0;

        ReadOnlySpan<char> length = header.AsSpan(9, 3);
        if (length[0] is < '0' or > '9' || length[1] is < '0' or > '9' || length[2] is < '0' or > '9')
            return 0;

        return 12 + int.Parse(length, System.Globalization.CultureInfo.InvariantCulture) + 3;
    }

    public static bool TryReadReplyStatus(string frame, string command, out string status)
    {
        status = "";
        if (frame.Length < 12 || frame[0] != '$' || !frame.EndsWith("00*", StringComparison.Ordinal) ||
            !frame.AsSpan(4, 3).SequenceEqual(command))
            return false;

        status = frame.Substring(7, 2);
        return true;
    }

    public static string DescribeReply(string frame)
    {
        if (frame.Length < 12 || frame[0] != '$' || !frame.EndsWith("00*", StringComparison.Ordinal))
            return "Risposta non riconosciuta.";

        string command = frame.Substring(4, 3);
        string status = frame.Substring(7, 2);
        if (status != "00") return $"Comando {command} · esito {status}.";
        if (frame.Length == 12) return $"Comando {command} · accettato dalla centralina.";
        if (frame.Length < 15 ||
            !int.TryParse(frame.AsSpan(9, 3), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int dataLength) ||
            dataLength != frame.Length - 15)
            return $"Comando {command} · esito 00, formato dati non riconosciuto.";

        if (command == "201" && TryReadLampStatus(frame, out var lamp)) return DeviceStatus(lamp);
        if (command == "208" && dataLength == 77)
        {
            string name = frame.Substring(52, 21).TrimEnd(' ', '\0');
            string serial = frame.Substring(21, 9).TrimEnd(' ', '\0');
            string mac = string.Join(":", frame.Skip(73).Take(6).Select(value => ((int)value).ToString("X2")));
            int revision = frame[83] | (frame[84] << 8);
            return $"Centralina {FormatReplyForLog(name)} · seriale {FormatReplyForLog(serial)} · MAC {mac} · revisione {revision}.";
        }
        return $"Comando {command} · esito 00, {dataLength} byte di dati.";
    }

    public static string FormatReplyForLog(string frame) =>
        System.Text.RegularExpressions.Regex.Replace(frame, @"[^ -~]", match => $@"\x{(int)match.Value[0]:X2}");

    public static string SequentialAddress(int line, int operation, int nextAddress)
    {
        if (line is < 0 or > 999 || operation is < 1 or > 3 || nextAddress is < 0 or > 63)
            throw new ArgumentOutOfRangeException(nameof(nextAddress), "Linea 0..999, operazione 1..3, indirizzo 0..63");
        return Command(133, FormattableString.Invariant($"{line:000}{operation:000}{nextAddress:000}"));
    }
}
