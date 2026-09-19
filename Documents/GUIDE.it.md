# Neko Dynamic Collision (NDC) — distribuzione e manuale

Collisione a involucro convesso pre-cotta per Unity. Tutto il lavoro costoso avviene
nell'Editor; il runtime si limita a caricare e instradare. Un personaggio skinnato o una
mesh statica diventa un insieme di involucri convessi per osso e per regione con
**costo nullo per frame**, eventi di partizione integrati e materiali fisici per regione dipinti con un pennello.

Il componente stesso non contiene codice generato, né attributi, né dipendenze a runtime
dalla metà editor del plugin. Elimina `Editor/` e il runtime continua a funzionare.

## Contenuti

- [Parte A — Distribuzione rapida](#sec-partA)
  - [1. Cotta il tuo primo personaggio](#sec-1)
  - [2. Dipingi le regioni di materiale](#sec-2)
  - [3. Il percorso di 10 minuti](#sec-3)
- [Parte B — Manuale](#sec-partB)
  - [4. Concetti fondamentali](#sec-4)
  - [5. Installazione e requisiti](#sec-5)
  - [6. La finestra del forno (baker)](#sec-6)
  - [7. Riferimento del menu](#sec-7)
  - [8. Precisione](#sec-8)
  - [9. Il pennello](#sec-9)
  - [10. Gruppi di materiali e preset](#sec-10)
  - [11. Diagnostica della copertura](#sec-11)
  - [12. Salute delle collisioni](#sec-12)
  - [13. Eventi e integrazione](#sec-13)
  - [14. Polling dei Trigger](#sec-14)
  - [15. Massa, autocollisione e LOD](#sec-15)
  - [16. Localizzazione](#sec-16)
  - [17. Struttura delle directory](#sec-17)
  - [18. Disinstallazione](#sec-18)
  - [19. Risoluzione dei problemi e FAQ](#sec-19)
  - [20. Contatti](#sec-20)
- [Appendice A. Preset dei materiali fisici](#sec-appA)
- [Appendice B. Controlli di fisica del progetto](#sec-appB)

---

<a id="sec-partA"></a>
# Parte A — Distribuzione rapida

<a id="sec-1"></a>
## 1. Cotta il tuo primo personaggio

1. Seleziona il tuo personaggio e aggiungi il componente **Dynamic Collision**
   (`Add Component → Neko → Dynamic Collision`, oppure il menu `GameObject`).
   Alla creazione il componente trova il proprio `SkinnedMeshRenderer` e crea un
   gruppo di materiali predefinito. Non devi compilare nulla.
2. Premi **Bake** nell'inspector.
3. Gli involucri compaiono nella Scene view come wireframe in posa di bind. Seleziona il personaggio per
   vederli; il Gizmo segue la tua selezione per impostazione predefinita.

La cottura scrive tre tipi di asset accanto alla scena:

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

Le mesh degli involucri sono sotto-asset di `_Baked.asset`, quindi viaggiano con esso e
sopravvivono alla modalità Play e alle build. Nulla viene ricalcolato a runtime.

<a id="sec-2"></a>
## 2. Dipingi le regioni di materiale

Il pennello è ciò che rende possibile "un oggetto, più materiali fisici".

1. Apri il forno (`NekoWorks → NekoDynamicCollision → Open Main Window`).
2. Vai alla scheda **Materials** e aggiungi un gruppo per regione — per esempio
   `Hard` e `Soft`. Scegli un preset per ciascuno.
3. Vai alla scheda **Paint**, scegli il gruppo che vuoi dipingere, premi **Start painting**.
4. Nella Scene view, trascina sulle facce che vuoi in quel gruppo. I triangoli
   dipinti vengono riempiti immediatamente con il colore del gruppo.
5. Esegui di nuovo la cottura. Ogni regione ora ottiene i propri involucri convessi con il proprio materiale fisico.

Puoi dipingere solo mentre la modalità Play è ferma e la finestra Animation non è
in anteprima. La posa in sé non conta — le etichette sono memorizzate per indice di triangolo,
quindi la posa corrente è irrilevante.

<a id="sec-3"></a>
## 3. Il percorso di 10 minuti

| Minuto | Fai questo |
|---|---|
| 0–2 | Aggiungi il componente, premi Bake, guarda il Gizmo. |
| 2–4 | Apri la scheda **Health**, sistema tutto ciò che è rosso. |
| 4–6 | Apri la scheda **Bake**, guarda il numero di copertura. Sotto ~95%, alza la precisione. |
| 6–9 | Aggiungi un gruppo di materiali per regione, dipingilo, esegui di nuovo la cottura. |
| 9–10 | Imposta il layer di interazione su `Bullet`, collega un nome di evento, prova in modalità Play. |

---

<a id="sec-partB"></a>
# Parte B — Manuale

<a id="sec-4"></a>
## 4. Concetti fondamentali

### Involucri, non zuppa di triangoli

Un `Rigidbody` dinamico (non cinematico) non può usare un `MeshCollider` non convesso — questa
è una restrizione di PhysX, non di Unity. Quindi ogni forma di collisione per un corpo in movimento
deve essere convessa. NDC cotta **involucri convessi** e fornisce a Unity l'involucro stesso
anziché il sottoinsieme grezzo di triangoli, ed è per questo che il conteggio dei vertici non può mai raggiungere
il limite di PhysX di 255.

### Perché gli involucri rigidi per osso sono sufficienti

In posa di bind, `bone.localToWorldMatrix · bindposes[i] = I`. Lo skinning di un vertice
pesato al 100% su un osso restituisce quindi esattamente la mesh in posa di bind. In
altre parole: **un involucro cotto nello spazio locale dell'osso è bit per bit ciò che produrrebbe
una ri-cottura per frame** per i vertici con peso rigido.

Solo i vertici con peso misto — quelli che attraversano una giunzione — differiscono. Questi sono coperti
dagli involucri vicini, che si sovrappongono per costruzione. Ecco perché NDC può essere gratuito a
runtime e comunque accurato dove conta.

### Clustering spaziale, non suddivisione in ordine di indice

NDC raggruppa i triangoli per posizione (seeding del punto più lontano più crescita in stile Dijkstra
sull'adiacenza degli spigoli). L'alternativa — prendere i triangoli in ordine di indice — produce
involucri che si sovrappongono tra loro e avvolgono l'aria, e peggiorano quanti più involucri chiedi.

### Partizioni e gruppi di materiali

Due assi indipendenti:

- **Partizione** — *dove*. Un osso (modalità Skin) o l'intera mesh (modalità Mesh).
  Una partizione figlia batte sempre un antenato, quindi gli eventi non vengono mai inviati due volte.
- **Gruppo di materiali** — *cosa*. Un insieme di facce che condividono un materiale fisico e una
  densità, creato dipingendo.

Un involucro è l'intersezione di una partizione e un gruppo di materiali. Se un gruppo non ha
facce dipinte dentro un dato osso, non viene prodotto alcun involucro per quella coppia.

### Cosa fa il runtime

1. Carica l'insieme cotto.
2. Crea un oggetto figlio nascosto per ogni involucro sotto l'osso corretto, con un transform
   identità, e assegna un `MeshCollider` convesso.
3. Costruisce una tabella di ricerca `Collider → hull`.
4. Colloca un `Dyc_Relay` su ogni `Rigidbody` che possiede un involucro.
5. Configura layer, autocollisione e massa.

Poi si ferma. Non c'è lavoro in `Update` oltre a un polling opzionale dei trigger e a un
controllo di distanza per il LOD.

<a id="sec-5"></a>
## 5. Installazione e requisiti

- Unity 2022.3 o superiore.
- Copia `Assets/NekoDynamicCollision` nel tuo progetto. Non c'è nulla da
  configurare; gli assembly sono delimitati da definizioni di assembly.
- Due assembly:
  - `Neko.DynamicCollision.Runtime` — il componente, il relay, il polling dei trigger, le struct
    di evento e la facciata di integrazione. Non referenzia mai `UnityEditor`.
  - `Neko.DynamicCollision.Editor` — il forno (baker), la matematica degli involucri, il clustering, il pennello,
    il Gizmo, il controllo di salute, i preset e la finestra. Piattaforma solo-Editor.

<a id="sec-6"></a>
## 6. La finestra del forno (baker)

`NekoWorks → NekoDynamicCollision → Open Main Window` (`Cmd/Ctrl+Shift+D`).

| Scheda | Cosa fa |
|---|---|
| **Bake** | Sorgente, modalità, precisione, bake/clear/rebuild, statistiche, copertura |
| **Paint** | Elenco dei gruppi con conteggi dei triangoli dipinti, impostazioni del pennello, ripristino della posa |
| **Gizmo** | Cosa disegna la Scene view e come |
| **Parts** | L'elenco delle partizioni — osso, includi figli, nome dell'evento, moltiplicatore di danno |
| **Materials** | Gruppi di materiali, preset, tabella di comportamento delle coppie, audit di fisica del progetto |
| **Health** | Punteggio su 100, ogni problema, correzioni con un clic |
| **Settings** | Lingua, diagnostica, apri la cartella dei baked, ripristina preferenze |

<a id="sec-7"></a>
## 7. Riferimento del menu

Tutto vive sotto un unico slot di primo livello, così che installare altri plugin NekoWorks
non allarghi mai la barra dei menu.

```
NekoWorks
└── NekoDynamicCollision
    ├── Window
    │   └── Open Main Window              Cmd/Ctrl+Shift+D
    ├── Bake
    │   ├── Bake Selected                 Cmd/Ctrl+Shift+B
    │   ├── Rebuild Selected
    │   └── Bake Contact Map
    ├── Gizmo
    │   ├── Toggle Hull Gizmo
    │   └── Highlight Uncovered Faces
    ├── Tools
    │   ├── Bone Precision (Expert)
    │   ├── Material Forge
    │   ├── Mirror RTL Interface
    │   └── Refresh Component Icon
    ├── Diagnostics
    │   └── Collision Health
    └── Help
        └── About Neko Dynamic Collision
```

Le didascalie dei menu sono localizzate al caricamento e al cambio di lingua; le stringhe
statiche in inglese negli attributi sono il fallback se l'API interna dei menu di Unity non è
disponibile nella tua versione.

<a id="sec-8"></a>
## 8. Precisione

Una manopola, quattro passi. Internamente si espande in quattro valori:

| Precisione | Triangoli per involucro | Involucri per parte | Sovrapposizione di giunzione | Soglia di peso |
|---|---|---|---|---|
| Grossolana | 120 | 1 | 4.0 mm | 0.35 |
| Normale | 250 | 2 | 3.0 mm | 0.25 |
| Fine | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **Triangoli per involucro** limita quanta geometria può assorbire un singolo involucro. Più triangoli
  per involucro significa involucri meno numerosi, più grandi e più allentati.
- **Involucri per parte** è il numero obiettivo di cluster per partizione. Più cluster
  significa una vestizione più aderente e più collider.
- **Sovrapposizione di giunzione** gonfia ogni involucro verso l'esterno, così che le regioni vicine si sovrappongano
  invece di lasciare uno spazio vuoto. La sovrapposizione è sicura (un colpo non viene mai mancato, e la
  de-duplicazione nello stesso frame impedisce eventi doppi); uno spazio vuoto non lo è.
- **Soglia di peso** scarta i vertici il cui peso per l'osso dominante è inferiore
  al valore. Più alta significa un involucro più aderente e più "rigidamente corretto".

Il conteggio dei vertici di un involucro non può mai superare il conteggio dei vertici univoci del cluster, che
è limitato a 250 — ben sotto il limite di PhysX di 255. Il controllo di salute segnala in rosso
qualsiasi involucro sopra 255.

### 8.1 La modalità convex decompone — oltre CC e il collider complesso integrato

In modalità **convex** una mesh concava viene tagliata in pezzi convessi lungo le sue concavità. È la stessa idea del collider complesso integrato e degli strumenti in stile V-HACD (CC): prendi una mesh qualsiasi e produci parti convesse. NDC mantiene l'ampiezza e alza il tetto:

| | Collider complesso / CC | NDC |
|---|---|---|
| Qualsiasi mesh | sì | sì |
| Ben ottimizzato | sì | sì — lavoro di cottura in background, avanzamento, annullamento, budget per osso |
| I pezzi rispettano le articolazioni | **no** — puramente geometrico, una spalla può inghiottire un braccio | **sì** — i pezzi portano etichette ossee da un campo di pesi |
| Utilizzabile su un corpo in movimento | **no** — PhysX rifiuta un collider non convesso su un `Rigidbody` non cinematico | **sì** — l'output è un involucro convesso |
| Costo a runtime | cottura del collider al caricamento | zero — cotto negli asset |
| Ripiego | — | clustering spaziale, così una mesh degenere ottiene comunque un collider |

La finestra Expert indica se i pezzi provengono dalla **decomposizione** (tagliati lungo le concavità) o dal **clustering spaziale** (il ripiego), quindi la differenza è un numero e non un'ipotesi.

**Quanto è fine il taglio** lo decide un solo cursore — **Decomposition detail** —, con la dimensione del voxel in millimetri nel campo numerico subito sotto. I due sono due viste di *un* numero, quindi concordano sempre: trascina il cursore e il campo lo segue, digita nel campo e il cursore si muove. Non c'è un secondo parametro da tenere sincronizzato.

- **Sinistra** — un voxel grosso: pezzi meno numerosi e più grandi. Il più economico, e di solito sufficiente per un props.
- **Destra** — il voxel più fine. I pezzi seguono la superficie, quindi il costo **eguaglia la modalità non convessa**: non c'è nulla di più fine da guadagnare, solo costo.

La tabella di precisione qui sopra si applica allora al *fitting dei cluster* — quanto strettamente aderisce ogni pezzo —, non a quanti collider ottieni.

Lo stesso cursore compare in entrambe le modalità: la decomposizione convessa e la semplificazione non convessa sono i due modi di rispondere alla stessa domanda: *quanto dettaglio voglio*.

### 8.2 Il dettaglio non convesso è un solo cursore

Passa a **Collider shape → Non-convex surface** e compare un cursore **Surface detail**.

| Cursore | Risultato |
|---|---|
| Tutto a sinistra | Superficie fortemente semplificata — pochi triangoli, sfaccettature visibili |
| Al centro | Un buon compromesso: la forma si legge bene, il collider resta economico |
| **Tutto a destra** | **Nessuna semplificazione** — la superficie è presa dalla mesh così com'è |

Il motivo per cui è un cursore e non un numero di triangoli: "quanti triangoli per pezzo" non si può scegliere in modo sensato senza sapere quanti ne ha la mesh — 500 è grossolano per un torso e preciso per un dito. Il cursore risponde all'unica domanda che un utente può davvero porsi: *quanto mi importa della forma esatta*. La posizione tutta a destra non è "quasi esatta", è esatta: la semplificazione è del tutto disattivata.

I cluster piccoli non vengono mai semplificati, qualunque sia la posizione del cursore — portare un dito sottile su una griglia grossolana lo fa collassare a nulla, e un collider vuoto è peggio di uno costoso.

Sia la modalità Skin sia quella Mesh usano lo stesso cursore.

<a id="sec-9"></a>
## 9. Il pennello

Il pennello non "esclude" le facce. Le **etichetta**, e le etichette guidano la cottura.

| Azione | Effetto |
|---|---|
| Trascinamento sinistro | Assegna il gruppo corrente ai triangoli sotto il cursore |
| Shift + trascinamento | Cancella tornando al gruppo 0 e azzera il flag dipinto |
| Rotella del mouse | Raggio del pennello |
| Interruttore `X` (finestra) | Rispecchia ogni tratto rispetto a X locale = 0 dell'oggetto |

Impostazioni: raggio, X-Ray (ignora i triangoli rivolti verso il retro), mirror su X.

Requisiti, applicati con un messaggio esplicito anziché un fallimento silenzioso:

1. La modalità Play deve essere ferma.
2. La finestra Animation non deve essere in anteprima.

Il rig **non** deve essere in posa di bind. Il pennello esegue raycast contro la mesh
nella sua posa **corrente**; poiché lo skinning non cambia mai la topologia, gli indici dei triangoli
corrispondono uno a uno e le etichette restano corrette.

Se vuoi comunque il rig in posa di bind, il pulsante **Reset to bind pose** risolve le
trasformazioni locali da `bindposes[i].inverse` e le riscrive, con undo.

<a id="sec-10"></a>
## 10. Gruppi di materiali e preset

Ogni gruppo porta un `PhysicMaterial`, una densità in kg/m³, un nome di evento opzionale
e un moltiplicatore di danno.

**Preset.** 224 preset in 14 categorie (Metal, Ceramic, Plastic, Glass, Wood,
Stone & Concrete, Rubber, Fabric & Leather, Body parts, Tissue & organs, Organic, Ice,
Food, Other) — vedi [Appendice A](#appendix-a-physic-material-presets).
Applicare un preset crea un vero asset `.physicMaterial` sotto `Baked/<Scene>/Materials/`,
quindi può essere referenziato, confrontato, messo in Addressables e consegnato a un artista.

**La forgia dei materiali.** Una riga della tabella risponde «di cosa è fatto»; la forgia
risponde «cosa è adesso». Moltiplica un preset base per uno **stato superficiale** (Dry,
Wet, Oiled, Bloody, Sweaty, Icy, Frozen, Dusty, Rough, Polished, Rusted, Worn, Charred,
Clothed, Armoured). `Dry` è l'identità, quindi non esiste mai uno `steel_dry` accanto a
uno `steel`. L'attrito è moltiplicato sia sul coefficiente statico sia su quello dinamico.

**Le parti del corpo sono derivate, non digitate.** «Parti del corpo» e «Tessuti e organi»
non sono righe della tabella: si calcolano da una miscela di tessuti — la densità è
additiva, la morbidezza è additiva più un termine di imbottitura, e attrito e rimbalzo
seguono la morbidezza. «Seno» è 80% grasso + 10% muscolo + 10% pelle; «cranio» è 95% osso
+ 5% pelle.

**Generare asset.** *Genera asset varianti* scrive un `.physicMaterial` per stato in
`Baked/<Scene>/Materials/`.

**Strategia di combinazione.** L'intera libreria usa `Multiply` per l'attrito e `Maximum`
per il rimbalzo. La priorità di combinazione di Unity è
`Average < Minimum < Multiply < Maximum`, quindi con questa strategia qualsiasi superficie scivolosa
domina il risultato dell'attrito e qualsiasi materiale rimbalzante domina il risultato del rimbalzo —
che è ciò che le persone si aspettano intuitivamente.

**Tabella del comportamento delle coppie.** La scheda Materials risolve ogni coppia di gruppi nel tuo
progetto usando le vere regole di priorità di Unity e mostra il valore che si applicherà
davvero, più un verdetto in linguaggio semplice ("aderente / nessun rimbalzo"). Questo è il modo più rapido
per rispondere a "perché il mio ghiaccio non è scivoloso".

**Assegnazione automatica per nome.** Riempie ogni gruppo dai preset confrontando il nome del gruppo
con parole chiave in inglese, cinese e russo.

**Limitazione onesta.** Un `PhysicMaterial` ha quattro numeri e due modalità di combinazione. Non
può esprimere attrito volvente, attrito anisotropo, viscosità, deformazione
plastica, temperatura o usura. "Parametri del mondo reale" qui significa una tabella di consultazione
basata su fonti e preset utilizzabili — non una simulazione fisica.

<a id="sec-11"></a>
## 11. Diagnostica della copertura

La scheda Bake risponde alla domanda che di solito è un tiro a indovinare: **quali triangoli non
hanno alcun involucro?**

Prende la mesh sorgente in posa di bind e verifica il centroide di ogni triangolo contro i piani
di ogni involucro, riportando:

- una percentuale complessiva e una barra di avanzamento;
- un'analisi dettagliata per partizione;
- l'elenco dei triangoli non coperti, disegnabile nella Scene view in rosso
  (**Show uncovered faces**).

Considera un valore sotto ~95% come un problema: alza la precisione, o verifica che gli ossi delle partizioni
coprano davvero l'intero scheletro.

<a id="sec-12"></a>
## 12. Salute delle collisioni

Un punteggio su 100 con ogni problema elencato e, dove possibile, una correzione con un clic.

I controlli includono: nulla cotto; involucro oltre il limite di vertici di PhysX; involucri vicini
al limite; cluster degeneri; triangoli che non appartengono ad alcuna partizione; la mesh sorgente
modificata dopo l'ultima cottura; gruppi di materiali senza materiale, senza involucri o con troppi
involucri frammentati; un ruolo Hitbox/Trigger senza layer di interazione; nessun `Rigidbody` nella
catena dei genitori; massa del rigidbody troppo piccola o troppo grande; autocollisione del ragdoll
completamente attiva; eventi inviati senza alcun listener; e i controlli di fisica a livello di progetto in
[Appendice B](#appendix-b-project-physics-checks).

<a id="sec-13"></a>
## 13. Eventi e integrazione

Ogni evento porta con sé un contesto completo, così non devi mai più cercare nulla:

```csharp
public struct DycEvent
{
    public DycEventKind kind;          // CollisionEnter/Exit, TriggerEnter/Exit
    public int elementIndex;
    public string elementName;
    public Transform bone;             // the bone the hull is attached to
    public int groupIndex;
    public string groupName;
    public PhysicMaterial material;
    public Collider selfCollider;      // which of your hulls
    public Collider otherCollider;
    public Rigidbody otherBody;
    public GameObject otherRoot;
    public Vector3 point;
    public Vector3 normal;
    public float relativeSpeed;
    public float damageMultiplier;     // element × group
    public string eventName;
    public float time;
}
```

Tre modi per consumarlo:

1. **UnityEvent** — `onEvent` sul componente, per listener registrati da codice.
2. **Registro di stringhe** — assegna a una partizione o a un gruppo un nome di evento e ascolta con
   `Dyc_Events.Register("Hit.Head", handler)`. I nomi scritti male non generano errori, ma
   il controllo di salute segnala le spedizioni che nessuno ha ricevuto.
3. **Facciata statica** — `Dyc_Api` per strumenti esterni:

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

I moltiplicatori di danno risiedono sulla partizione e sul gruppo e vengono moltiplicati tra loro —
testa ×4 è un numero, non uno strato di codice colla.

**De-duplicazione.** La sovrapposizione di giunzione significa che due gruppi adiacenti possono entrambi toccare lo stesso
collider estraneo in un frame. NDC invia al massimo un evento per
`(partizione, altro collider)` per frame, quindi i materiali partizionati non raddoppiano gli eventi.

<a id="sec-14"></a>
## 14. Polling dei Trigger

Unity consegna i callback dei trigger **per coppia di Rigidbody**, quindi un singolo ragdoll è una
sola coppia di rigidbody e il layer di fisica semplicemente non può dirti quale osso è entrato in un
volume. Dare a ogni osso il proprio `Rigidbody` distruggerebbe la promessa di costo zero.

Quindi i trigger partizionati vengono campionati invece:

- Ogni partizione viene testata con `Physics.OverlapBoxNonAlloc` sui bounds in spazio mondo
  dei suoi collider.
- Vengono considerati solo i trigger reali, e mai i tuoi stessi collider.
- Le partizioni vengono elaborate a fette: `elements / frames-per-pass` per frame.
- Enter ed exit vengono confrontati con il passaggio precedente per ogni partizione.

**Semantica da ricordare:** questo è campionamento, non un evento. Un passaggio molto veloce può essere
mancato. Alza la frequenza di campionamento, o usa il **sweep margin** per espandere la casella di query.

<a id="sec-15"></a>
## 15. Massa, autocollisione e LOD

**Massa dalla densità.** NDC conosce il volume di ogni involucro, quindi può calcolare la massa correttamente:
`mass = hull volume × group density`, opzionalmente normalizzato così che l'intero personaggio
corrisponda a una massa totale obiettivo. Questo elimina il più antico lavoro di messa a punto manuale nei
ragdoll di Unity. Un singolo rigidbody riceve la somma dei volumi degli involucri che possiede.

**Autocollisione.** `Ignore` (tutte le coppie), `Adjacent` (stessa partizione, o antenato e
discendente) oppure `On`. Le ossa del ragdoll che collidono tra loro sono una fonte comune di
jitter, e `Adjacent` è la risposta usuale. Oltre 200 collider il passo viene saltato
con un avviso anziché bloccare `Awake`.

**LOD.** `Disable` disattiva i collider oltre una certa distanza; `Reduce` mantiene solo
l'involucro più grande per partizione. Il controllo viene eseguito ogni quattro frame.

**Rigidbody.** Unity consegna i callback di collisione solo al GameObject che possiede il
`Rigidbody`. Per un ragdoll, ogni osso ne ha già uno. Per qualsiasi altra cosa, abilita
**Auto-add Rigidbody** e NDC ne crea uno cinematico sull'oggetto del componente.

<a id="sec-16"></a>
## 16. Localizzazione

La finestra, l'inspector, i messaggi di salute e le didascalie dei menu sono localizzati
in **15 lingue**:

`en` (integrato) · `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- L'inglese è integrato nell'assembly ed è il fallback per qualsiasi chiave mancante, quindi una
  lingua parzialmente tradotta degrada invece di rompersi.
- Ogni altra lingua è puro dato in `Locale/<code>/strings.json` — aggiungerne una
  non richiede ricompilazione.
- L'arabo e l'ebraico sono completamente da destra a sinistra: il layout si specchia anziché affidarsi
  a `style.direction`, il cui supporto in UI Toolkit è incompleto e dipendente dalla versione.
- Cambia la lingua nella scheda **Settings**. La finestra e il menu si aggiornano
  immediatamente, senza domain reload.
- La scheda Settings mostra anche il percorso della locale risolto e quante lingue sono
  state trovate, così un errore di packaging è visibile anziché silenzioso.

<a id="sec-17"></a>
## 17. Struttura delle directory

```
Assets/NekoDynamicCollision/
├── Runtime/                  runtime assembly (no UnityEditor references)
│   ├── Dyc_DynamicCollision.cs    the component
│   ├── Dyc_BakedSet.cs            baked data asset
│   ├── Dyc_PaintMask.cs           brush labels
│   ├── Dyc_Types.cs               enums, elements, groups, event struct
│   ├── Dyc_Events.cs              string registry
│   ├── Dyc_Relay.cs               collision forwarding
│   ├── Dyc_TriggerPoll.cs         partitioned trigger sampling
│   └── Dyc_Api.cs                 integration facade
├── Editor/                   editor-only assembly
│   ├── Core/                      baker, hull maths, clustering, health, presets
│   └── UI/                        window, inspector, brush, gizmo, icon, style
├── Locale/<code>/strings.json     14 language packs (+ built-in English)
├── Documents/GUIDE.<code>.md      15 manuals
├── Baked/<Scene>/                 bake output (generated)
├── README.md
└── package.json
```

<a id="sec-18"></a>
## 18. Disinstallazione

1. Rimuovi il componente **Dynamic Collision** dai tuoi prefab e dalle scene.
2. Elimina `Assets/NekoDynamicCollision`.

Gli asset cotti vivono sotto `Baked/` dentro la cartella del plugin e se ne vanno con essa. Nulla
viene scritto fuori dalla cartella del plugin, e il runtime non contiene codice che dipende
dalla metà editor.

<a id="sec-19"></a>
## 19. Risoluzione dei problemi e FAQ

**Non collide nulla, e non parte alcun evento.**
Non c'è alcun `Rigidbody` nella catena dei genitori. Unity invia i callback di collisione solo
all'oggetto che possiede il rigidbody. Abilita **Auto-add Rigidbody**, o aggiungine uno tu.

**Gli involucri non corrispondono a ciò che vedo.**
Il Gizmo disegna la **posa di bind** per impostazione predefinita — è ciò che è stato cotto. Disattiva
**Bind pose** nella scheda Gizmo per vederli nella posa corrente.

**"L'involucro ha N vertici, oltre il limite di PhysX di 255."**
Unity ignora silenziosamente un involucro convesso oltre il limite. Abbassa la precisione di un passo; il
controllo di salute offre esattamente questo come correzione con un clic.

**I colpi vengono mancati in alcuni punti.**
Controlla prima la percentuale di copertura. Sotto ~95% significa buchi reali. Poi controlla la
**sovrapposizione di giunzione** per il passo di precisione in cui ti trovi.

**Un tratto di pennello ha lasciato uno spazio tra due regioni.**
Questo è il problema della giunzione. Alza la precisione (che riduce la sovrapposizione di giunzione) o dipingi un
po' oltre il confine. La de-duplicazione nello stesso frame impedisce già eventi doppi
dalla sovrapposizione.

**Gli eventi partono due volte per un singolo colpo.**
Due partizioni diverse sono state colpite nello stesso frame, il che è legittimo. Se vuoi davvero
un evento per coppia di oggetti, filtra per `elementIndex` nel tuo handler.

**Ho dipinto ma nulla è cambiato dopo la cottura.**
Le etichette vengono ignorate quando il conteggio dei triangoli non corrisponde alla mesh sorgente —
di solito dopo un re-import o un cambiamento di topologia. Ridipingi, o cuoci prima così l'asset
delle etichette viene creato con la dimensione giusta.

**Il pennello non si avvia.**
La modalità Play è in esecuzione, o la finestra Animation è in anteprima. Entrambe sono mostrate come
motivo esplicito nella scheda Paint.

**Il mio vecchio lavoro con il pennello è sparito dopo l'aggiornamento.**
Non dovrebbe: le maschere create prima che esistesse il flag dipinto vengono migrate, e qualsiasi
etichetta diversa da zero è trattata come dipinta. Se una maschera è stata azzerata, ridipingi e cuoci di nuovo.

**Il costo a runtime è davvero zero?**
In condizioni stabili, sì: gli involucri sono asset, i transform sono seguiti dalla gerarchia, e
non c'è alcun lavoro sulla mesh. L'unico lavoro per frame è il polling opzionale dei trigger
e il controllo di distanza del LOD.

**Posso avere due componenti Dynamic Collision su un oggetto?**
No, ed è bloccato di proposito. Due componenti creerebbero involucri duplicati sulle
stesse facce, raddoppierebbero i contatti e raddoppierebbero gli eventi. Partiziona con partizioni e
gruppi di materiali invece.

<a id="sec-20"></a>
## 20. Contatti

NekoAndreeva — vedi `package.json` per l'URL del repository.

---

<a id="sec-appA"></a>
## 21. Uso ordinario e parità con RASCAL

### 21.1 NDC come collider di sistema

La regola di progettazione è che chi conosce `Collider` e `Rigidbody` conosce già NDC, perché NDC *crea* collider ordinari: `MeshCollider` su figli nascosti, un `Rigidbody` sull'oggetto, messaggi standard, layer e materiali fisici ordinari. `Physics.Raycast` e `Physics.OverlapSphere` non richiedono alcuna modifica.

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // aggiungi + costruisci
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // un messaggio Unity standard
void OnTriggerStay(Collider other) { }     // un messaggio Unity standard
```

| Chiamata | Significato |
|---|---|
| `Find(go)` | Il componente, sull'oggetto o su un genitore |
| `Attach(go, generateNow)` | Aggiunge il componente e costruisce |
| `Build(go)` / `Rebuild(go)` | Costruisce dall'insieme cotto, o genera a runtime |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | Stato, tutti gli involucri insieme |
| `SetTrigger(go, v)` | `Collider.isTrigger` per ogni involucro |
| `SetMaterial(go, pm)` | Immediato; non sopravvive a una ricostruzione |
| `GetColliders(go)` / `ForEachCollider(go, a)` | Gli involucri, come `Collider` ordinari |
| `SetReceiver(go, t)` | Invia i messaggi standard anche a `t` |

**Solo la zonizzazione è in più.** Zone, materiali dipinti, LOD, eventi, massa dalla densità e il controllo di salute richiedono l'API NDC — sono le cose che un collider di sistema non può fare.

**Nessun passaggio di cottura.** Attiva **Advanced ▸ Build at startup when nothing is baked** (o chiama `Attach`). I collider sono costruiti per osso dalla mesh all'`Awake` — un involucro convesso per osso, come l'impostazione predefinita di RASCAL. La cottura resta il modo per ottenere zone, decomposizione, copertura e precisione.

**I messaggi che raggiungono il tuo script.** Unity consegna `OnCollision*` all'oggetto con il `Rigidbody`. Se il tuo script è altrove (una radice del personaggio mentre il Rigidbody è su un osso), imposta `Advanced ▸ Also send OnCollision*/OnTrigger* to` — i messaggi vengono allora inoltrati con `SendMessage`, che non costa nulla nei frame senza collisione.

### 21.2 Live update — la capacità che la cottura non può sostituire

Un involucro cotto incollato a un osso è esatto in posa di bind e rigido dopo. Sotto una forte deformazione — un accovacciamento, un arto compresso, un tessuto teso — l'involucro riporta male la superficie. Live update ricostruisce l'involucro dalla posa di skinning **attuale**.

Attivalo con **Advanced ▸ Live update** o `Dyc_Collision.EnableLiveUpdate(go)`.

| Impostazione | Predefinito | Significato |
|---|---|---|
| `liveUpdate` | off | Ricostruisce gli involucri dalla posa attuale |
| `liveUpdateContinuous` | on | Continua, o esegue una passata su richiesta |
| `idleCpuBudgetMs` | 0.2 | Budget mentre la mesh si muove appena |
| `activeCpuBudgetMs` | 1.0 | Budget mentre si muove velocemente |
| `meshUpdateThreshold` | 0.02 | Salta la passata sotto questo movimento (metri) |
| `maxColliderTriangles` | 5000 | Tetto per collider, così un osso pesante non si mangia il budget |

Il budget è scelto in base a quanto si è mossa davvero la mesh, quindi a un personaggio fermo si applica la tariffa idle e a uno che corre quella attiva. Il lavoro che non entra è rinviato al frame successivo, e `OnUpdateYield` / `OnPassComplete` riportano i millisecondi trascorsi.

**Non ricostruisce tutto ogni frame.** Tre meccanismi mantengono il costo prevedibile:

1. **Incrementale.** Il centro di ogni cluster è confrontato con la passata precedente, e solo i cluster che si sono davvero mossi vengono ricostruiti. Un corpo morbido appeso a un'ancora ha un bordo che trema e un centro quasi fermo — il centro non costa nulla.
2. **Ordinato per priorità.** La coda è ordinata in base a quanto si è mosso ogni cluster. Se il budget finisce, finisce sui cluster più calmi — quelli dove l'imprecisione si vede meno. Senza questo, il budget verrebbe speso su qualunque cosa capitasse prima nella lista.
3. **Budget basato sull'orologio**, non sul numero di cluster: il costo per frame non cresce con il numero di cluster del corpo.

`LastDirtyCount` e `LastBuiltCount` riportano cosa ha fatto davvero l'ultima passata, che è il modo onesto di vedere il risparmio.

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"pass done in {ms:F2} ms");
live.StopAfterPass();          // termina la passata corrente, poi fermati
live.UpdateNow();              // una passata completa, fuori budget
```

**Requisiti.** Gli involucri hanno bisogno di `sourceVertices`, scritto dalla cottura; ricuoci un personaggio più vecchio per attivare live update. Il costo a runtime è reale — è l'unica funzione che contraddice "zero costo per frame", ed è esattamente per questo che è disattivata per impostazione predefinita.

### 21.3 Override per osso

`Dyc_BoneProperties` va sull'osso stesso (Add Component ▸ NekoWorks ▸ Dynamic Collision ▸ Bone Properties):

| Campo | Effetto |
|---|---|
| `overrideMaterial` + `physicsMaterial` | Gli involucri di questo osso usano quel materiale |
| `overrideConvex` + `convex` | Involucro invece di superficie (o viceversa) per questo osso |
| `overrideWeightThreshold` + `boneWeightThreshold` | Soglia di peso per osso |
| `exclude` | Nessun collider per questo osso |

Appenderlo all'osso significa che sopravvive alle rinomine — conserva un riferimento, non un percorso.

### 21.4 Materiali per materiale di origine

`Advanced ▸ Materials by source material` mappa un `Material` di origine su un `PhysicMaterial`. Gli involucri sono attribuiti in base alla submalla da cui provengono in prevalenza, risolta in fase di cottura in `Dyc_BakedSet.sourceMaterials`. Priorità, dalla più alta:

1. `Dyc_BoneProperties.physicsMaterial`;
2. l'associazione di materiale per il materiale di origine dell'involucro;
3. il materiale del gruppo dipinto.

### 21.5 Mappa di esclusione dei vertici

`Advanced ▸ Exclusion map` legge un canale della texture (R/G/B/A, con una soglia) ed esclude i vertici il cui valore di canale è pari o superiore a essa. Il pennello marca le *facce*, la mappa marca i *vertici* — si completano a vicenda. La mesh ha bisogno di UV, la texture di **Read/Write Enabled**, e un triangolo è escluso solo quando lo sono tutti e tre i suoi vertici.

### 21.6 Riorientare lo scheletro

`Advanced ▸ Attach hulls to another skeleton` costruisce gli involucri da questa mesh ma li appende a ossa con lo stesso nome di un'altra radice — il caso `RetargetSkeleton`, per Puppet Master e configurazioni simili. Le ossa sono risolte per percorso relativo; un involucro senza omologo resta sul proprio scheletro e il rapporto di cottura dice quanti.

### 21.7 Modalità soft — senza ossa, guidata da codice o da un solver

La modalità soft (`Mode → Soft`) **non** è un rig con skinning. È per una mesh **senza scheletro** la cui forma è prodotta da un solver o dal codice — NekoDynamicSoftbody e simili. Nulla nella modalità soft legge le ossa; la geometria della mesh è presa così com'è.

**Cosa produce la cottura.** La mesh è divisa in cluster spaziali numerati. Ogni involucro è costruito *rispetto al centro del suo cluster*, e il centro è memorizzato come posa di riposo del cluster (`clusterRest`). È questo che permette a un frame di traslare **e ruotare** l'involucro come un sol pezzo.

**Senza solver.** I frame sono creati alle loro pose di riposo, quindi gli involucri poggiano esattamente sulla geometria reale della mesh e si muovono con l'oggetto. La forma è giusta; semplicemente non c'è dinamica. Questo è il degrado previsto, non un fallimento — ed è ciò che significa "calcola la forma reale senza NDSC".

**Con solver.** Il solver spinge i frame (`Push` → `Apply`) e prende il controllo completo, dando una simulazione completa. Un push indirizzabile non viene sovrascritto dal polling globale nello stesso frame, cosa che conta non appena esiste più di un corpo.

**Live update funziona anche senza scheletro.** `Dyc_LiveUpdate` legge i vertici CPU di un `MeshFilter` così come sono, quindi qualsiasi codice che deforma la mesh — un solver di corpi morbidi, uno script procedurale, un deformatore personalizzato — guida involucri accurati senza colla specifica del plugin. Un involucro ha bisogno di `sourceVertices`, scritto dalla cottura; ricuoci un asset più vecchio.

**Il limite onesto:** la deformazione che esiste solo sulla GPU (un vertex shader, skinning GPU) non può essere riletta sulla CPU, quindi live update non la vede. Sposta la deformazione sulla CPU, oppure conserva gli involucri cotti.

### 21.8 Pilotare singoli involucri

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`, `HullIndexOf`, `HullCount`, `BakedSet`, `Elements` e `Groups` sono pubblici, quindi gli strumenti esterni possono iterare e pilotare singoli involucri senza riflessione.

---

## Appendice A. Preset dei materiali fisici

224 preset, 14 categorie. I valori sono approssimazioni ingegneristiche basate su fonti, mappate sul modello a quattro parametri di Unity. «Parti del corpo» e «Tessuti e organi» sono derivati dalla forgia da una miscela di tessuti invece che scritti nella tabella.

| Categoria | Conteggio | Preset |
|---|---|---
| Metal | 26 | Steel, Stainless, Cast Iron, Aluminium, Cast Aluminium, Anodised, Copper, Brass, Bronze, Titanium, Lead, Tungsten, Chrome, Nickel, Zinc, Magnesium, Gold, Silver, Platinum, Galvanised, Rusted Steel, Gun Steel, Tool Steel, Armour Steel, Sheet Metal, Rebar |
| Ceramic | 10 | Porcelain, Bone China, Ceramic Tile, Terracotta, Alumina, Silicon Carbide, Zirconia, Glass-Ceramic, Enamel, Unfired Clay |
| Plastic | 18 | ABS, PVC, Nylon, Acrylic, Polycarbonate, HDPE, PTFE, PEEK, POM, PET, Polypropylene, Polystyrene, TPU, PLA, Bakelite, Melamine, Vinyl, Rigid Urethane |
| Glass | 9 | Glass, Tempered, Laminated, Frosted, Thick, Armoured, Mirror, Crystal, Borosilicate |
| Wood | 16 | Oak, Pine, Plywood, Cork, Birch, Maple, Walnut, Teak, Mahogany, Balsa, MDF, Particleboard, Bamboo, Wet Wood, Charred Wood, Round Timber |
| Stone & Concrete | 15 | Concrete, Wet Concrete, Brick, Asphalt, Granite, Marble, Polished Marble, Limestone, Sandstone, Slate, Basalt, Cobblestone, Gravel, Rubble, Sand |
| Rubber | 12 | Rubber, Tire, SBR, Latex, EPDM, Butyl, Neoprene, Silicone, Hose Rubber, Rubber Mat, Rubber Foam, Shock Gel |
| Fabric & Leather | 25 | Leather, Canvas, Carpet, Kevlar, Body Armour, Vest Shell, Cotton, Denim, Silk, Satin, Wool, Linen, Burlap, Velvet, Felt, Nomex, Spandex, Gore-Tex, Parachute, Nylon, Suit Fabric, Upholstery, Heavy Tarp, Blanket, Towel |
| Body parts | 39 | Head, Skull, Scalp, Hair, Face, Cheek, Lip, Jaw, Nose, Ear, Eyeball, Tooth, Tongue, Neck, **Chest**, Breast, Abdomen, Groin, Back, Hip, Pelvis, Shoulder, Bicep, Elbow, Forearm, Wrist, Hand, Palm, Fist, Knuckle, Nail, Glute, Thigh, Knee, Calf, Shin, Ankle, Foot, Sole |
| Tissue & organs | 10 | Bone, Cartilage, Tendon, Muscle, Fat, Skin, Organ, Lung, Brain, Keratin |
| Organic | 3 | Flesh, Bone, Ballistic Gel |
| Ice, snow & mud | 10 | Ice, Wet Ice, Black Ice, Snow, Powder Snow, Packed Snow, Slush, Hail, Frozen Ground, Mud |
| Food | 9 | Bread, Fruit, Vegetable, Raw Meat, Cooked Meat, Fish, Cheese, Chocolate, Ice Cream |
| Other | 22 | Cardboard, Wet Cardboard, Paper, Foam, Sponge, Bubble Wrap, Drywall, Tarp, Sandbag, Dirt, Grass, Hay, Leaf Litter, Trash Bag, Mineral Wool, Rope, Net, Coal, Ash, Rock Salt, Sugar, Wax |


Ogni preset porta anche una **densità** in kg/m³ per la massa automatica, e i preset della famiglia
della gomma portano il `bounceThreshold` di cui hanno bisogno per rimbalzare affatto.

<a id="sec-appB"></a>
## Appendice B. Controlli di fisica del progetto

Il controllo di salute verifica le impostazioni `Physics` del progetto, perché un preset di materiale
non può correggere un'impostazione globale:

| Impostazione | Perché è importante |
|---|---|
| `bounceThreshold` | Gli impatti più lenti di questo non rimbalzano mai. Al valore predefinito di Unity di 2, un preset di gomma sembra rotto. Abbassalo a 0.2–0.5 per usare materiali elastici. |
| `defaultSolverVelocityIterations` | A 1, le pile e gli impatti veloci vibrano o attraversano. 2–4 di solito è meglio, ed è una causa comune di jitter del ragdoll. |
| `gravity` | Se non è −9.81, ogni intuizione su massa e impulso derivata da −9.81 è sfasata dello stesso fattore, e i preset di densità vanno corretti. |
| `defaultContactOffset` | Un ampio gap di contatto fa sembrare che gli oggetti sottili fluttuino. |

Applicare i valori consigliati è un'azione con un clic dalla scheda Materials o dal
controllo di salute.
