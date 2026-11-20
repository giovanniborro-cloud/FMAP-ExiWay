# Installazione OVA-GEN3

Sorgenti della build Release: commit `33aa244`.

| Tipo di Windows | Pacchetto |
| --- | --- |
| Sistema operativo a 32 bit | `OVA-GEN3-win-x86.zip` |
| Sistema operativo a 64 bit | `OVA-GEN3-win-x64.zip` |

1. Controlla **Impostazioni → Sistema → Informazioni → Tipo sistema**.
2. Estrai tutto lo ZIP in una cartella sul PC, ad esempio `C:\OVA-GEN3`.
3. Avvia `SchneiderLoader.exe` dalla cartella estratta.

Il runtime .NET Desktop è incluso. La cartella va mantenuta intera. Per Windows 10 Home su Intel Atom x5-Z8350 è ancora da verificare sul PC l'avvio e la connessione Bluetooth al controller.

La rubrica NCU è salvata in `%LOCALAPPDATA%\OVA-GEN3\centrali-ncu.json`. Per trasferirla su un altro PC, usa **Esporta JSON** e poi **Importa JSON** nell'altra installazione.
