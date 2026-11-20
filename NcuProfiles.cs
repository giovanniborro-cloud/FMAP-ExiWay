using System.Text.Json;

namespace SchneiderLoader;

internal enum NcuLines { Both, A, B }

internal sealed record NcuProfile(string Name, string Mac, string Building, int ControllerAddress, NcuLines Lines)
{
    public override string ToString() =>
        $"{Building} · {ControllerAddress:00} · {NcuProfiles.LineLabel(Lines)} · {(Name.Length == 0 ? Mac : Name)}";
}

internal static class NcuProfiles
{
    private const string WindowsInvalidFileNameChars = "<>:\"/\\|?*";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter<NcuLines>(allowIntegerValues: false) }
    };

    public static string LocalPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OVA-GEN3", "centrali-ncu.json");

    public static List<NcuProfile> Read(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<ProfileFile>(stream, JsonOptions);
        if (document is not { Version: 1, Profiles: not null })
            throw new InvalidDataException("Formato della rubrica NCU non riconosciuto.");

        var result = new List<NcuProfile>();
        foreach (var entry in document.Profiles)
        {
            if (entry is null) throw new InvalidDataException("La rubrica contiene un profilo vuoto.");
            Upsert(result, Validate(entry));
        }
        return result;
    }

    public static void Save(string path, IReadOnlyList<NcuProfile> profiles)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".ncu-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = File.Create(temporary))
                JsonSerializer.Serialize(stream, new ProfileFile(1, profiles), JsonOptions);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static NcuProfile Validate(NcuProfile profile)
    {
        string building = profile.Building?.Trim() ?? "";
        if (building.Length is 0 or > 80 || building.Any(char.IsControl))
            throw new InvalidDataException("Inserisci il palazzo (da 1 a 80 caratteri).");
        if (profile.ControllerAddress is < 1 or > 50 || !Enum.IsDefined(profile.Lines) ||
            (profile.ControllerAddress == 50 && profile.Lines != NcuLines.A))
            throw new InvalidDataException("Indirizzo centrale o linee non validi. L'indirizzo 50 ammette solo la linea A.");

        if (profile.Mac is null || !System.Text.RegularExpressions.Regex.IsMatch(
                profile.Mac.Trim(), @"\A(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\z"))
            throw new InvalidDataException("Il profilo non contiene un MAC Bluetooth valido.");
        ulong mac = BluetoothRfcommClient.ParseAddress(profile.Mac);
        if (mac == 0) throw new InvalidDataException("Il MAC Bluetooth non può essere nullo.");
        string name = profile.Name?.Trim() ?? "";
        if (name.Length > 248 || name.Any(char.IsControl))
            throw new InvalidDataException("Il nome della centralina non è valido.");
        return profile with { Mac = BluetoothRfcommClient.FormatAddress(mac), Name = name, Building = building };
    }

    public static void Upsert(List<NcuProfile> profiles, NcuProfile profile)
    {
        int index = profiles.FindIndex(existing =>
            existing.Mac == profile.Mac && existing.ControllerAddress == profile.ControllerAddress);
        if (index < 0) profiles.Add(profile);
        else profiles[index] = profile;
    }

    public static string LineLabel(NcuLines lines) => lines switch { NcuLines.A => "A", NcuLines.B => "B", _ => "A+B" };

    public static string ExcelName(string report, string building, int controllerAddress, NcuLines lines, DateTime now)
    {
        string place = new string(building.Trim().Select(c =>
            char.IsControl(c) || WindowsInvalidFileNameChars.Contains(c) ? '-' : c).ToArray()).Trim(' ', '.', '-');
        if (place.Length == 0) place = "Senza-palazzo";
        return FormattableString.Invariant($"{report.Replace(' ', '-')}-{place}-C{controllerAddress:00}-Linee-{LineLabel(lines)}-{now:yyyyMMdd-HHmmss}.xlsx");
    }

    private sealed record ProfileFile(int Version, IReadOnlyList<NcuProfile> Profiles);
}
