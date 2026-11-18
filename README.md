# OVA-GEN3 / Exitwai2 – diagnostica radio locale (18-11-2026)

Questa versione usa **solo Bluetooth Classic RFCOMM** e non riattiva Ethernet/Modbus o COM/Modbus.

## Perché
Il PC rileva `NCU33fab915` all’indirizzo `00:80:E1:B0:04:C0` (paired=true), ma la prova `bind` con MAC locale zero e porta zero restituisce Win32 10049. Il problema precede la richiesta di autenticazione Schneider `323`.

## Novità
1. Enumerazione delle radio Bluetooth **locali** con `BluetoothFindFirstRadio` e `BluetoothGetRadioInfo`.
2. Tentativo di connessione RFCOMM 1 senza `bind` esplicito, lasciando scegliere a Windows l'endpoint locale.
3. In caso di insuccesso, nuovo tentativo con `bind` sull'indirizzo di ciascuna radio locale (porta locale 0).
4. Stesso schema per il servizio SDP Serial Port Profile 0x1101.
5. Log di ciascun tentativo separato (creazione socket, bind quando previsto, connect).

## Test
Chiudere eCommission sul tablet. Estrarre i file nella cartella originale dopo backup, ricompilare `SchneiderLoader.slnx`, premere Cerca NCU e Connetti. Copiare tutte le righe da `Radio Bluetooth locale` fino a `ERRORE connessione`.

**Nota:** questa modifica non garantisce che il controller pubblichi SPP o che Windows consenta la connessione. Non sono inviati comandi DALI durante l'apertura. Il progetto non è stato compilato né testato su Windows in questo ambiente.
