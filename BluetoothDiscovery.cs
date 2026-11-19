using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SchneiderLoader;

internal static class BluetoothDiscovery
{
    internal sealed record Device(string Name, string Mac, bool Paired, bool Connected)
    {
        public bool IsNcu => Name.StartsWith("NCU", StringComparison.OrdinalIgnoreCase);

        public override string ToString()
        {
            string displayName = Name.Length == 0 ? "Dispositivo senza nome" : Name;
            return $"{displayName} [{Mac}] · {(Paired ? "associato" : "non associato")}";
        }
    }

    private const int NoMoreItems = 259;
    private const byte InquiryTimeoutUnits = 8; // Windows uses 1.28-second units.

    // Run an active Bluetooth Classic inquiry as well as including cached devices.
    public static List<Device> Scan()
    {
        var devices = new List<Device>();
        var search = new BluetoothDeviceSearchParams
        {
            dwSize = Marshal.SizeOf<BluetoothDeviceSearchParams>(),
            fReturnAuthenticated = true, fReturnRemembered = true,
            fReturnUnknown = true, fReturnConnected = true,
            fIssueInquiry = true, cTimeoutMultiplier = InquiryTimeoutUnits,
            hRadio = IntPtr.Zero
        };
        var info = new BluetoothDeviceInfo { dwSize = Marshal.SizeOf<BluetoothDeviceInfo>() };
        IntPtr handle = BluetoothFindFirstDevice(ref search, ref info);
        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == NoMoreItems) return devices;
            throw new Win32Exception(error);
        }
        try
        {
            do
            {
                if ((info.Address & 0x0000FFFFFFFFFFFFUL) != 0)
                {
                    string mac = BluetoothRfcommClient.FormatAddress(info.Address);
                    devices.Add(new Device(info.szName?.Trim() ?? "", mac, info.fAuthenticated, info.fConnected));
                }
                info.dwSize = Marshal.SizeOf<BluetoothDeviceInfo>();
            } while (BluetoothFindNextDevice(handle, ref info));

            int error = Marshal.GetLastWin32Error();
            if (error != NoMoreItems) throw new Win32Exception(error);
        }
        finally { BluetoothFindDeviceClose(handle); }
        return devices.OrderByDescending(d => d.IsNcu)
            .ThenBy(d => d.Paired)
            .ThenBy(d => d.Name.Length == 0)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Mac, StringComparer.Ordinal)
            .ToList();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothDeviceSearchParams
    {
        public int dwSize;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo
    {
        public int dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
        public SystemTime stLastSeen;
        public SystemTime stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
    }
    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref BluetoothDeviceSearchParams search, ref BluetoothDeviceInfo info);
    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr find, ref BluetoothDeviceInfo info);
    [DllImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr find);
}
