# CS2 Recoil Pattern Reader And Recorder V0.1

Prova offline per Windows x64: F8 arma o termina il record, il pulsante sinistro delimita lo spray. I valori vengono letti dalla memoria di cs2.exe e salvati in CSV e JSON.

Questa versione registra i valori grezzi di AimPunchServices e i tempi osservati. Non genera ancora AMC: la ricostruzione del recoil istantaneo deve essere verificata sui dati del gioco.

## Prova

1. Estrai il pacchetto completo e apri `CS2_Recoil_Pattern_Reader_And_Recorder_V0.1.exe`.
2. Apri CS2 con `-insecure` in una mappa di pratica locale.
3. Scrivi l'etichetta dell'arma, premi F8, torna al gioco e attendi un secondo.
4. Spara un caricatore tenendo fermo il mouse; rilascia il sinistro.
5. Apri Registrazioni e conserva entrambi i file CSV e JSON.

La sensibilità 1.250 è solo un'annotazione. Il nome dell'arma è manuale; il relativo hash è letto dal gioco.

## Compatibilità e dati

- Layout vincolato alla build motore 14188, snapshot del 2026-10-01.
- Una build motore diversa ferma la prova; non scarica offset a runtime.
- Il layout non è stato verificato su una sessione CS2 reale.
- Gli angoli base del servizio non sono automaticamente il recoil istantaneo.
- I tempi QPC rappresentano l'osservazione sul PC, non l'istante esatto dello sparo nel motore.
- Il polling richiesto è 1 ms. I gap effettivi e gli aggiornamenti dei colpi saltati vengono riportati nel JSON.
- Richiede -insecure e rifiuta IsValveDS; questi controlli non provano che il server sia locale. Usa una mappa di pratica locale.

Apre il processo con PROCESS_VM_READ e PROCESS_QUERY_INFORMATION. Non scrive nella memoria del gioco e non simula input.

## Compilazione

Workflow: `.github/workflows/build-windows.yml`. Usa Windows Server 2022 e il compilatore .NET Framework x64 già presente; non richiede NuGet o Python.

Per ricompilare il progetto in Windows:

```powershell
powershell.exe -NoProfile -File .\build.ps1
```

Il workflow verifica la compilazione, il caricamento dell'EXE, la lettura della sola memoria del processo di test, l'interfaccia e l'esportazione CSV/JSON di dati sintetici. Non esegue CS2 e non verifica il comportamento del recoil nel gioco.

Il pacchetto completo contiene EXE, sorgenti, workflow, verifica automatica e SHA256.

Fonte del layout: [a2x/cs2-dumper](https://github.com/a2x/cs2-dumper/tree/2d204b1400eb08accfb4098ad954602448dacb07), licenza MIT inclusa.
