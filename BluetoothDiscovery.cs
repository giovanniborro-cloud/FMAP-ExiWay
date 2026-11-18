using System.Runtime.InteropServices;

namespace SchneiderLoader;

internal static class BluetoothDiscovery
{
    internal sealed record Device(string Name, string Mac, bool Paired, bool Connected)
    {
        public override string ToString() => $"{Name}  [{Mac}]" + (Paired ? "  associato" : "");
    }

    // Windows Bluetooth Classic discovery using bthprops.cpl. Includes known and newly discovered devices.
    public static List<Device> Scan()
    {
        var devices = new List<Device>();
        var search = new BluetoothDeviceSearchParams
        {
            dwSize = Marshal.SizeOf<BluetoothDeviceSearchParams>(),
            fReturnAuthenticated = true, fReturnRemembered = true,
            fReturnUnknown = true, fReturnConnected = true,
            fIssueInquiry = true, cTimeoutMultiplier = 4,
            hRadio = IntPtr.Zero
        };
        var info = new BluetoothDeviceInfo { dwSize = Marshal.SizeOf<BluetoothDeviceInfo>() };
        IntPtr handle = BluetoothFindFirstDevice(ref search, ref info);
        if (handle == IntPtr.Zero) return devices;
        try
        {
            do
            {
                string mac = string.Join(":", Enumerable.Range(0, 6)
                    .Select(i => ((info.Address >> (8 * (5 - i))) & 0xFF).ToString("X2")));
                if (!string.IsNullOrWhiteSpace(info.szName))
                    devices.Add(new Device(info.szName, mac, info.fAuthenticated, info.fConnected));
                info.dwSize = Marshal.SizeOf<BluetoothDeviceInfo>();
            } while (BluetoothFindNextDevice(handle, ref info));
        }
        finally { BluetoothFindDeviceClose(handle); }
        return devices.OrderByDescending(d => d.Name.StartsWith("NCU", StringComparison.OrdinalIgnoreCase)).ThenBy(d => d.Name).ToList();
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
    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode)]
    private static extern IntPtr BluetoothFindFirstDevice(ref BluetoothDeviceSearchParams search, ref BluetoothDeviceInfo info);
    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr find, ref BluetoothDeviceInfo info);
    [DllImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr find);
}
