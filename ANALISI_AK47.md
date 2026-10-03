# Analisi della registrazione AK47 e correzione V0.2.5

L'analisi riguarda CSV, JSON, AMC e report della registrazione fornita il 3 ottobre 2026. La registrazione non contiene una prova dell'AMC in esecuzione: gli angoli della visuale restano praticamente fermi.

| Controllo | Risultato nei file |
|---|---:|
| Campioni CSV | 1658 |
| Colpi distinti | 30 |
| Sensibilità registrata | 1.25, invariata |
| Arma | AK47, ID 7, handle invariato |
| Aggiornamenti di colpo mancanti | 0 |
| Intervallo mediano di osservazione | 2.0007 ms |
| Intervallo massimo di osservazione | 5.34925 ms |
| Scarto massimo osservazione/tick del recoil | 2.703426 ms |
| Stati distinti di angolo/velocità di base | 30 |
| MoveR nell'AMC | 288 |
| Spostamento cumulativo nell'AMC | X -130, Y +363 |
| Rilascio del click registrato | 3282.965 ms |
| Rilascio del click nell'AMC V0.2.3 | 3002 ms |

La conversione riproduce la scala assunta e gli angoli di base ai tempi dei colpi, con arrotondamento cumulativo. Questo verifica l'esportazione matematica; non dimostra che la compensazione coincida con la direzione reale dei proiettili.

Il lettore acquisisce m_predictableBaseTick, m_predictableBaseTickInterpAmount, m_predictableBaseAngle e m_predictableBaseAngleVel da CCSPlayer_AimPunchServices. Nei 1658 campioni angolo e velocità di base cambiano soltanto ai 30 aggiornamenti dei colpi e restano fermi fino al colpo successivo. Anche dopo l'ultimo colpo restano invariati fino al rilascio. Aumentare il polling non aggiunge punti alla curva effettiva del recoil.

V0.2.3 usa gli angoli come punti e interpola linearmente; la velocità serve a scegliere lo stato più frequente, ma non è integrata. Le parti intermedie restano stime. Nel JSON fornito "instantaneous_bullet_recoil_reconstruction_verified" è false.

m_pitch/m_yaw=0.022, recoil scale=2 e tick a 64Hz sono assunti dal convertitore. I file non contengono le impostazioni effettive di pitch/yaw o una misura della riproduzione temporale di Bloody. Le colonne view_angle sono zero e pawn_mouse_sensitivity è zero: non possono essere usate come conferma aggiuntiva della scala. La sensibilità base di 1.25 è stata letta dal percorso dedicato.

## Cosa è stato corretto in V0.2.4

Quando il CSV include il campione di rilascio, la conversione conserva quel tempo anziché inventare la fine come "ultimo colpo + ciclo mediano". Per questa registrazione la fine diventa 3283 ms. Gli spostamenti, la suddivisione moderata e la pausa di 30000 ms restano uguali. Questo corregge la durata del click, ma non dimostra di risolvere il raggruppamento dei colpi.

La modalità TEST AMC registra una prova della macro eseguita da Bloody. Usa le variazioni di m_angEyeAngles dal campione precedente al click per confrontare il movimento ricevuto dal gioco con i punti dell'AMC selezionato. Stima ritardo, fattore di durata, guadagno dei due assi e residuo. Assume pitch/yaw 0.022; non legge direttamente i pacchetti HID e non misura direttamente la direzione dei proiettili. Il ritardo include anche elaborazione e osservazione del gioco.

Il test mantiene CSV/JSON completi e produce un file .execution.json con la timeline attesa e la traccia di confronto. Non converte una prova di macro in un nuovo pattern recoil. Non modifica automaticamente velocità o ampiezza sulla base di una stima non verificata.

Per usare il test aggiornato: apri V0.2.5, premi TEST AMC..., seleziona esattamente l'AMC importato in Bloody, equipaggia AK47 a 1.250 nella pratica locale, premi F8 nel gioco e poi esegui lo spray completo con la macro attiva e il mouse fisicamente fermo. Rilascia il pulsante alla fine. Il file .execution.json è la nuova evidenza necessaria per verificare la riproduzione.

## Correzione V0.2.5: stato interno e test `fdece407`

Il test `fdece407` ha registrato 30 colpi ma `m_angEyeAngles` è rimasto praticamente immobile; per questo non è stato possibile creare `.execution.json`. V0.2.5 registra anche `C_BasePlayerPawn.v_angle` e lo usa come prima sorgente del confronto AMC, mantenendo `m_angEyeAngles` soltanto come fallback per file precedenti.

Per la generazione AMC, V0.2.5 usa sia l'angolo sia la velocità di `CCSPlayer_AimPunchServices`. Adatta allo spray il decadimento angolare esponenziale, il decadimento lineare e il decadimento della velocità, integra lo stato a 1/128 di secondo e forza una correzione continua molto piccola affinché ogni anchor misurato resti esatto. Il residuo e i tre parametri sono salvati nel report; un fit fuori limite o impreciso blocca l'AMC invece di inventare la curva.

Questo sostituisce la linea retta di V0.2.3, ma non trasforma una lettura client in una traiettoria server completa: `ballistic_trajectory_directly_read` e `server_spread_included` rimangono false.

## Fonti dei campi e distinzione fra visuale e proiettili

- [Layout utilizzato dal recorder, revisione fissata](https://github.com/a2x/cs2-dumper/blob/2d204b1400eb08accfb4098ad954602448dacb07/output/client_dll.json).
- [Schema CCSPlayer_AimPunchServices](https://s2v.app/SchemaExplorer/cs2/client/CCSPlayer_AimPunchServices).
- [Aggiornamento ufficiale CS2 del 21 aprile 2026](https://store.steampowered.com/news/posts/?appids=730&enddate=1776897650&feed=steam_community_announcements): il comportamento della visuale e le traiettorie dei proiettili sono trattati separatamente.

La nuova dinamica è verificata su stati sintetici con parametri noti e sulla fixture AK47, mantenendo esatti i punti dei colpi. Non viene dichiarata una lettura diretta dell'impatto server; GitHub Actions non esegue CS2.
