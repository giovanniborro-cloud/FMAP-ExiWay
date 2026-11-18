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
        return Command(133, $"{line:000}{operation:000}{nextAddress:000}");
    }
}
