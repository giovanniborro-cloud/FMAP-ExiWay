using System.Security.Cryptography;
using System.Text;

namespace SchneiderLoader;

internal static class NcuLoginStore
{
    public static void Save(string mac, string loginFrame)
    {
        string path = PathFor(mac);
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, ProtectedData.Protect(Encoding.ASCII.GetBytes(loginFrame),
                Encoding.ASCII.GetBytes(mac), DataProtectionScope.CurrentUser));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static string? Read(string mac)
    {
        string path = PathFor(mac);
        if (!File.Exists(path)) return null;
        byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), Encoding.ASCII.GetBytes(mac), DataProtectionScope.CurrentUser);
        return Encoding.ASCII.GetString(bytes);
    }

    private static string PathFor(string mac) => Path.Combine(Path.GetDirectoryName(NcuProfiles.LocalPath)!,
        "Login", mac.Replace(":", "", StringComparison.Ordinal) + ".bin");
}
