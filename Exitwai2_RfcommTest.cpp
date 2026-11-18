#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2bth.h>
#include <windows.h>
#include <cstdio>
#include <cstddef>
#include <cstring>
#pragma comment(lib, "Ws2_32.lib")

int main() {
    constexpr ULONGLONG target = 0x0080E1B004C0ULL;
    WSADATA wd{};
    int startup = WSAStartup(MAKEWORD(2,2), &wd);
    if (startup != 0) { std::printf("WSAStartup = %d\n", startup); return 1; }
    std::printf("SOCKADDR_BTH sizeof=%zu offsets: family=%zu addr=%zu guid=%zu port=%zu\n",
        sizeof(SOCKADDR_BTH), offsetof(SOCKADDR_BTH,addressFamily), offsetof(SOCKADDR_BTH,btAddr),
        offsetof(SOCKADDR_BTH,serviceClassId), offsetof(SOCKADDR_BTH,port));
    std::printf("Destinazione NCU 00:80:E1:B0:04:C0\n");
    // The two attempts are independent. No payload is transmitted.
    for (int test=0; test<3; ++test) {
        SOCKET sock = socket(AF_BTH, SOCK_STREAM, BTHPROTO_RFCOMM);
        if (sock == INVALID_SOCKET) { std::printf("socket: WSA=%d\n", WSAGetLastError()); continue; }
        SOCKADDR_BTH local{};
        local.addressFamily = AF_BTH;
        local.btAddr = 0;
        local.serviceClassId = GUID_NULL;
        local.port = 0;
        if (test == 1 || test == 2) {
            int brc = bind(sock, (SOCKADDR*)&local, sizeof(local));
            std::printf("Test %d: bind = %d, WSA=%d\n", test, brc, brc ? WSAGetLastError() : 0);
            if (brc != 0) { closesocket(sock); continue; }
        }
        SOCKADDR_BTH remote{};
        remote.addressFamily = AF_BTH;
        remote.btAddr = target;
        remote.serviceClassId = GUID_NULL;
        remote.port = 1;
        if (test == 2) {
            // UUID 0x1101, ask SDP to choose a remote channel.
            remote.port = 0;
            remote.serviceClassId = { 0x00001101, 0x0000, 0x1000, {0x80,0x00,0x00,0x80,0x5F,0x9B,0x34,0xFB} };
        }
        const char* description = test == 0 ? "direct no bind" : test == 1 ? "direct with bind" : "SDP with bind";
        std::printf("Test %d: connect (%s)...\n", test, description);
        int crc = connect(sock, (SOCKADDR*)&remote, sizeof(remote));
        std::printf("Test %d: connect = %d, WSA=%d\n", test, crc, crc ? WSAGetLastError() : 0);
        if (crc == 0) {
            std::printf("CONNESSIONE RIUSCITA. Nessun telegramma inviato.\n");
        }
        closesocket(sock);
    }
    WSACleanup();
    return 0;
}
