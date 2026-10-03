# CS2 Recoil Reader & Recorder V0.2.2

Windows x64. F8 arma il recorder; il sinistro delimita lo spray. Il programma legge automaticamente l'arma equipaggiata e la sensibilità dal gioco, salva CSV/JSON e può creare subito un AMC di prova.

## Uso

1. Estrai il pacchetto completo e apri CS2_Recoil_Pattern_Reader_And_Recorder_V0.2.2.exe.
2. Avvia CS2 con -insecure in una mappa di pratica locale.
3. Arma e sensibilità compaiono automaticamente. F8, torna al gioco, attendi un secondo con il sinistro rilasciato e spara da recoil azzerato, senza muovere il mouse.
4. Rilascia il sinistro. Con "Genera AMC automaticamente" attivo trovi la macro nella sottocartella Registrazioni/AMC.
5. CSV e JSON rimangono nella cartella Registrazioni. L'AMC ha un report associato.

CONVERTER apre una singola registrazione ZIP oppure il CSV/JSON associato; supporta trascinamento. Non richiede che CS2 sia aperto. I file V0.1 rimangono leggibili ma il loro nome arma e la sensibilità erano annotazioni manuali: il convertitore lo segnala.

La sensibilità destinazione usa per default il valore registrato. Per cambiarla, togli "Usa la sensibilità della registrazione" e scrivi, per esempio, 1.250: punto e tre decimali. Gli angoli sono convertiti nella scala della sensibilità destinazione; non vengono modificati i tempi.

## AMC

La conversione riproduce la prova già fatta sull'AK: angoli base registrati negli aggiornamenti dei colpi, due passaggi lineari stimati per intervallo, tempi ricostruiti dai tick. Non usa vecchi pattern o uno smoothing per frame.

La macro contiene LeftDown all'inizio, LeftUp a fine sequenza e nel gestore di rilascio, quindi una pausa finale separata di 30000 ms. Importala in Bloody nella modalità "finché tieni premuto". La pausa ritarda la ripetizione della stessa attivazione; non è un blocco globale su una nuova pressione.

Il contatore tiene il massimo raggiunto: un rollback 29→28→29 non diventa un colpo numero 31. La conversione deduplica i tick del recoil e rifiuta colpi mancanti, cambi d'arma/sensibilità, zoom, aim punch esterno e movimento della visuale.

## Limiti da conoscere

- Gli angoli di base non misurano tutta la curva di recoil fra i colpi. I due passaggi intermedi sono stime; l'AMC è una prova da verificare nel gioco, non una compensazione garantita.
- m_pitch/m_yaw=0.022, weapon_recoil_scale=2.0 e tick a 64Hz sono assunti, non letti automaticamente. La sensibilità base viene letta realmente da dwSensitivity.
- La conversione è per registrazioni senza zoom/ADS. Non compensa la dispersione casuale dei proiettili.
- Il rilevamento usa handle dell'arma attiva, ID dell'oggetto e nome designer dell'entità; verifica l'identità e il numero seriale del riferimento. Niente nomi inseriti a mano per nuove registrazioni.
- Layout vincolato alla build motore 14188: una build diversa ferma le letture. Nessun download di offset a runtime.
- Richiede -insecure e rifiuta IsValveDS. Questi controlli da soli non dimostrano che la sessione sia locale: scegli una mappa di pratica locale.
- I tempi observed_ms misurano l'osservazione sul PC. Il report conserva gli scarti rispetto ai tick.
- Letture con PROCESS_VM_READ | PROCESS_QUERY_INFORMATION. Il programma non scrive nel gioco e non riproduce input; il file AMC verrà eseguito dal software del mouse.

## Build e verifiche

Workflow: .github/workflows/build-windows.yml. Compilazione Windows x64 con .NET Framework, senza Python o NuGet.

Per ricompilare: powershell.exe -NoProfile -File .\build.ps1

Le verifiche eseguono l'EXE, controllano la memoria del solo processo di test, i due layout, CSV/JSON, import ZIP, validazione degli errori, geometria/tempi e pausa dell'AMC. Una fixture contiene gli aggiornamenti selezionati dalla registrazione AK dell'utente, incluso il rollback; i campioni intermedi sono omessi e le colonne diagnostiche non necessarie al convertitore sono sintetiche.

Il pacchetto include EXE, tutti i sorgenti, workflow, fixture, risultati delle verifiche, anteprime, SHA256 e AMC AK di esempio. Il workflow pubblica gli stessi file verificati in Downloads/V0.2.2 dopo i controlli, senza avviare un'altra build.

CS2 non viene eseguito in GitHub Actions. Le nuove letture automatiche dell'arma/sensibilità e la precisione della compensazione richiedono una prova reale.

Fonte dei campi: a2x/cs2-dumper, snapshot 2d204b1400eb08accfb4098ad954602448dacb07, MIT (licenza inclusa). La struttura dell'handle mantiene l'indice e il seriale; i controlli di identità impediscono l'uso di riferimenti scaduti.


## Diagnostica avvio V0.2.2

La lettura di arma e sensibilità usa una sessione senza richiedere i servizi del recoil. Questi ultimi rimangono obbligatori per registrare: non vengono sostituiti con valori inventati.

Gli errori di puntatore indicano ora il campo: giocatore/controller locale, regole della sessione, client della sessione, servizi delle armi, sensibilità, catena delle identità, identità/nome dell'arma e servizi del recoil. Il messaggio della V0.2 "entra prima in una mappa" veniva usato anche per i campi delle nuove funzioni e non consentiva di individuare il problema.

Il pulsante REPORT apre Diagnostica_CS2.txt, salvato vicino all'EXE dopo un errore. Se quella cartella non è scrivibile, viene usata la cartella locale dell'applicazione. Il report contiene il campo fallito, l'indirizzo/valore letti, i puntatori precedenti, la build, le versioni DLL e lo stack dell'errore. Non trasmette il file e non salva password, token o contenuti arbitrari della memoria.

Il report reale ha identificato il blocco: il globale dwEntityList del dump, client.dll + 0x2715828, restituisce 0x1 nella sessione 14188. V0.2.2 elimina questa lettura dal riconoscimento dell'arma.

Il nuovo percorso parte da m_pEntity del pawn locale e percorre m_pNext/m_pPrev delle identità, campi del medesimo dump. Cerca il riferimento completo dell'arma attiva (indice e seriale), verifica il collegamento identità/entità e poi legge l'ID dell'oggetto e il nome designer. Valida anche il riferimento mantenuto in cache; interrompe la lettura se viene riciclato.

La ricerca ha un limite di 32768 identità/2 secondi e riconosce i cicli. I collegamenti devono essere coerenti. Non scansiona regioni arbitrarie della memoria e non prova offset a caso. Durante lo spray viene usata l'arma già risolta, con verifica del suo riferimento.

Una regressione in memoria del solo processo di test riproduce il globale 0x1 e controlla identificazione, seriali diversi, ricerca nelle due direzioni, cache scaduta, collegamenti incoerenti e cicli. La correzione del blocco è verificata sul caso riprodotto; resta da provare il percorso nella sessione CS2 reale.

Campi usati: output/client_dll.json alla revisione 2d204b1400eb08accfb4098ad954602448dacb07. m_pEntity=0x10, m_pPrev=0x50, m_pNext=0x58 e m_flags=0x30. Il report utente non è incluso nel repository; la regressione usa esclusivamente indirizzi e dati del processo di test.
