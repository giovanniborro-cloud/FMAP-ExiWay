using System.Globalization;
using System.Text.Json;

namespace SchneiderLoader;

internal sealed class InstallerAddressHistory
{
    private readonly SortedSet<int> _addresses = [];

    public static InstallerAddressHistory Read(string file)
    {
        var history = new InstallerAddressHistory();
        if (!File.Exists(file)) return history;

        using var stream = File.OpenRead(file);
        int[] addresses = JsonSerializer.Deserialize<int[]>(stream)
            ?? throw new InvalidDataException("Elenco indirizzi non valido.");
        if (addresses.Any(index => index is < 0 or >= 128))
            throw new InvalidDataException("L'elenco contiene un indirizzo fuori dalle linee A e B.");
        history._addresses.UnionWith(addresses);
        return history;
    }

    public bool SetAssigned(int index, bool assigned) =>
        assigned ? _addresses.Add(index) : _addresses.Remove(index);

    public bool ClearLine(int line) => _addresses.RemoveWhere(index => index / 64 == line) > 0;

    public string FormatLine(int line)
    {
        string numbers = string.Join(", ", _addresses
            .Where(index => index / 64 == line)
            .Select(index => (index % 64).ToString("00", CultureInfo.InvariantCulture)));
        return $"{(char)('A' + line)}: {(numbers.Length == 0 ? "—" : numbers)}";
    }

    public void Save(string file)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(file))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = File.Create(temporary))
                JsonSerializer.Serialize(stream, _addresses);
            File.Move(temporary, file, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
