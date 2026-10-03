# CS2 Recoil Reader & Recorder V0.2.4

Windows x64. F8 arma il recorder; il sinistro delimita lo spray. Il programma legge automaticamente l'arma equipaggiata e la sensibilità dal gioco, salva CSV/JSON e può creare subito un AMC di prova.

## Prova della macro senza video — V0.2.4

Premi **TEST AMC...** nel recorder e seleziona lo stesso AMC che esegui in Bloody. Poi torna a CS2, equipaggia l'arma e usa la sensibilità indicate, premi F8 e fai uno spray completo con la macro attiva, tenendo il mouse fisicamente fermo. Rilascia alla fine. La prova salva CSV/JSON e un **.execution.json** nella cartella Registrazioni. Per tornare alla registrazione normale usa **ESCI DAL TEST**.

La prova accetta le variazioni di visuale causate dalla macro e le confronta con la timeline dell'AMC. Il report stima ritardo, fattore di durata, guadagno orizzontale/verticale e scarto. Un fattore di durata maggiore di 1 indica una timeline più lenta. Assume m_pitch=m_yaw=0.022; una diversa impostazione di pitch/yaw può anche alterare il guadagno. Include gli effetti dell'elaborazione/lettura del gioco e non è una misura diretta del firmware Bloody o della direzione dei proiettili. Prove troppo brevi, assenza di movimento, scala incompatibile e stime ai limiti sono segnalate.

Durante il test l'esportazione automatica è disabilitata. Il converter rifiuta i file marcati come prove AMC: il movimento della macro non diventa un nuovo pattern di riferimento. Tutti i dati grezzi rimangono salvati. Il report non applica automaticamente correzioni non verificate.

V0.2.4 conserva anche il tempo del campione di rilascio nei CSV che lo contengono. Nei file senza rilascio osservato rimane la stima precedente; il report indica il metodo. Lo smoothing moderato di circa 10 ms conserva la geometria. La ricostruzione istantanea del recoil continua a essere non verificata.

[Analisi della registrazione AK47 fornita](ANALISI_AK47.md): 1658 campioni, 30 colpi e sensibilità 1.25 stabile; non è disponibile una misura dell'AMC in esecuzione.

## Uso

1. Estrai il pacchetto completo e apri CS2_Recoil_Pattern_Reader_And_Recorder_V0.2.4.exe.
2. Avvia CS2 con -insecure in una mappa di pratica locale.
3. Arma e sensibilità compaiono automaticamente. F8, torna al gioco, attendi un secondo con il sinistro rilasciato e spara da recoil azzerato, senza muovere il mouse.
4. Rilascia il sinistro. Con "Genera AMC automaticamente" attivo trovi la macro nella sottocartella Registrazioni/AMC.
5. CSV e JSON rimangono nella cartella Registrazioni. L'AMC ha un report associato.

CONVERTER apre una singola registrazione ZIP, il CSV/JSON associato oppure un AMC CS2 già esportato dal recorder; supporta trascinamento. Non richiede che CS2 sia aperto. I file V0.1 rimangono leggibili ma il loro nome arma e la sensibilità erano annotazioni manuali: il convertitore lo segnala.

La sensibilità destinazione usa per default il valore registrato. Per cambiarla, togli "Usa la sensibilità della registrazione" e scrivi, per esempio, 1.250: punto e tre decimali. Gli angoli sono convertiti nella scala della sensibilità destinazione; non vengono modificati i tempi.

## AMC

La conversione usa gli angoli base registrati negli aggiornamenti dei colpi e i tempi ricostruiti dai tick. Da V0.2.3 il converter suddivide ogni vecchio mezzo intervallo in passaggi lineari di circa 10 ms. I punti ai colpi e ai vecchi punti medi mantengono il loro tempo e la posizione cumulativa arrotondata. L'arrotondamento è applicato alla posizione cumulativa, evitando di accumulare errori sui piccoli movimenti. I passaggi senza spostamento vengono omessi; per questo alcuni Delay possono superare 10 ms.

Per rendere più fluido un AMC già creato: CONVERTER → APRI FILE → seleziona l'AMC → lascia attiva la sensibilità originale → CONVERTI IN AMC. Salva con un nome nuovo. Il convertitore mantiene ogni punto originale, il primo movimento al suo tempo originale, il rilascio e la pausa finale. Suddivide i salti successivi senza attraversare un cambio di direzione o attenuare l'ampiezza. Con la stessa sensibilità ogni punto originale rimane identico.

L'import AMC legge la sensibilità dall'intestazione ARMA · SENS 1.250. Non può rileggerla dal gioco né ricavare il numero di colpi: non dichiara queste informazioni come verificate automaticamente. Accetta solo macro CS2 con MoveR, Delay, LeftDown/LeftUp e pausa finale 30000 ms; altri comandi o dati incompleti producono un errore.

La macro contiene LeftDown all'inizio, LeftUp a fine sequenza e nel gestore di rilascio, quindi una pausa finale separata di 30000 ms. Importala in Bloody nella modalità "finché tieni premuto". La pausa ritarda la ripetizione della stessa attivazione; non è un blocco globale su una nuova pressione.

Il contatore tiene il massimo raggiunto: un rollback 29→28→29 non diventa un colpo numero 31. La conversione deduplica i tick del recoil e rifiuta colpi mancanti, cambi d'arma/sensibilità, zoom, aim punch esterno e movimento della visuale.

## Limiti da conoscere

- Gli angoli di base non misurano tutta la curva di recoil fra i colpi. I passaggi intermedi sono stime; l'AMC è una prova da verificare nel gioco, non una compensazione garantita.
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

Le verifiche misurano anche ritardo, durata e guadagni su prove sintetiche quantizzate, controllano il rilascio registrato e impediscono la conversione delle prove AMC in pattern. Eseguono l'EXE, controllano la memoria del solo processo di test, i due layout, CSV/JSON, import ZIP, validazione degli errori, geometria/tempi e pausa dell'AMC, preservazione dei punti originali e import/suddivisione degli AMC. Una fixture contiene gli aggiornamenti selezionati dalla registrazione AK dell'utente, incluso il rollback; i campioni intermedi sono omessi e le colonne diagnostiche non necessarie al convertitore sono sintetiche.

Il pacchetto include EXE, tutti i sorgenti, workflow, fixture, risultati delle verifiche, anteprime, SHA256 e AMC AK di esempio. La cartella AMC_AK47 contiene anche la versione moderata dell'AMC fornito per questa modifica: 58 movimenti a 50 ms diventano 286 movimenti con intervalli di almeno 10 ms; ogni punto originale rimane identico. Il workflow pubblica gli stessi file verificati in Downloads/V0.2.4 dopo i controlli, senza avviare un'altra build.

CS2 non viene eseguito in GitHub Actions. Le nuove letture automatiche dell'arma/sensibilità e la precisione della compensazione richiedono una prova reale.

Fonte dei campi: a2x/cs2-dumper, snapshot 2d204b1400eb08accfb4098ad954602448dacb07, MIT (licenza inclusa). La struttura dell'handle mantiene l'indice e il seriale; i controlli di identità impediscono l'uso di riferimenti scaduti.


## Diagnostica avvio V0.2.2

La lettura di arma e sensibilità usa una sessione senza richiedere i servizi del recoil. Questi ultimi rimangono obbligatori per registrare: non vengono sostituiti con valori inventati.

Gli errori di puntatore indicano ora il campo: giocatore/controller locale, regole della sessione, client della sessione, servizi delle armi, sensibilità, catena delle identità, identità/nome dell'arma e servizi del recoil. Il messaggio della V0.2 "entra prima in una mappa" veniva usato anche per i campi delle nuove funzioni e non consentiva di individuare il problema.

Il pulsante REPORT apre Diagnostica_CS2.txt, salvato vicino all'EXE dopo un errore. Se quella cartella non è scrivibile, viene usata la cartella locale dell'applicazione. Il report contiene il campo fallito, l'indirizzo/valore letti, i puntatori precedenti, la build, le versioni DLL e lo stack dell'errore. Non trasmette il file e non salva password, token o contenuti arbitrari della memoria.

Il report reale ha identificato il blocco: il globale dwEntityList del dump, client.dll + 0x2715828, restituisce 0x1 nella sessione 14188. V0.2.2 elimina questa lettura dal riconoscimento dell'arma.

Il nuovo percorso parte da m_pEntity del pawn locale e percorre m_pNext/m_pPrev delle identità, campi del medesimo dump. Cerca il riferimento completo dell'arma attiva (indice e seriale), verifica il collegamento identità/entità e poi legge l'ID dell'oggetto e il nome designer. Valida anche il riferimento mantenuto in cache; interrompe la lettura se viene riciclato.

La ricerca ha un limite di 32768 identità/2 secondi e riconosce i cicli. I collegamenti devono essere coerenti. Non scansiona regioni arbitrarie della memoria e non prova offset a caso. Durante lo spray viene usata l'arma già risolta, con verifica del suo riferimento.

Una regressione in memoria del solo processo di test riproduce il globale 0x1 e controlla identificazione, seriali diversi, ricerca nelle due direzioni, cache scaduta, collegamenti incoerenti e cicli. La correzione del blocco è verificata sul caso riprodotto. L'utente ha confermato il riconoscimento dell'AK47 e la registrazione con V0.2.2; la fluidità di V0.2.4 richiede la sua prova in gioco.

Campi usati: output/client_dll.json alla revisione 2d204b1400eb08accfb4098ad954602448dacb07. m_pEntity=0x10, m_pPrev=0x50, m_pNext=0x58 e m_flags=0x30. Il report utente non è incluso nel repository; la regressione usa esclusivamente indirizzi e dati del processo di test.
