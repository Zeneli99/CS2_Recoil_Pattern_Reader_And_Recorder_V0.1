# RecoilLabs / CS2 Recoil Reader & Recorder V0.3.3 — XML Razer Synapse 4

La V0.3.3 aggiunge l'export XML per **Razer Synapse 4** secondo la calibrazione M249 verificata in gioco dall'utente sul Naga V3 Pro. Mantiene la compatibilita' CS2 14189 della V0.3.2 e i motori AMC identici alla V0.3.1. L'AMC e' il master: l'export XML legge il file gia' salvato e non modifica X/Y, ordine o numero dei MoveR.

Seleziona **Esporta anche XML Razer · Synapse 4** nella schermata principale o nel converter; nel recorder usa **XML Razer** insieme a **Genera AMC**. I file vengono salvati in sottocartelle sorelle **AMC** e **XML** della cartella scelta. I parametri e report AMC rimangono accanto al master. L'opzione XML parte disattivata; il risultato AMC non dipende dall'opzione. Se l'XML fallisce, l'AMC gia' salvato viene conservato e l'errore viene mostrato.

Per convertire un AMC esistente direttamente, aprilo nel converter e premi **XML RAZER DA AMC**. Questo pulsante conserva tutti i comandi originali, anche consecutivi; non usa lo smoothing o i controlli di sensibilita'. **CONVERTI IN AMC**, con l'opzione XML attiva, mantiene invece il comportamento del converter esistente e deriva l'XML dall'AMC risultante. Un eventuale cambio di sensibilita' avviene nell'AMC tramite il converter esistente; l'XML non aggiunge un altro fattore.

## Calibrazione Synapse 4

Ogni Buffer successivo all'origine usa coordinate cumulative; la differenza X/Y riproduce esattamente il relativo MoveR AMC. Il tempo del Buffer e' la somma dei Delay AMC dall'ultimo movimento **+1 ms per quel MoveR**. Anche i MoveR consecutivi ricevono ciascuno 1 ms; non vengono fusi. Si leggono i comandi grezzi, senza riapplicare il costo gia' dichiarato da `MoveRCommandCostMs=1` nelle timeline AMC. Il master non viene corretto o riscritto.

Il formato replica il golden verificato: `mmtSetting=3`, `MouseMoveType=relative`, Start Point (mouse cursor), origine 500/300 a tempo 0, LeftDown prima della traiettoria, LeftUp dopo il delay di rilascio originale. `Number` della traiettoria e' la somma dei tempi Buffer in secondi; i singoli tempi Buffer sono intervalli in millisecondi. La pausa Bloody dopo LeftUp viene omessa dall'XML. Importa in **Synapse 4**, che gestisce la modalita' di attivazione della macro nel proprio binding.

Il golden M249 fornito e' `tests/fixtures/M249_Razer_Synapse4_CAL1_MoveCost1ms.xml`: **765 MoveR, X +25, Y +448, 7155 ms di Delay prima dell'ultimo movimento +765 ms =7920 ms di traiettoria**, poi **79 ms** fino a LeftUp (rilascio a 7999 ms). L'utente ha confermato il test in gioco. I test della nuova implementazione confrontano tutti i Buffer con questo file e verificano anche comandi consecutivi, costo gia' dichiarato, rilascio e assenza di modifiche al master. Il file AMC originale non e' stato materializzabile (403): il test M249 usa una fixture esplicitamente ricostruita dal golden XML, non pretende di confrontare i byte del master originale. Il golden XML resta nei test e non viene incorporato nell'EXE.

Ogni XML ha un `.report.json` nella cartella XML, con percorso e SHA256 del master, numero di movimenti, X/Y, somma dei Delay, costo MoveR, tempo della traiettoria, tempo di rilascio e pausa omessa. L'export non esegue macro o input del mouse.

## Generazione AMC invariata

La generazione esistente conserva **1 ms di costo per comando MoveR**. Un movimento pianificato ogni 10 ms viene scritto normalmente con Delay 9 ms e un MoveR. Se un movimento viene saltato perché X/Y arrotondati non cambiano, il delay successivo conta soltanto i comandi realmente emessi. I delta divisi in più MoveR contano ciascun comando.

La traiettoria, i punti dei colpi, la sensibilità, lo smoothing moderato e il rilascio pianificato restano invariati. L'importazione legge la convenzione dal Comment dell'AMC, così una successiva conversione non applica due volte la correzione. Gli AMC precedenti privi del nuovo campo conservano la convenzione delay-only.

I report AMC esistenti mantengono `CommandTimingMeasured=false`; la calibrazione Razer e la verifica M249 fornite dall'utente vengono documentate nel report XML separato. Il modello senza sparare rimane sperimentale: legge VData reali ma ricostruisce impulsi e angoli. La verifica M249 della conversione tra dispositivi non prova la correttezza di tutte le nuove traiettorie generate. Il codice della generazione e i metadati AMC preesistenti non sono stati modificati.

I controlli includono uno stream AMC golden indipendente, 300 comandi senza deriva del clock, delta divisi, conversione sensibilità, reimportazione e preservazione dei punti simulati/registrati. EXE e sorgenti completi sono pubblicati in `Downloads/V0.3.3` dopo la compilazione e i controlli Windows.

La schermata principale non avvia piu' una registrazione: **ESTRAI + AMC / F8** legge i parametri VData dell'arma attiva e crea una macro di prova senza sparare. Il recorder precedente rimane separato in **RECORDER / TEST**, solo per confronto e diagnostica.

## Prova rapida V0.3.3

1. Estrai tutto `CS2_Recoil_Reader_Recorder_V0.3.3_FULL.zip` e apri `CS2_Recoil_Pattern_Reader_And_Recorder_V0.3.3.exe`.
2. Avvia CS2 con `-insecure`, in una mappa offline ospitata nello stesso processo. Per la prima prova usa AK47.
3. Ricarica completamente, togli zoom/burst, lascia il sinistro rilasciato e aspetta il reset del recoil.
4. Se desideri l'XML, seleziona **Esporta anche XML Razer · Synapse 4**, poi premi **F8 senza sparare**. In `Estrazioni/AMC` trovi `.amc`, `.recoil.json` e `.report.json`; l'XML opzionale e il suo report sono in `Estrazioni/XML`.
5. Importa l'AMC in Bloody nella modalita' finche' tieni premuto. La macro e' sperimentale: questo passaggio e' una prova reale, non una verifica gia' eseguita.
6. Se l'estrazione fallisce, premi REPORT e conserva `Diagnostica_CS2.txt`. Se il movimento non corrisponde, conserva i due JSON insieme all'AMC: contengono i valori usati, non occorre ricreare lo spray per rigenerare il file.

CONVERTER legge anche il nuovo `.recoil.json`: funziona senza CS2 aperto, rigenera la macro e permette una sensibilita' diversa senza alterare i tempi. Non e' un decrittatore MGN.

## Cosa viene estratto e cosa no

Sono letti arma/ID, sensibilita' base, capacita' caricatore, full-auto, modalita', numero di proiettili, cycle time, recoil seed, angolo/varianza e magnitudine/varianza. Il puntatore VData `weapon+0x388` e' un candidato non nominato nello schema: viene accettato soltanto se il nome coincide con l'entita' attiva, i campi sono plausibili e due letture stabili concordano. Se il layout non corrisponde, il programma si ferma; non sostituisce i parametri dell'arma con una tabella inventata.

**Non viene estratta una traiettoria nativa dei proiettili.** Gli impulsi e il decadimento vengono ricostruiti con un modello legacy non ancora verificato nel motore CS2 corrente. RNG Park-Miller/shuffle, tabella di 64 impulsi, smoothing della varianza e soppressione iniziale sono ipotesi esplicite. I valori del modello si trovano in `Model`; quelli letti in `Native`. Non vengono letti automaticamente m_pitch/m_yaw, recoil scale o costanti di decadimento: i default sperimentali sono 0.022/0.022, 2.0, 8/18/4.5, soppressione 4 colpi a 0.75 e varianza 0.55. Neanche la latenza tra pressione e primo colpo viene misurata: il modello assume primo colpo immediato. Non sono compensate dispersione casuale, movimento del giocatore o shake visuale.

L'output conserva i punti **simulati** dei colpi e arrotonda la posizione cumulativa per evitare deriva. I MoveR sono interpolati in passaggi di circa 10 ms, non a 1 ms. La macro include LeftDown, LeftUp finale e al KeyUp, quindi 30000 ms di anti-repeat. La pausa non impedisce una nuova pressione. Il rilascio e' stimato dal ciclo VData e dal caricatore, non registrato. Questi accorgimenti non provano che i colpi siano centrati.

La modalita' senza sparare supporta solo full-auto, modalita' primaria senza zoom/burst, caricatore pieno e recoil azzerato. Per altri casi si ferma. Restano i controlli build 14189, `-insecure`, IsValveDS e presenza del modulo server locale; quest'ultimo non dimostra da solo che nessun client remoto sia connesso: usa una mappa offline. Il processo e' esterno e in sola lettura; nessuna DLL, hook, scrittura nella memoria del gioco o simulazione di input. Bloody esegue l'AMC, non questo programma.

## Verifiche e distribuzione V0.3.3

`build.ps1` compila l'EXE x64 con .NET Framework su Windows. GitHub Actions esegue i controlli esistenti e quelli nuovi su lettura VData nel solo processo di test, errori di puntatore/nome/flag, RNG, primo impulso confrontato con la fixture AK, ripetibilita', JSON, tempi, sensibilita', punti simulati e anteprime. **CS2 e Bloody non vengono avviati in CI.** Il primo impulso coincidente non dimostra che l'intero algoritmo corrente sia corretto.

Il pacchetto contiene EXE, sorgenti completi, workflow, verifiche, anteprime e SHA256. Gli esempi sono separati nelle cartelle `AMC` e `XML`. `AMC/Esempio_NoFire_SINTETICO` contiene parametri AK costruiti dal test, NON estratti da una sessione live. `XML/M249_GOLDEN_EXPORT.xml` e' il risultato del test golden ricostruito, non un'estrazione live. I download della V0.3.3 vengono pubblicati in `Downloads/V0.3.3` solo dopo i controlli.

Fonti tecniche: [schema della build fissata](https://github.com/a2x/cs2-dumper/tree/4116de000e085d62bbd42334c67b35bda37bda4f/output), [implementazione legacy della tabella](https://github.com/SwagSoftware/Kisak-Strike/blob/master/game/shared/cstrike15/cs_weapon_parse.cpp) e [stream legacy](https://github.com/SwagSoftware/Kisak-Strike/blob/master/vstdlib/random.cpp). Le fonti legacy non sono una prova dell'implementazione attuale di CS2.

---

## Aggiornamento di compatibilita' 14189

Il report del 7 ottobre rilevava la build 14189 e il blocco previsto della V0.3.1 (layout 14188). Il layout ora usa il dump a2x del 6 ottobre 2026, revisione `4116de000e085d62bbd42334c67b35bda37bda4f`, che dichiara `build_number: 14189`.

Aggiornati i cinque indirizzi globali usati da client.dll: sensibilita', pawn, controller, angoli e regole. I campi schema usati da arma, VData e recoil risultano invariati. Restano le verifiche di build, sessione, puntatori e dati plausibili; il puntatore VData non-schema continua a richiedere la validazione live esistente.

`AmcConverter.cs`, `AmcInput.cs`, `RecoilDynamics.cs` e `NoFireGenerator.cs` restano identici alla V0.3.1 e alla V0.3.2. La correzione dei delay di 1 ms/MoveR e il modello non vengono modificati. Il campo Version degli snapshot resta 0.3.1 come versione del generatore invariato, mentre interfaccia e assembly sono V0.3.3. Il nuovo exporter XML e' separato; il layout resta quello della V0.3.2. La lettura della build 14189 richiede una prova live dell'utente.

I test controllano ogni costante schema/globale del layout contro un manifest fissato al dump 14189. Le costanti non-schema vengono conservate con i controlli runtime esistenti. Gli input diagnostici storici 14188 restano tali: non vengono rinominati come registrazioni 14189.

## Documentazione della modalita' recorder precedente (V0.2.5)

Windows x64. F8 arma il lettore integrato; il sinistro delimita lo spray. Il programma legge automaticamente arma, sensibilità e stato interno del recoil dal gioco, salva CSV/JSON diagnostici e crea subito un AMC di prova.

## Lettura diretta dello stato recoil — V0.2.5

La V0.2.5 elimina l'interpolazione lineare della registrazione. Legge per ogni aggiornamento `m_predictableBaseAngle`, `m_predictableBaseAngleVel`, tick e frazione del tick. Il convertitore adatta automaticamente al singolo spray le tre componenti della dinamica (decadimento angolare esponenziale, decadimento lineare e decadimento della velocità), la integra a passi interni di 1/128 di secondo e produce comandi moderati di circa 10 ms. Ogni punto registrato al momento del colpo resta esatto; una piccola correzione continua impedisce qualsiasi deriva al punto successivo.

Vengono registrati anche `v_angle`, view punch della telecamera e recoil index dell'arma per la diagnostica. Il programma rimane un processo esterno in sola lettura: non carica DLL, non crea hook e non scrive in CS2.

Questa è una lettura diretta dello **stato di recoil deterministico**, non della traiettoria server completa. La dispersione casuale e l'impatto server non sono presenti nella memoria letta e non vengono dichiarati verificati al 100%.

## Prova della macro senza video — V0.2.5

Premi **TEST AMC...** nel recorder e seleziona lo stesso AMC che esegui in Bloody. Poi torna a CS2, equipaggia l'arma e usa la sensibilità indicate, premi F8 e fai uno spray completo con la macro attiva, tenendo il mouse fisicamente fermo. Rilascia alla fine. La prova salva CSV/JSON e un **.execution.json** nella cartella Registrazioni. Per tornare alla registrazione normale usa **ESCI DAL TEST**.

La prova usa prima `C_BasePlayerPawn.v_angle`, il campo input disponibile nella build fissata; `m_angEyeAngles` rimane soltanto come fallback per i vecchi file. Questo sostituisce la lettura che nel test `fdece407` era rimasta quasi immobile. Il report stima ritardo, fattore di durata, guadagno orizzontale/verticale e scarto. Assume m_pitch=m_yaw=0.022; include gli effetti dell'elaborazione/lettura del gioco e non è una misura diretta del firmware Bloody o della direzione dei proiettili.

Durante il test l'esportazione automatica è disabilitata. Il converter rifiuta i file marcati come prove AMC: il movimento della macro non diventa un nuovo pattern di riferimento. Tutti i dati grezzi rimangono salvati. Il report non applica automaticamente correzioni non verificate.

V0.2.5 conserva il tempo del campione di rilascio. Nei file vecchi senza rilascio osservato rimane la stima precedente; il report indica il metodo. Lo smoothing moderato di circa 10 ms conserva gli anchor dei colpi e la geometria cumulativa.

[Analisi della registrazione AK47 fornita](ANALISI_AK47.md): 1658 campioni, 30 colpi e sensibilità 1.25 stabile; non è disponibile una misura dell'AMC in esecuzione.

## Uso

1. Estrai il pacchetto completo e apri CS2_Recoil_Pattern_Reader_And_Recorder_V0.2.5.exe.
2. Avvia CS2 con -insecure in una mappa di pratica locale.
3. Arma e sensibilità compaiono automaticamente. F8, torna al gioco, attendi un secondo con il sinistro rilasciato e spara da recoil azzerato, senza muovere il mouse.
4. Rilascia il sinistro. Con "Genera AMC automaticamente" attivo trovi la macro nella sottocartella Registrazioni/AMC.
5. CSV e JSON rimangono nella cartella Registrazioni. L'AMC ha un report associato.

CONVERTER apre una singola registrazione ZIP, il CSV/JSON associato oppure un AMC CS2 già esportato dal recorder; supporta trascinamento. Non richiede che CS2 sia aperto. I file V0.1 rimangono leggibili ma il loro nome arma e la sensibilità erano annotazioni manuali: il convertitore lo segnala.

La sensibilità destinazione usa per default il valore registrato. Per cambiarla, togli "Usa la sensibilità della registrazione" e scrivi, per esempio, 1.250: punto e tre decimali. Gli angoli sono convertiti nella scala della sensibilità destinazione; non vengono modificati i tempi.

## AMC

La conversione usa angolo, velocità angolare e tempi nativi registrati. La dinamica viene adattata all'intero spray e integrata internamente a 1/128 di secondo; l'AMC viene quantizzato soltanto dopo, in passaggi moderati di circa 10 ms. I punti ai colpi mantengono tempo e posizione cumulativa esatti. L'arrotondamento è applicato alla posizione cumulativa per non accumulare errori; i passaggi senza spostamento vengono omessi.

Per rendere più fluido un AMC già creato: CONVERTER → APRI FILE → seleziona l'AMC → lascia attiva la sensibilità originale → CONVERTI IN AMC. Salva con un nome nuovo. Il convertitore mantiene ogni punto originale, il primo movimento al suo tempo originale, il rilascio e la pausa finale. Suddivide i salti successivi senza attraversare un cambio di direzione o attenuare l'ampiezza. Con la stessa sensibilità ogni punto originale rimane identico.

L'import AMC legge la sensibilità dall'intestazione ARMA · SENS 1.250. Non può rileggerla dal gioco né ricavare il numero di colpi: non dichiara queste informazioni come verificate automaticamente. Accetta solo macro CS2 con MoveR, Delay, LeftDown/LeftUp e pausa finale 30000 ms; altri comandi o dati incompleti producono un errore.

La macro contiene LeftDown all'inizio, LeftUp a fine sequenza e nel gestore di rilascio, quindi una pausa finale separata di 30000 ms. Importala in Bloody nella modalità "finché tieni premuto". La pausa ritarda la ripetizione della stessa attivazione; non è un blocco globale su una nuova pressione.

Il contatore tiene il massimo raggiunto: un rollback 29→28→29 non diventa un colpo numero 31. La conversione deduplica i tick del recoil e rifiuta colpi mancanti, cambi d'arma/sensibilità, zoom, aim punch esterno e movimento della visuale.

## Limiti da conoscere

- Lo stato interno determina una ricostruzione molto più precisa della curva fra i colpi, ma la funzione nativa di CS2 non viene chiamata dall'esterno. Il report conserva residuo e parametri adattati; l'AMC resta da verificare nel gioco.
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

Il pacchetto include anche gli esempi precedenti. La cartella AMC/AK47 contiene la versione moderata dell'AMC importato: ogni punto originale rimane identico. Non confonderla con l'output della nuova modalita' senza sparare.

CS2 non viene eseguito in GitHub Actions. Le nuove letture automatiche dell'arma/sensibilità e la precisione della compensazione richiedono una prova reale.

Fonte dei campi: a2x/cs2-dumper, snapshot 2d204b1400eb08accfb4098ad954602448dacb07, MIT (licenza inclusa). La struttura dell'handle mantiene l'indice e il seriale; i controlli di identità impediscono l'uso di riferimenti scaduti.


## Diagnostica avvio V0.2.2

La lettura di arma e sensibilità usa una sessione senza richiedere i servizi del recoil. Questi ultimi rimangono obbligatori per registrare: non vengono sostituiti con valori inventati.

Gli errori di puntatore indicano ora il campo: giocatore/controller locale, regole della sessione, client della sessione, servizi delle armi, sensibilità, catena delle identità, identità/nome dell'arma e servizi del recoil. Il messaggio della V0.2 "entra prima in una mappa" veniva usato anche per i campi delle nuove funzioni e non consentiva di individuare il problema.

Il pulsante REPORT apre Diagnostica_CS2.txt, salvato vicino all'EXE dopo un errore. Se quella cartella non è scrivibile, viene usata la cartella locale dell'applicazione. Il report contiene il campo fallito, l'indirizzo/valore letti, i puntatori precedenti, la build, le versioni DLL e lo stack dell'errore. Non trasmette il file e non salva password, token o contenuti arbitrari della memoria.

Il report reale ha identificato il blocco: il globale dwEntityList del dump, client.dll + 0x2715828, restituisce 0x1 nella sessione 14188. V0.2.2 elimina questa lettura dal riconoscimento dell'arma.

Il nuovo percorso parte da m_pEntity del pawn locale e percorre m_pNext/m_pPrev delle identità, campi del medesimo dump. Cerca il riferimento completo dell'arma attiva (indice e seriale), verifica il collegamento identità/entità e poi legge l'ID dell'oggetto e il nome designer. Valida anche il riferimento mantenuto in cache; interrompe la lettura se viene riciclato.

La ricerca ha un limite di 32768 identità/2 secondi e riconosce i cicli. I collegamenti devono essere coerenti. Non scansiona regioni arbitrarie della memoria e non prova offset a caso. Durante lo spray viene usata l'arma già risolta, con verifica del suo riferimento.

Una regressione in memoria del solo processo di test riproduce il globale 0x1 e controlla identificazione, seriali diversi, ricerca nelle due direzioni, cache scaduta, collegamenti incoerenti e cicli. La correzione del blocco è verificata sul caso riprodotto. L'utente ha confermato il riconoscimento dell'AK47 e la registrazione con V0.2.2; la nuova dinamica V0.2.5 richiede la prova reale in gioco.

Campi usati: output/client_dll.json alla revisione 2d204b1400eb08accfb4098ad954602448dacb07. m_pEntity=0x10, m_pPrev=0x50, m_pNext=0x58 e m_flags=0x30. Il report utente non è incluso nel repository; la regressione usa esclusivamente indirizzi e dati del processo di test.
