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

    public static string SequentialAddress(int line, int operation, int nextAddress)
    {
        if (line is < 0 or > 999 || operation is < 1 or > 3 || nextAddress is < 0 or > 63)
            throw new ArgumentOutOfRangeException(nameof(nextAddress), "Linea 0..999, operazione 1..3, indirizzo 0..63");
        return Command(133, $"{line:000}{operation:000}{nextAddress:000}");
    }
}
