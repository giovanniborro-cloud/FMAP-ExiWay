namespace SchneiderLoader;

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

    public static string LineACommand(int operation) =>
        LineCommand(0, operation);

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

    public static string DeviceStatus(string frame)
    {
        if (frame.Length != 130 || !frame.AsSpan(0, 12).SequenceEqual("$00120100115"))
            return string.Empty;

        int index = frame[12];
        string address = FormattableString.Invariant($"{(char)('A' + index / 64)}{index % 64:00}");
        string assigned = (frame[18] & 0x80) != 0 ? "indirizzo registrato" : "indirizzo libero";
        string communication = frame[19] == 1 ? ", errore di comunicazione" : "";
        return $"{address}: {assigned}{communication}.";
    }

    public static int DeviceReplyLength(string header)
    {
        // Successful 201 replies carry binary device data, which may contain '*'.
        if (header.Length != 12 || header[0] != '$' || !header.AsSpan(4, 5).SequenceEqual("20100"))
            return 0;

        ReadOnlySpan<char> length = header.AsSpan(9, 3);
        if (length[0] is < '0' or > '9' || length[1] is < '0' or > '9' || length[2] is < '0' or > '9')
            return 0;

        return 12 + int.Parse(length, System.Globalization.CultureInfo.InvariantCulture) + 3;
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
