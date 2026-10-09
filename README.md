# RecoilLabs / CS2 Recoil Reader & Recorder V0.3.8 — compatibilita' CS2 14190

La V0.3.8 aggiorna il layout alla build **14190**. Sensibilita', pawn, controller e view angles hanno nuovi indirizzi; il campo eye angles usato dal recorder passa a `0x3600`. Le regole della sessione sono risolte tramite l'entita' client con nome esatto `cs_gamerules` e il suo campo schema `m_pGameRules`, validando identita' e puntatore prima di ogni controllo della sessione. I due dump disponibili discordano sul globale `dwGameRules`, che non viene usato. I parametri delle armi, il selettore, la generazione AMC e la calibrazione XML approvati della V0.3.7 restano invariati.

Restano i fix della V0.3.7: il programma distingue **M4A1-S con silenziatore** e **M4A1-S senza silenziatore** nella schermata principale e nel converter JSON. Legge `m_bSilencerOn` dallo schema della stessa build; il rilevamento aggiorna il flag anche quando il riferimento dell'arma e' gia' in cache. Nell'estrazione il flag deve concordare con la modalita': **0 senza silenziatore, 1 con silenziatore**. Se i due stati non concordano, bisogna attendere la fine dell'animazione e ripetere F8.

I due profili leggono i rispettivi parametri reali dalla stessa VData `weapon_m4a1_silencer`, anche quando il silenziatore e' rimosso. Nei file di riferimento i recoil hanno magnitudine/varianza 25/3 alla modalita' 0 e 21/0 alla modalita' 1; il programma legge i valori in memoria e non li sostituisce con questi numeri. I nomi dei file e la descrizione AMC includono `M4A1_S_SENZA_SILENZIATORE` oppure `M4A1_S_CON_SILENZIATORE`. L'identita' JSON resta `Weapon=M4A1_S`, ID 60, con Mode originale e flag nullable `SilencerOn`. I JSON precedenti senza il nuovo flag rimangono leggibili e usano la modalita' gia' registrata per il nome del profilo.

**Fix MP5-SD:** il report utente indica entita' `weapon_mp7`, VData `weapon_mp5sd`, **ID 23**. Questa coppia ora e' accettata soltanto per l'ID 23 e richiede i VData specifici MP5-SD. MP7 ID 33, altre entita' o VData MP7 per ID 23 restano rifiutati. Entrambi i percorsi di export AMC/XML ricevono i parametri MP5-SD effettivi. Le varianti M4A1-S rimangono vincolate all'ID 60.

Ciclo, angolo, varianza angolo, magnitudine e varianza magnitudine sono campi `CFiringModeFloat` con due float32: ora vengono letti all'indice della modalita' corrente, anziche' sempre all'indice 0. Il codice legge i parametri in memoria; i dati di riferimento non diventano una tabella sostitutiva. Full-auto, singolo proiettile, zoom/burst, plausibilita' e doppia lettura stabile restano verificati. Altre armi in modalita' 1 e indici fuori 0/1 vengono rifiutati.

La diagnostica indica separatamente ID non supportato, FullAuto=false, numero di proiettili, modalita' incompatibile o stato silenziatore non coerente e conserva i parametri nella sezione **PARAMETRI ARMA LETTI**, incluso il nuovo flag. La lettura live della nuova build resta da provare sul PC dell'utente.

Restano la scelta esplicita **Formato: AMC / XML Razer** della V0.3.4, RNG, modello e costruzione dei comandi AMC invariati. Rimangono i nomi distinti dei profili M4A1-S nei file/descrizioni e il flag nei metadati. La calibrazione XML rimane quella M249 verificata in gioco dall'utente sul Naga V3 Pro: stessi X/Y e tempi Delay +1 ms per ciascun MoveR. I fix riguardano il riconoscimento e lo stato prima dell'esportazione, quindi si applicano a entrambi i formati.

Seleziona **XML Razer** per salvare soltanto `.xml` e report nella sottocartella **XML**; seleziona **AMC** per salvare soltanto `.amc` e report nella sottocartella **AMC**. Le due cartelle restano separate. I parametri estratti `.recoil.json` vengono conservati accanto al formato selezionato anche se la successiva conversione fallisce. Il pulsante principale mostra **ESTRAI XML · F8** o **ESTRAI AMC · F8**. La scelta viene ricordata in `%LOCALAPPDATA%/RecoilLabs/ExportFormat.txt` e ripresa quando si apre uno strumento o si riavvia il programma. Un errore XML viene mostrato come errore dell'esportazione selezionata e registrato nella diagnostica; non viene sostituito da un messaggio di successo AMC.

Per convertire un AMC esistente, aprilo nel converter, scegli **XML Razer** e premi **ESPORTA XML RAZER**. Questo percorso conserva tutti i comandi originali, anche consecutivi, e disabilita il cambio di sensibilita': non passa attraverso lo smoothing. Scegliendo **AMC**, **CONVERTI IN AMC** mantiene il converter precedente. Con parametri/registrazioni, l'XML viene derivato da un AMC temporaneo prodotto dal generatore invariato, poi rimosso: non viene eseguita una seconda ricostruzione della traiettoria e non serve un AMC visibile nella cartella di output. Un eventuale cambio di sensibilita' sul JSON/CSV viene applicato dal generatore esistente; l'XML non aggiunge un altro fattore.

## Calibrazione Synapse 4

Ogni Buffer successivo all'origine usa coordinate cumulative; la differenza X/Y riproduce esattamente il relativo MoveR AMC. Il tempo del Buffer e' la somma dei Delay AMC dall'ultimo movimento **+1 ms per quel MoveR**. Anche i MoveR consecutivi ricevono ciascuno 1 ms; non vengono fusi. Si leggono i comandi grezzi, senza riapplicare il costo gia' dichiarato da `MoveRCommandCostMs=1` nelle timeline AMC. Il master non viene corretto o riscritto.

Il formato replica il golden verificato: `mmtSetting=3`, `MouseMoveType=relative`, Start Point (mouse cursor), origine 500/300 a tempo 0, LeftDown prima della traiettoria, LeftUp dopo il delay di rilascio originale. `Number` della traiettoria e' la somma dei tempi Buffer in secondi; i singoli tempi Buffer sono intervalli in millisecondi. La pausa Bloody dopo LeftUp viene omessa dall'XML. Importa in **Synapse 4**, che gestisce la modalita' di attivazione della macro nel proprio binding.

Il golden M249 fornito e' `tests/fixtures/M249_Razer_Synapse4_CAL1_MoveCost1ms.xml`: **765 MoveR, X +25, Y +448, 7155 ms di Delay prima dell'ultimo movimento +765 ms =7920 ms di traiettoria**, poi **79 ms** fino a LeftUp (rilascio a 7999 ms). L'utente ha confermato il test in gioco. I test della nuova implementazione confrontano tutti i Buffer con questo file e verificano anche comandi consecutivi, costo gia' dichiarato, rilascio e assenza di modifiche al master. Il file AMC originale non e' stato materializzabile (403): il test M249 usa una fixture esplicitamente ricostruita dal golden XML, non pretende di confrontare i byte del master originale. Il golden XML resta nei test e non viene incorporato nell'EXE.

Ogni XML ha un `.report.json` nella cartella XML, con SHA256 del master, numero di movimenti, X/Y, somma dei Delay, costo MoveR, tempo della traiettoria, tempo di rilascio e pausa omessa. Se il master e' un AMC esistente vengono conservati anche percorso e `MasterAmcStored=true`; con generazione XML-only il master e' temporaneo, `MasterAmcStored=false` e il percorso e' null. L'export non esegue macro o input del mouse.

## Generazione AMC invariata

La generazione esistente conserva **1 ms di costo per comando MoveR**. Un movimento pianificato ogni 10 ms viene scritto normalmente con Delay 9 ms e un MoveR. Se un movimento viene saltato perché X/Y arrotondati non cambiano, il delay successivo conta soltanto i comandi realmente emessi. I delta divisi in più MoveR contano ciascun comando.

La traiettoria, i punti dei colpi, la sensibilità, lo smoothing moderato e il rilascio pianificato restano invariati. L'importazione legge la convenzione dal Comment dell'AMC, così una successiva conversione non applica due volte la correzione. Gli AMC precedenti privi del nuovo campo conservano la convenzione delay-only.

I report AMC mantengono `CommandTimingMeasured=false`; la calibrazione Razer e la verifica M249 fornite dall'utente vengono documentate nel report XML separato. Il modello senza sparare rimane sperimentale: legge VData reali ma ricostruisce impulsi e angoli. La verifica M249 della conversione tra dispositivi non prova la correttezza di tutte le nuove traiettorie generate. La costruzione dei comandi e i metadati temporali AMC preesistenti non sono stati modificati; il report conserva anche il nuovo stato del silenziatore quando disponibile.

I controlli includono uno stream AMC golden indipendente, 300 comandi senza deriva del clock, delta divisi, conversione sensibilità, reimportazione e preservazione dei punti simulati/registrati. EXE e sorgenti completi sono pubblicati in `Downloads/V0.3.8` dopo la compilazione e i controlli Windows.

La schermata principale non avvia piu' una registrazione: **ESTRAI / F8** legge i parametri VData dell'arma attiva e crea il formato selezionato senza sparare. Il recorder precedente rimane separato in **RECORDER / TEST**, solo per confronto e diagnostica.

## Prova rapida V0.3.8

1. Estrai tutto `CS2_Recoil_Reader_Recorder_V0.3.8_FULL.zip` e apri `CS2_Recoil_Pattern_Reader_And_Recorder_V0.3.8.exe`.
2. Avvia CS2 con `-insecure`, in una mappa offline ospitata nello stesso processo. Per verificare i fix usa M4A1-S oppure MP5-SD.
3. Ricarica completamente, togli zoom/burst, lascia il sinistro rilasciato e aspetta il reset del recoil e la fine dell'eventuale animazione del silenziatore.
4. Per M4A1-S monta oppure rimuovi il silenziatore, attendi la fine dell'animazione e controlla il nome mostrato. Scegli **Formato: AMC** oppure **XML Razer**, poi premi **F8 senza sparare**. Il formato selezionato, `.recoil.json` e `.report.json` si trovano in `Estrazioni/AMC` oppure `Estrazioni/XML`, con il nome del profilo M4A1-S distinto.
5. Importa l'AMC in Bloody nella modalita' finche' tieni premuto. La macro e' sperimentale: questo passaggio e' una prova reale, non una verifica gia' eseguita.
6. Se l'estrazione fallisce, premi REPORT e conserva `Diagnostica_CS2.txt`. Se il movimento non corrisponde, conserva i due JSON insieme all'AMC: contengono i valori usati, non occorre ricreare lo spray per rigenerare il file.

CONVERTER legge anche il nuovo `.recoil.json`: funziona senza CS2 aperto, rigenera la macro e permette una sensibilita' diversa senza alterare i tempi. Non e' un decrittatore MGN.

## Cosa viene estratto e cosa no

Sono letti arma/ID, sensibilita' base, capacita' caricatore, full-auto, modalita', numero di proiettili, cycle time, recoil seed, angolo/varianza, magnitudine/varianza e stato silenziatore per M4A1-S. Il puntatore VData `weapon+0x388` e' un candidato non nominato nello schema: viene accettato soltanto se il nome coincide con l'entita' attiva oppure corrisponde agli alias M4A1-S ID 60 / MP5-SD ID 23 descritti sopra, i campi sono plausibili e due letture stabili concordano. Se il layout non corrisponde, il programma si ferma; non sostituisce i parametri dell'arma con una tabella inventata.

**Non viene estratta una traiettoria nativa dei proiettili.** Gli impulsi e il decadimento vengono ricostruiti con un modello legacy non ancora verificato nel motore CS2 corrente. RNG Park-Miller/shuffle, tabella di 64 impulsi, smoothing della varianza e soppressione iniziale sono ipotesi esplicite. I valori del modello si trovano in `Model`; quelli letti in `Native`. Non vengono letti automaticamente m_pitch/m_yaw, recoil scale o costanti di decadimento: i default sperimentali sono 0.022/0.022, 2.0, 8/18/4.5, soppressione 4 colpi a 0.75 e varianza 0.55. Neanche la latenza tra pressione e primo colpo viene misurata: il modello assume primo colpo immediato. Non sono compensate dispersione casuale, movimento del giocatore o shake visuale.

L'output conserva i punti **simulati** dei colpi e arrotonda la posizione cumulativa per evitare deriva. I MoveR sono interpolati in passaggi di circa 10 ms, non a 1 ms. La macro include LeftDown, LeftUp finale e al KeyUp, quindi 30000 ms di anti-repeat. La pausa non impedisce una nuova pressione. Il rilascio e' stimato dal ciclo VData e dal caricatore, non registrato. Questi accorgimenti non provano che i colpi siano centrati.

La modalita' senza sparare supporta solo full-auto, un proiettile per colpo, modalita' 0 oppure M4A1-S in modalita' 1, senza zoom/burst, caricatore pieno e recoil azzerato. Per altri casi si ferma. Restano i controlli build 14190, `-insecure`, IsValveDS e presenza del modulo server locale; quest'ultimo non dimostra da solo che nessun client remoto sia connesso: usa una mappa offline. Il processo e' esterno e in sola lettura; nessuna DLL, hook, scrittura nella memoria del gioco o simulazione di input. Bloody esegue l'AMC, non questo programma.

## Verifiche e distribuzione V0.3.8

`build.ps1` compila l'EXE x64 con .NET Framework su Windows. GitHub Actions esegue i controlli esistenti, il golden XML e i test del percorso realmente usato da F8: XML-only, AMC-only, JSON nel converter, AMC originale nel converter e registrazione. Le regressioni riproducono l'alias MP5-SD del report e i due stati M4A1-S in memoria di test. Si verificano i parametri distinti, le due traiettorie M4A1-S, i nomi file/descrizioni, il refresh del flag anche con identita' in cache, la reimportazione JSON nuova e precedente e l'estrazione AMC/XML dei tre profili. ID/nome incompatibili, flag invalidi o in transizione, FullAuto=false, numero di proiettili e indici incompatibili restano rifiutati. Si verifica la diagnostica con i valori effettivi e il nuovo campo schema contro il dump fissato. Verifica cleanup del master temporaneo, errori, calibrazione, scelta e pulsante UI. **CS2, Bloody e Synapse non vengono avviati in CI.** La lettura live e l'esecuzione della nuova build rimangono da provare sul PC dell'utente.

Il pacchetto contiene EXE, sorgenti completi, workflow, verifiche, anteprime e SHA256. Gli esempi sono separati nelle cartelle `AMC` e `XML`. `AMC/Esempio_NoFire_SINTETICO` contiene parametri AK costruiti dal test, NON estratti da una sessione live. `XML/M249_GOLDEN_EXPORT.xml` e' il risultato del test golden ricostruito, non un'estrazione live. I download della V0.3.8 vengono pubblicati in `Downloads/V0.3.8` solo dopo i controlli.

Fonti tecniche precedenti: [schema storico 14189](https://github.com/a2x/cs2-dumper/tree/4116de000e085d62bbd42334c67b35bda37bda4f/output), [struttura CFiringModeFloat con due valori](https://github.com/roflmuffin/CounterStrikeSharp/blob/f32e74515b7a10beb06e1e35406ade8a80ceb6d2/managed/CounterStrikeSharp.API/Generated/Schema/Classes/CFiringModeFloat.g.cs), [parametri M4A1-S nei file di gioco](https://github.com/SteamTracking/GameTracking-CS2/blob/ac1278dbbff39b7fe5030fba42a010e455c011f6/game/csgo/pak01_dir/scripts/weapons.vdata), [implementazione legacy della tabella](https://github.com/SwagSoftware/Kisak-Strike/blob/master/game/shared/cstrike15/cs_weapon_parse.cpp) e [stream legacy](https://github.com/SwagSoftware/Kisak-Strike/blob/master/vstdlib/random.cpp). Le fonti legacy non sono una prova dell'implementazione attuale di CS2. Le fonti 14190 sono elencate nella sezione seguente.

---

## Aggiornamento di compatibilita' 14190

Il report del 9 ottobre rileva la build 14190 e il blocco previsto della V0.3.7 (layout 14189). Il nuovo layout e' fissato al [dump sezzyaep/CS2-OFFSETS](https://github.com/sezzyaep/CS2-OFFSETS/tree/fa24ed457f096f519d2cdbb95db55f4b7bc5c6df) del 8 ottobre, revisione `fa24ed457f096f519d2cdbb95db55f4b7bc5c6df`, con `build_number: 14190`. Tutti i campi schema client usati concordano con il [dump indipendente spotted-wtf/CS2-OFFSETS](https://github.com/spotted-wtf/CS2-OFFSETS/tree/35cb4039861708501bd2cf72850036575c56cbd6); concordano anche i globali condivisi. Quest'ultima fonte non pubblica il globale sensibilita'.

I due valori diversi di `dwGameRules` sono documentati come esclusi nel manifest `tests/fixtures/LAYOUT_14190.json`. Il reader trova invece l'unica entita' client `cs_gamerules` nella catena delle identita' del pawn, controlla collegamenti reciproci, nome esatto, riferimento completo e corrispondenza fra entita' e identita', poi legge `C_CSGameRulesProxy::m_pGameRules` a `0x600`. Identita', nome e puntatore vengono riconfermati a ogni `VerifySession`. Ricerca limitata e cicli controllati; zero/duplicati/cambi di regole interrompono la lettura. Non c'e' fallback a un globale non validato. Il [nome client e' confermato dai file di gioco](https://github.com/SteamTracking/GameTracking-CS2/blob/f68ba6aa2473367589e7cb249fc38814c9b90ff0/DumpSource2/entities/client.fgd); il [campo GameRules del proxy](https://github.com/roflmuffin/CounterStrikeSharp/blob/653d651f1ac09ac1ddb423d588f871b891038860/managed/CounterStrikeSharp.API/Generated/Schema/Classes/CCSGameRulesProxy.g.cs) e' esposto anche da CounterStrikeSharp. Restano le verifiche di build, sessione, puntatori e dati plausibili; il puntatore VData non-schema continua a richiedere la validazione live esistente.

`AmcConverter.cs`, `AmcInput.cs`, `RecoilDynamics.cs`, `NoFireGenerator.cs`, `RazerXmlExporter.cs`, `MacroExport.cs`, `GameIdentity.cs`, `WeaponVDataIdentity.cs` e `WeaponVariant.cs` restano identici alla V0.3.7. I fix M4A1-S/MP5-SD e i tempi AMC/XML restano invariati. Il campo Version degli snapshot resta 0.3.1 come versione del modello invariato; interfaccia e assembly sono V0.3.8. Il nuovo layout aggiorna anche `m_angEyeAngles` a `0x3600` per il recorder.

I test confrontano ogni costante schema/globale con il manifest 14190 fissato. Le costanti non-schema vengono conservate con i controlli runtime esistenti. Il manifest 14189 e gli input diagnostici storici restano conservati con la loro build originale. La nuova ricerca delle regole viene provata in entrambe le direzioni, con nomi esatti, riferimenti riciclati, puntatori nulli/cambiati, duplicati, link corrotti e cicli. Il percorso reale `Game.VerifySession` viene provato con memoria allocata nel processo di test e continua a rifiutare ValveDS, mappa non pronta e regole non validate. La lettura della nuova sessione CS2 richiede una prova sul PC dell'utente; la CI usa soltanto memoria del proprio processo.

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
