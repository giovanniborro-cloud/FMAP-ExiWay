using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SchneiderLoader;

// Windows Bluetooth Classic client. First tries the known NCU RFCOMM channel 1,
// then resolves the standard Serial Port Profile (SPP) service via SDP.
internal sealed class BluetoothRfcommClient : IDisposable
{
    private const int AfBth = 32;
    private const int SockStream = 1;
    private const int BthProtoRfcomm = 3;
    private const int SockaddrBthLength = 30;
    private const int SolSocket = 0xFFFF;
    private const int SoRcvTimeout = 0x1006;
    private const int SoSndTimeout = 0x1005;
    private static readonly IntPtr InvalidSocket = new(-1);
    private static readonly Guid SerialPortService = new("00001101-0000-1000-8000-00805f9b34fb");
    private readonly object _writeLock = new();
    private IntPtr _socket = InvalidSocket;
    private bool _wsaStarted;

    public bool Connected { get; private set; }

    public static ulong ParseAddress(string value)
    {
        string digits = new(value.Where(Uri.IsHexDigit).ToArray());
        if (digits.Length != 12 || !ulong.TryParse(digits, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out ulong result))
            throw new FormatException("MAC Bluetooth non valido: servono 12 cifre esadecimali.");
        return result;
    }

    public static string FormatAddress(ulong address) =>
        string.Join(":", Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2")));

    public void Connect(string macText, uint channel = 1, Action<string>? diagnostic = null)
    {
        Dispose();
        ulong address = ParseAddress(macText);
        if (address == 0) throw new FormatException("Indirizzo Bluetooth nullo.");
        if (channel is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(channel));
        IntPtr wsadata = Marshal.AllocHGlobal(512);
        try
        {
            int rc = WSAStartup(0x0202, wsadata);
            if (rc != 0) throw new Win32Exception(rc, "WSAStartup fallita");
            _wsaStarted = true;
        }
        finally { Marshal.FreeHGlobal(wsadata); }

        try
        {
            // First: connect directly, without an explicit bind (as used by many clients).
            // Second: repeat with a real local radio address, where available.
            var localRadios = GetLocalRadioAddresses(diagnostic);
            int directError = TryConnect(address, channel, Guid.Empty, diagnostic,
                "RFCOMM 1 / senza bind", null);
            if (directError == 0) return;

            foreach (ulong localRadio in localRadios.Where(x => x != 0))
            {
                int error = TryConnect(address, channel, Guid.Empty, diagnostic,
                    $"RFCOMM 1 / radio locale {FormatAddress(localRadio)}", localRadio);
                if (error == 0) return;
            }

            diagnostic?.Invoke($"RFCOMM 1 non connesso (ultimo errore {directError}). Prova SDP SPP 0x1101...");
            int sdpError = TryConnect(address, 0, SerialPortService, diagnostic,
                "SDP SPP / senza bind", null);
            if (sdpError == 0) return;
            foreach (ulong localRadio in localRadios.Where(x => x != 0))
            {
                int error = TryConnect(address, 0, SerialPortService, diagnostic,
                    $"SDP SPP / radio locale {FormatAddress(localRadio)}", localRadio);
                if (error == 0) return;
            }
            throw new Win32Exception(sdpError,
                $"RFCOMM 1 = {directError}; SDP SPP = {sdpError}. " +
                "I log dei tentativi senza bind e con radio locale distinguono un problema dello stack Windows da un errore remoto.");
        }
        catch
        {
            CloseSocket();
            throw;
        }
    }

    private int TryConnect(ulong address, uint port, Guid serviceGuid, Action<string>? diagnostic,
        string description, ulong? localRadio)
    {
        CloseSocket();
        _socket = socket(AfBth, SockStream, BthProtoRfcomm);
        if (_socket == InvalidSocket)
        {
            int socketError = WSAGetLastError();
            diagnostic?.Invoke($"{description}: CREAZIONE SOCKET FALLITA Win32 {socketError}");
            return socketError;
        }

        if (localRadio is ulong localAddress)
        {
            byte[] local = CreateSocketAddress(localAddress, 0, Guid.Empty);
            diagnostic?.Invoke($"{description}: bind con radio locale {FormatAddress(localAddress)}, porta=0");
            if (bind(_socket, local, local.Length) != 0)
            {
                int bindError = WSAGetLastError();
                diagnostic?.Invoke($"{description}: BIND FALLITO Win32 {bindError}");
                CloseSocket();
                return bindError;
            }
            diagnostic?.Invoke($"{description}: bind riuscito");
        }
        else diagnostic?.Invoke($"{description}: nessun bind esplicito, endpoint gestito da Windows");

        byte[] sockaddr = CreateSocketAddress(address, port, serviceGuid);
        diagnostic?.Invoke($"Tentativo {description}: MAC={FormatAddress(address)}, porta={port}, UUID={serviceGuid}");
        if (connect(_socket, sockaddr, sockaddr.Length) != 0)
        {
            int error = WSAGetLastError();
            diagnostic?.Invoke($"{description}: errore Win32 {error}");
            CloseSocket();
            return error;
        }
        Connected = true;
        diagnostic?.Invoke($"Connesso tramite {description}.");
        SetTimeout(SoRcvTimeout, 1000);
        SetTimeout(SoSndTimeout, 5000);
        return 0;
    }

    private static byte[] CreateSocketAddress(ulong address, uint port, Guid serviceGuid)
    {
        // ws2bth.h declares SOCKADDR_BTH with one-byte packing.
        byte[] sockaddr = new byte[SockaddrBthLength];
        BinaryPrimitives.WriteUInt16LittleEndian(sockaddr, AfBth);
        BinaryPrimitives.WriteUInt64LittleEndian(sockaddr.AsSpan(2), address);
        serviceGuid.TryWriteBytes(sockaddr.AsSpan(10, 16));
        BinaryPrimitives.WriteUInt32LittleEndian(sockaddr.AsSpan(26), port);
        return sockaddr;
    }

    private static List<ulong> GetLocalRadioAddresses(Action<string>? diagnostic)
    {
        var addresses = new List<ulong>();
        IntPtr findHandle = IntPtr.Zero;
        try
        {
            var parameters = new BluetoothFindRadioParams { dwSize = Marshal.SizeOf<BluetoothFindRadioParams>() };
            findHandle = BluetoothFindFirstRadio(ref parameters, out IntPtr radio);
            if (findHandle == IntPtr.Zero)
            {
                diagnostic?.Invoke($"Radio locali: nessuna radio enumerata (Win32 {Marshal.GetLastWin32Error()})");
                return addresses;
            }
            do
            {
                try
                {
                    // BLUETOOTH_RADIO_INFO: size=520, address at offset 8.
                    byte[] info = new byte[520];
                    BitConverter.GetBytes((uint)info.Length).CopyTo(info, 0);
                    uint rc = BluetoothGetRadioInfo(radio, info);
                    if (rc == 0)
                    {
                        ulong mac = BitConverter.ToUInt64(info, 8);
                        if (mac != 0 && !addresses.Contains(mac)) addresses.Add(mac);
                        diagnostic?.Invoke($"Radio Bluetooth locale: {FormatAddress(mac)}");
                    }
                    else diagnostic?.Invoke($"BluetoothGetRadioInfo: Win32 {rc}");
                }
                finally { if (radio != IntPtr.Zero) CloseHandle(radio); }
            }
            while (BluetoothFindNextRadio(findHandle, out radio));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            diagnostic?.Invoke("Enumerazione radio non disponibile: " + ex.Message);
        }
        finally { if (findHandle != IntPtr.Zero) BluetoothFindRadioClose(findHandle); }
        return addresses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams { public int dwSize; }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BluetoothFindRadioParams parameters, out IntPtr radio);
    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextRadio(IntPtr find, out IntPtr radio);
    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr find);
    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothGetRadioInfo(IntPtr radio, byte[] info);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    private void SetTimeout(int option, int ms)
    {
        byte[] bytes = BitConverter.GetBytes(ms);
        if (setsockopt(_socket, SolSocket, option, bytes, bytes.Length) != 0)
            throw new Win32Exception(WSAGetLastError(), "Configurazione timeout Bluetooth fallita");
    }

    public void SendAscii(string data)
    {
        if (!Connected) throw new InvalidOperationException("Bluetooth non connesso");
        byte[] payload = Encoding.ASCII.GetBytes(data);
        lock (_writeLock)
        {
            int sentTotal = 0;
            while (sentTotal < payload.Length)
            {
                byte[] remaining = payload[sentTotal..];
                int sent = send(_socket, remaining, remaining.Length, 0);
                if (sent <= 0) throw new Win32Exception(WSAGetLastError(), "Invio Bluetooth fallito");
                sentTotal += sent;
            }
        }
    }

    public int Receive(byte[] buffer)
    {
        if (!Connected) return 0;
        int count = recv(_socket, buffer, buffer.Length, 0);
        if (count >= 0) return count;
        int error = WSAGetLastError();
        if (error == 10060) return -1;
        if (error is 10004 or 10038 or 10053 or 10054 or 10101) return 0;
        throw new Win32Exception(error, "Ricezione Bluetooth fallita");
    }

    private void CloseSocket()
    {
        IntPtr s = _socket;
        _socket = InvalidSocket;
        bool wasConnected = Connected;
        Connected = false;
        if (s != InvalidSocket)
        {
            if (wasConnected) shutdown(s, 2);
            closesocket(s);
        }
    }

    public void Dispose()
    {
        CloseSocket();
        if (_wsaStarted) { WSACleanup(); _wsaStarted = false; }
    }

    [DllImport("Ws2_32.dll")] private static extern int WSAStartup(ushort version, IntPtr data);
    [DllImport("Ws2_32.dll")] private static extern int WSACleanup();
    [DllImport("Ws2_32.dll")] private static extern IntPtr socket(int af, int type, int protocol);
    [DllImport("Ws2_32.dll")] private static extern int bind(IntPtr s, byte[] name, int namelen);
    [DllImport("Ws2_32.dll")] private static extern int getsockname(IntPtr s, byte[] name, ref int namelen);
    [DllImport("Ws2_32.dll")] private static extern int connect(IntPtr s, byte[] name, int namelen);
    [DllImport("Ws2_32.dll")] private static extern int setsockopt(IntPtr s, int level, int optname, byte[] optval, int optlen);
    [DllImport("Ws2_32.dll")] private static extern int send(IntPtr s, byte[] buf, int len, int flags);
    [DllImport("Ws2_32.dll")] private static extern int recv(IntPtr s, byte[] buf, int len, int flags);
    [DllImport("Ws2_32.dll")] private static extern int shutdown(IntPtr s, int how);
    [DllImport("Ws2_32.dll")] private static extern int closesocket(IntPtr s);
    [DllImport("Ws2_32.dll")] private static extern int WSAGetLastError();
}
