# Neko Dynamic Collision (NDC) — Einrichtung und Handbuch

Gebackene Kollision mit konvexen Hüllen für Unity. Die ganze teure Arbeit passiert im
Editor; die Laufzeit lädt und leitet nur weiter. Ein skinned Charakter oder ein statisches Mesh
wird zu einer Menge von konvexen Hüllen pro Knochen und pro Region mit **null Kosten pro Frame**,
integrierten Partitionsereignissen und pro Region mit dem Pinsel aufgetragenen Physikmaterialien.

Die Komponente selbst enthält keinen generierten Code, keine Attribute und keine Laufzeit-
abhängigkeit von der Editor-Hälfte des Plugins. Lösche `Editor/`, und die Laufzeit funktioniert weiter.

## Inhalt

- [Teil A — Schnelleinrichtung](#sec-partA)
  - [1. Backe deinen ersten Charakter](#sec-1)
  - [2. Materialregionen anmalen](#sec-2)
  - [3. Der 10-Minuten-Pfad](#sec-3)
- [Teil B — Handbuch](#sec-partB)
  - [4. Kernkonzepte](#sec-4)
  - [5. Installation und Anforderungen](#sec-5)
  - [6. Das Backer-Fenster](#sec-6)
  - [7. Menüreferenz](#sec-7)
  - [8. Genauigkeit](#sec-8)
  - [9. Der Pinsel](#sec-9)
  - [10. Materialgruppen und Voreinstellungen](#sec-10)
  - [11. Abdeckungsdiagnose](#sec-11)
  - [12. Kollisionszustand](#sec-12)
  - [13. Ereignisse und Integration](#sec-13)
  - [14. Trigger-Abfrage](#sec-14)
  - [15. Masse, Selbstkollision und LOD](#sec-15)
  - [16. Lokalisierung](#sec-16)
  - [17. Verzeichnisstruktur](#sec-17)
  - [18. Deinstallation](#sec-18)
  - [19. Fehlerbehebung und FAQ](#sec-19)
  - [20. Kontakt](#sec-20)
- [Anhang A. Physikmaterial-Voreinstellungen](#sec-appA)
- [Anhang B. Projekt-Physikprüfungen](#sec-appB)

---

<a id="sec-partA"></a>
# Teil A — Schnelleinrichtung

<a id="sec-1"></a>
## 1. Backe deinen ersten Charakter

1. Wähle deinen Charakter aus und füge die Komponente **Dynamic Collision** hinzu
   (`Add Component → Neko → Dynamic Collision`, oder über das `GameObject`-Menü).
   Bei der Erstellung findet die Komponente ihr eigenes `SkinnedMeshRenderer` und legt eine
   Standard-Materialgruppe an. Du musst nichts ausfüllen.
2. Drücke **Backen** im Inspector.
3. Die Hüllen erscheinen in der Szenenansicht als Bind-Pose-Drahtgitter. Wähle den Charakter aus,
   um sie zu sehen; das Gizmo folgt standardmäßig deiner Auswahl.

Das Backen schreibt drei Arten von Assets neben die Szene:

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

Die Hüllen-Meshes sind Sub-Assets von `_Baked.asset`, wandern also damit mit und
überleben den Play-Modus und Builds. Zur Laufzeit wird nichts neu berechnet.

<a id="sec-2"></a>
## 2. Materialregionen anmalen

Der Pinsel ist das, was „ein Objekt, mehrere Physikmaterialien" möglich macht.

1. Öffne den Backer (`NekoWorks → NekoDynamicCollision → Open Main Window`).
2. Gehe zum Tab **Materialien** und füge pro Region eine Gruppe hinzu — zum Beispiel
   `Hard` und `Soft`. Wähle für jede eine Voreinstellung.
3. Gehe zum Tab **Anmalen**, wähle die Gruppe, die du anmalen willst, und drücke **Anmalen starten**.
4. Ziehe in der Szenenansicht über die Flächen, die zu dieser Gruppe gehören sollen. Die angemalten
   Dreiecke werden sofort mit der Gruppenfarbe gefüllt.
5. Backe erneut. Jede Region erhält jetzt ihre eigenen konvexen Hüllen mit ihrem eigenen Physikmaterial.

Du kannst nur anmalen, während der Play-Modus gestoppt ist und das Animationsfenster keine
Vorschau zeigt. Die Pose selbst spielt keine Rolle — Labels werden pro Dreiecksindex gespeichert,
die aktuelle Pose ist also irrelevant.

<a id="sec-3"></a>
## 3. Der 10-Minuten-Pfad

| Minute | Mach das |
|---|---|
| 0–2 | Komponente hinzufügen, Backen drücken, Gizmo ansehen. |
| 2–4 | Tab **Zustand** öffnen, alles Rote beheben. |
| 4–6 | Tab **Backen** öffnen, die Abdeckungszahl ansehen. Unter ~95 % die Genauigkeit erhöhen. |
| 6–9 | Pro Region eine Materialgruppe hinzufügen, anmalen, erneut backen. |
| 9–10 | Interaktions-Layer auf `Bullet` setzen, einen Ereignisnamen verdrahten, im Play-Modus testen. |

---

<a id="sec-partB"></a>
# Teil B — Handbuch

<a id="sec-4"></a>
## 4. Kernkonzepte

### Hüllen statt Dreieckssuppe

Ein dynamischer (nicht-kinematischer) `Rigidbody` kann keinen nicht-konvexen `MeshCollider` verwenden — das
ist eine PhysX-Beschränkung, keine von Unity. Also muss jede Kollisionsform für einen bewegten Körper
konvex sein. NDC backt **konvexe Hüllen** und übergibt Unity die Hülle selbst anstelle
der rohen Dreiecks-Teilmenge, weshalb die Vertexzahl niemals die PhysX-
Grenze von 255 erreichen kann.

### Warum knochenstarre Hüllen ausreichen

In der Bind-Pose gilt `bone.localToWorldMatrix · bindposes[i] = I`. Das Skinning für einen Vertex,
der zu 100 % auf einen Knochen gewichtet ist, ergibt daher genau das Bind-Pose-Mesh. Mit
anderen Worten: **eine im knochenlokalen Raum gebackene Hülle ist bit für bit das, was ein
Neu-Backen pro Frame erzeugen würde**, für starr gewichtete Vertices.

Nur blend-gewichtete Vertices — die, die ein Gelenk überqueren — weichen ab. Diese werden
von benachbarten Hüllen abgedeckt, die sich konstruktionsbedingt überlappen. Deshalb kann NDC zur
Laufzeit kostenlos sein und trotzdem dort genau, wo es zählt.

### Räumliches Clustering statt Aufteilung in Indexreihenfolge

NDC gruppiert Dreiecke nach Position (Farthest-Point-Seeding plus Dijkstra-artiges Wachstum
über Kantennachbarschaft). Die Alternative — Dreiecke in Indexreihenfolge zu nehmen — erzeugt
Hüllen, die einander überlappen und Luft umschließen, und wird umso schlechter, je mehr Hüllen man verlangt.

### Abschnitte und Materialgruppen

Zwei unabhängige Achsen:

- **Abschnitt** — *wo*. Ein Knochen (Skin-Modus) oder das ganze Mesh (Mesh-Modus).
  Ein untergeordneter Abschnitt schlägt immer einen Vorfahren, daher werden Ereignisse nie doppelt ausgelöst.
- **Materialgruppe** — *was*. Eine Menge von Flächen, die sich ein Physikmaterial und eine
  Dichte teilen, erzeugt durch Anmalen.

Eine Hülle ist die Schnittmenge aus einem Abschnitt und einer Materialgruppe. Hat eine Gruppe keine
angemalten Flächen innerhalb eines bestimmten Knochens, wird für dieses Paar keine Hülle erzeugt.

### Was die Laufzeit tut

1. Lädt das gebackene Set.
2. Erzeugt ein verstecktes Kindobjekt pro Hülle unter dem richtigen Knochen, mit einer Identitäts-
   Transformation, und weist einen konvexen `MeshCollider` zu.
3. Baut eine `Collider → hull`-Nachschlagetabelle auf.
4. Platziert ein `Dyc_Relay` auf jedem `Rigidbody`, das eine Hülle besitzt.
5. Konfiguriert Layer, Selbstkollision und Masse.

Dann hört es auf. Es gibt keine `Update`-Arbeit außer einer optionalen Trigger-Abfrage und einer
Distanzprüfung für LOD.

<a id="sec-5"></a>
## 5. Installation und Anforderungen

- Unity 2022.3 oder neuer.
- Kopiere `Assets/NekoDynamicCollision` in dein Projekt. Es gibt nichts zu
  konfigurieren; die Assemblies sind durch Assembly-Definitionen abgegrenzt.
- Zwei Assemblies:
  - `Neko.DynamicCollision.Runtime` — die Komponente, das Relay, die Trigger-Abfrage, die Ereignis-
    Structs und die Integrations-Fassade. Referenziert nie `UnityEditor`.
  - `Neko.DynamicCollision.Editor` — der Backer, die Hüllen-Mathematik, das Clustering, der Pinsel,
    das Gizmo, die Zustandsprüfung, die Voreinstellungen und das Fenster. Editor-only-Plattform.

<a id="sec-6"></a>
## 6. Das Backer-Fenster

`NekoWorks → NekoDynamicCollision → Open Main Window` (`Cmd/Ctrl+Shift+D`).

| Tab | Was es tut |
|---|---|
| **Backen** | Quelle, Modus, Genauigkeit, Backen/Löschen/Neuaufbau, Statistik, Abdeckung |
| **Anmalen** | Gruppenliste mit Zahlen angemalter Dreiecke, Pinsel-Einstellungen, Pose zurücksetzen |
| **Gizmo** | Was die Szenenansicht zeichnet und wie |
| **Teile** | Die Abschnittsliste — Knochen, Kinder einbeziehen, Ereignisname, Schadensmultiplikator |
| **Materialien** | Materialgruppen, Voreinstellungen, Paarverhalten-Tabelle, Projekt-Physikprüfung |
| **Zustand** | Punktzahl von 100, jedes Problem, Ein-Klick-Korrekturen |
| **Einstellungen** | Sprache, Diagnose, gebackenen Ordner öffnen, Einstellungen zurücksetzen |

<a id="sec-7"></a>
## 7. Menüreferenz

Alles liegt unter einem einzigen obersten Slot, damit die Installation weiterer NekoWorks-
Plugins die Menüleiste nie verbreitert.

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

Menübeschriftungen werden beim Laden und bei Sprachwechsel lokalisiert; die statischen englischen
Strings in den Attributen sind der Fallback, falls Unitys interne Menü-API in deiner Version nicht
verfügbar ist.

<a id="sec-8"></a>
## 8. Genauigkeit

Ein Regler, vier Stufen. Intern entfaltet er sich zu vier Werten:

| Genauigkeit | Dreiecke pro Hülle | Hüllen pro Teil | Nahtüberlappung | Gewichtsschwelle |
|---|---|---|---|---|
| Coarse | 120 | 1 | 4.0 mm | 0.35 |
| Normal | 250 | 2 | 3.0 mm | 0.25 |
| Fine | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **Dreiecke pro Hülle** begrenzt, wie viel Geometrie eine Hülle aufnehmen darf. Mehr Dreiecke
  pro Hülle bedeutet weniger, größere, lockerere Hüllen.
- **Hüllen pro Teil** ist die Zielzahl der Cluster pro Abschnitt. Mehr Cluster
  bedeutet eine engere Passform und mehr Collider.
- **Nahtüberlappung** bläht jede Hülle nach außen auf, damit benachbarte Regionen sich überlappen,
  statt eine Lücke zu lassen. Überlappung ist sicher (ein Treffer wird nie verpasst, und die
  Deduplizierung im selben Frame verhindert doppelte Ereignisse); eine Lücke ist es nicht.
- **Gewichtsschwelle** verwirft Vertices, deren Gewicht für den dominanten Knochen unter
  dem Wert liegt. Höher bedeutet eine engere, „starr korrektere" Hülle.

Die Vertexzahl einer Hülle kann nie die Zahl der eindeutigen Vertices des Clusters überschreiten, die
auf 250 begrenzt ist — sicher unter dem PhysX-Limit von 255. Die Zustandsprüfung markiert jede
Hülle über 255 in Rot.

### 8.1 Der konvexe Modus zerlegt — über CC und den eingebauten komplexen Collider hinaus

Im **konvexen** Modus wird ein konkaves Mesh entlang seiner Konkavitäten in konvexe Stücke geschnitten. Das ist dieselbe Idee wie beim eingebauten komplexen Collider und bei Werkzeugen im V-HACD-Stil (CC): Nimm ein beliebiges Mesh und erzeuge konvexe Teile. NDC behält die Breite und hebt die Obergrenze an:

| | Komplexer Collider / CC | NDC |
|---|---|---|
| Beliebiges Mesh | ja | ja |
| Gut optimiert | ja | ja — Back-Job im Hintergrund, Fortschritt, Abbruch, Budgets pro Knochen |
| Stücke respektieren Gelenke | **nein** — rein geometrisch, eine Schulter kann einen Arm verschlucken | **ja** — Stücke tragen Knochenlabels aus einem Gewichtsfeld |
| Auf einem bewegten Körper nutzbar | **nein** — PhysX verweigert einen nicht-konvexen Collider an einem nicht-kinematischen `Rigidbody` | **ja** — die Ausgabe ist eine konvexe Hülle |
| Laufzeitkosten | Collider-Cooking beim Laden | null — in Assets gebacken |
| Fallback | — | räumliches Clustering, damit ein degeneriertes Mesh trotzdem einen Collider bekommt |

Das Expertenfenster meldet, ob die Teile aus der **Zerlegung** (entlang der Konkavitäten geschnitten) oder aus dem **räumlichen Clustering** (dem Fallback) stammen, sodass der Unterschied eine Zahl ist und keine Vermutung.

**Wie fein der Schnitt ist**, steuert ein einziger Regler — **Decomposition detail** —, wobei die Voxelgröße in Millimetern im Zahlenfeld direkt darunter steht. Die beiden sind zwei Ansichten *einer* Zahl, also stimmen sie immer überein: Zieh den Regler, und das Feld folgt; tippe in das Feld, und der Regler bewegt sich. Es gibt keine zweite Einstellung, die synchron gehalten werden müsste.

- **Links** — ein grobes Voxel: weniger, größere Stücke. Am günstigsten und für ein Requisit meist ausreichend.
- **Rechts** — das feinste Voxel. Die Stücke folgen der Oberfläche, also **entsprechen die Kosten dem nicht-konvexen Modus**: feiner ist nichts zu gewinnen, nur teurer.

Die Genauigkeitstabelle oben gilt dann für die *Cluster-Anpassung* — wie eng jedes Stück anliegt —, nicht für die Zahl der Collider, die du bekommst.

Derselbe Regler erscheint in beiden Modi: konvexe Zerlegung und nicht-konvexe Vereinfachung sind die zwei Wege, dieselbe Frage zu beantworten: *Wie viel Detail will ich*.

### 8.2 Nicht-konvexes Detail ist ein einziger Regler

Schalte **Collider shape → Non-convex surface** um, und ein Regler **Surface detail** erscheint.

| Regler | Ergebnis |
|---|---|
| Ganz links | Stark vereinfachte Oberfläche — wenige Dreiecke, sichtbare Facetten |
| Mitte | Ein guter Kompromiss: Die Form liest sich richtig, der Collider bleibt günstig |
| **Ganz rechts** | **Überhaupt keine Vereinfachung** — die Oberfläche wird unverändert aus dem Mesh übernommen |

Der Grund, warum es ein Regler ist und keine Dreieckszahl: „Wie viele Dreiecke pro Stück" lässt sich nicht sinnvoll wählen, ohne zu wissen, wie viele das Mesh hat — 500 ist grob für einen Torso und präzise für einen Finger. Der Regler beantwortet die einzige Frage, die ein Nutzer tatsächlich stellen kann: *Wie wichtig ist mir die genaue Form*. Die Position ganz rechts ist nicht „fast exakt", sie ist exakt: Die Vereinfachung ist vollständig abgeschaltet.

Kleine Cluster werden unabhängig vom Regler nie vereinfacht — einen dünnen Finger auf ein grobes Raster herunterzuziehen lässt ihn zu nichts kollabieren, und ein leerer Collider ist schlimmer als ein teurer.

Sowohl Skin- als auch Mesh-Modus verwenden denselben Regler.

<a id="sec-9"></a>
## 9. Der Pinsel

Der Pinsel „schließt" Flächen nicht aus. Er **markiert** sie, und die Markierungen steuern das Backen.

| Aktion | Wirkung |
|---|---|
| Links ziehen | Weist den Dreiecken unter dem Cursor die aktuelle Gruppe zu |
| Shift + ziehen | Löscht zurück auf Gruppe 0 und entfernt die Anmal-Markierung |
| Mausrad | Pinselradius |
| `X`-Umschalter (Fenster) | Spiegelt jeden Strich über das lokale X = 0 des Objekts |

Einstellungen: Radius, Röntgen (rückseitige Dreiecke ignorieren), Spiegelung an X.

Voraussetzungen, die mit einer expliziten Meldung statt eines stillen Fehlschlags erzwungen werden:

1. Der Play-Modus muss gestoppt sein.
2. Das Animationsfenster darf keine Vorschau anzeigen.

Das Rig muss **nicht** in der Bind-Pose sein. Der Pinsel raycastet gegen das Mesh
in seiner **aktuellen** Pose; da Skinning die Topologie nie ändert, bilden die Dreiecksindizes
eins zu eins ab, und die Labels bleiben korrekt.

Wenn du das Rig trotzdem in der Bind-Pose haben willst, löst die Schaltfläche **Auf Bind-Pose zurücksetzen** die
lokalen Transformationen aus `bindposes[i].inverse` und schreibt sie zurück, mit Undo.

<a id="sec-10"></a>
## 10. Materialgruppen und Voreinstellungen

Jede Gruppe trägt ein `PhysicMaterial`, eine Dichte in kg/m³, einen optionalen Ereignisnamen
und einen Schadensmultiplikator.

**Voreinstellungen.** 224 Voreinstellungen in 14 Kategorien (Metal, Ceramic, Plastic,
Glass, Wood, Stone & Concrete, Rubber, Fabric & Leather, Body parts, Tissue & organs,
Organic, Ice, Food, Other) — siehe [Anhang A](#appendix-a-physic-material-presets).
Das Anwenden einer Voreinstellung erzeugt ein echtes `.physicMaterial`-Asset unter
`Baked/<Scene>/Materials/`, das referenziert, verglichen, in Addressables gelegt und
einem Artist übergeben werden kann.

**Die Materialschmiede.** Eine Tabellenzeile beantwortet „woraus ist das gemacht“; die
Schmiede beantwortet „was ist es gerade jetzt“. Sie multipliziert eine Basisvoreinstellung
mit einem **Oberflächenzustand** (Dry, Wet, Oiled, Bloody, Sweaty, Icy, Frozen, Dusty,
Rough, Polished, Rusted, Worn, Charred, Clothed, Armoured). `Dry` ist die Identität,
deshalb gibt es nie ein `steel_dry` neben einem `steel`. Reibung wird auf statischen und
dynamischen Koeffizienten zugleich multipliziert.

**Körperteile sind abgeleitet, nicht getippt.** „Körperteile“ und „Gewebe & Organe“ sind
keine Tabellenzeilen: Sie werden aus einer Gewebemischung berechnet — Dichte ist additiv,
Weichheit additiv plus ein Polsterterm, Reibung und Sprung folgen der Weichheit. „Brust“
ist 80 % Fett + 10 % Muskel + 10 % Haut; „Schädel“ ist 95 % Knochen + 5 % Haut.

**Assets erzeugen.** *Varianten-Assets erzeugen* schreibt je ein `.physicMaterial` pro
Zustand nach `Baked/<Scene>/Materials/`.

**Kombinationsstrategie.** Die gesamte Bibliothek verwendet `Multiply` für Reibung und `Maximum`
für Rückprall. Unitys Kombinationspriorität ist
`Average < Minimum < Multiply < Maximum`, also dominiert mit dieser Strategie jede rutschige Oberfläche
das Reibungsergebnis und jedes sprungfreudige Material das Rückprall-Ergebnis —
was der intuitiven Erwartung der Leute entspricht.

**Paarverhalten-Tabelle.** Der Tab Materialien löst jedes Paar von Gruppen in deinem
Projekt anhand von Unitys echten Prioritätsregeln auf und zeigt den Wert, der tatsächlich
gilt, plus ein Urteil in klarer Sprache („griffig / kein Rückprall"). Das ist der schnellste Weg,
um zu beantworten, „warum ist mein Eis nicht rutschig".

**Automatische Zuordnung nach Name.** Füllt jede Gruppe aus Voreinstellungen, indem der Gruppenname
gegen englische, chinesische und russische Schlüsselwörter abgeglichen wird.

**Ehrliche Einschränkung.** Ein `PhysicMaterial` hat vier Zahlen und zwei Kombinationsmodi. Er
kann keine Rollreibung, anisotrope Reibung, Viskosität, plastische
Verformung, Temperatur oder Verschleiß ausdrücken. „Realwelt-Parameter" bedeutet hier eine belegte
Nachschlagetabelle und brauchbare Voreinstellungen — keine physikalische Simulation.

<a id="sec-11"></a>
## 11. Abdeckungsdiagnose

Der Tab Backen beantwortet die Frage, die sonst meist Rätselraten ist: **welche Dreiecke haben
überhaupt keine Hülle?**

Es nimmt das Quell-Mesh in der Bind-Pose und prüft jeden Dreiecksschwerpunkt gegen die Ebenen jeder
Hülle und meldet:

- einen Gesamtprozentsatz und einen Fortschrittsbalken;
- eine Aufschlüsselung pro Abschnitt;
- die Liste der unabgedeckten Dreiecke, in der Szenenansicht rot zeichenbar
  (**Unabgedeckte Flächen anzeigen**).

Betrachte unter ~95 % als Problem: Erhöhe die Genauigkeit, oder prüfe, ob die Abschnittsknochen
tatsächlich das ganze Skelett abdecken.

<a id="sec-12"></a>
## 12. Kollisionszustand

Eine Punktzahl von 100 mit jeder aufgeführten Problemstelle und, wo möglich, einer Ein-Klick-Korrektur.

Zu den Prüfungen gehören: nichts gebacken; Hülle über der PhysX-Vertexgrenze; Hüllen nahe der
Grenze; degenerierte Cluster; Dreiecke, die zu keinem Abschnitt gehören; das Quell-Mesh
hat sich seit dem letzten Backen geändert; Materialgruppen ohne Material, ohne Hüllen oder mit zu vielen
fragmentierten Hüllen; eine Hitbox/Trigger-Rolle ohne Interaktions-Layer; kein `Rigidbody` in der
Elternkette; Rigidbody-Masse, die viel zu klein oder zu groß ist; Ragdoll-Selbstkollision
vollständig an; ausgelöste Ereignisse ohne Listener; und die Physikprüfungen auf Projektebene in
[Anhang B](#appendix-b-project-physics-checks).

<a id="sec-13"></a>
## 13. Ereignisse und Integration

Jedes Ereignis trägt einen vollständigen Kontext, sodass du nie wieder etwas nachschlagen musst:

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

Drei Arten, es zu konsumieren:

1. **UnityEvent** — `onEvent` auf der Komponente, für im Code registrierte Listener.
2. **String-Registry** — gib einem Abschnitt oder einer Gruppe einen Ereignisnamen und höre mit
   `Dyc_Events.Register("Hit.Head", handler)` zu. Falsch geschriebene Namen lösen keinen Fehler aus, aber
   die Zustandsprüfung meldet Dispatches, die niemand empfangen hat.
3. **Statische Fassade** — `Dyc_Api` für externe Werkzeuge:

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

Schadensmultiplikatoren liegen am Abschnitt und an der Gruppe und werden miteinander multipliziert —
Kopf ×4 ist eine Zahl, keine Schicht aus Klebecode.

**Deduplizierung.** Nahtüberlappung bedeutet, dass zwei benachbarte Gruppen im selben Frame denselben
fremden Collider berühren können. NDC löst höchstens ein Ereignis pro
`(element, other collider)` pro Frame aus, sodass partitionierte Materialien keine doppelten Ereignisse erzeugen.

<a id="sec-14"></a>
## 14. Trigger-Abfrage

Unity liefert Trigger-Callbacks **pro Rigidbody-Paar**, ein einzelnes Ragdoll ist also ein
einziges Rigidbody-Paar, und die Physikschicht kann dir schlicht nicht sagen, welcher Knochen ein
Volumen betreten hat. Jedem Knochen ein eigenes `Rigidbody` zu geben, würde das Nullkosten-Versprechen zerstören.

Stattdessen werden partitionierte Trigger abgetastet:

- Jeder Abschnitt wird mit `Physics.OverlapBoxNonAlloc` über die Weltraum-Grenzen
  seiner Collider getestet.
- Nur tatsächliche Trigger werden berücksichtigt, niemals deine eigenen Collider.
- Abschnitte werden in Scheiben verarbeitet: `elements / frames-per-pass` pro Frame.
- Enter und Exit werden pro Abschnitt gegen den vorherigen Durchlauf gedifft.

**Wichtige Semantik:** Das ist Abtastung, kein Ereignis. Ein sehr schneller Durchlauf kann
verpasst werden. Erhöhe die Abtastrate oder nutze die **Sweep-Margin**, um die Abfragebox zu erweitern.

<a id="sec-15"></a>
## 15. Masse, Selbstkollision und LOD

**Masse aus Dichte.** NDC kennt das Volumen jeder Hülle und kann die Masse daher korrekt berechnen:
`mass = hull volume × group density`, optional normalisiert, sodass der ganze Charakter
einer Ziel-Gesamtmasse entspricht. Das beseitigt die älteste Handabstimmungs-Arbeit bei Unity-
Ragdolls. Ein einzelnes Rigidbody erhält die Summe der Volumina der Hüllen, die es besitzt.

**Selbstkollision.** `Ignore` (alle Paare), `Adjacent` (gleicher Abschnitt oder Vorfahre und
Nachkomme) oder `On`. Dass Ragdoll-Knochen miteinander kollidieren, ist eine häufige Ursache für
Jitter, und `Adjacent` ist die übliche Antwort. Ab 200 Collidern wird der Schritt mit
einer Warnung übersprungen, statt `Awake` zu blockieren.

**LOD.** `Disable` schaltet Collider ab einer bestimmten Entfernung ab; `Reduce` behält nur die
größte Hülle pro Abschnitt. Die Prüfung läuft in jedem vierten Frame.

**Rigidbody.** Unity liefert Kollisions-Callbacks nur an das GameObject, dem das
`Rigidbody` gehört. Bei einem Ragdoll hat jeder Knochen bereits eines. Für alles andere aktiviere
**Rigidbody automatisch hinzufügen**, und NDC erstellt ein kinematisches am Objekt der Komponente.

<a id="sec-16"></a>
## 16. Lokalisierung

Das Fenster, der Inspector, die Zustandsmeldungen und die Menübeschriftungen sind in
**15 Sprachen** lokalisiert:

`en` (integriert) · `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- Englisch ist in die Assembly eingebaut und ist der Fallback für jeden fehlenden Schlüssel, sodass eine
  teilweise übersetzte Sprache sich verschlechtert, statt zu brechen.
- Jede andere Sprache ist reine Daten in `Locale/<code>/strings.json` — eine hinzuzufügen
  erfordert kein Neukompilieren.
- Arabisch und Hebräisch sind vollständig rechts-nach-links: Das Layout spiegelt, statt sich auf
  `style.direction` zu verlassen, dessen UI-Toolkit-Unterstützung unvollständig und versionsabhängig ist.
- Ändere die Sprache im Tab **Einstellungen**. Fenster und Menü aktualisieren sich
  sofort, ohne Domain Reload.
- Der Tab Einstellungen zeigt auch den aufgelösten Locale-Pfad und wie viele Sprachen
  gefunden wurden, sodass ein Verpackungsfehler sichtbar statt still ist.

<a id="sec-17"></a>
## 17. Verzeichnisstruktur

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
## 18. Deinstallation

1. Entferne die Komponente **Dynamic Collision** aus deinen Prefabs und Szenen.
2. Lösche `Assets/NekoDynamicCollision`.

Die gebackenen Assets liegen unter `Baked/` im Plugin-Ordner und verschwinden mit ihm. Nichts
wird außerhalb des Plugin-Ordners geschrieben, und die Laufzeit enthält keinen Code, der von der
Editor-Hälfte abhängt.

<a id="sec-19"></a>
## 19. Fehlerbehebung und FAQ

**Nichts kollidiert, und keine Ereignisse werden ausgelöst.**
Es gibt kein `Rigidbody` in der Elternkette. Unity sendet Kollisions-Callbacks nur an
das Objekt, dem das Rigidbody gehört. Aktiviere **Rigidbody automatisch hinzufügen**, oder füge selbst eines hinzu.

**Die Hüllen stimmen nicht mit dem überein, was ich sehe.**
Das Gizmo zeichnet standardmäßig die **Bind-Pose** — das ist, was gebacken wurde. Schalte
**Bind-Pose** im Tab Gizmo aus, um sie in der aktuellen Pose zu sehen.

**„Hülle hat N Vertices, über dem PhysX-Limit von 255."**
Unity ignoriert eine konvexe Hülle über dem Limit stillschweigend. Senke die Genauigkeit um eine Stufe; die
Zustandsprüfung bietet genau das als Ein-Klick-Korrektur an.

**Treffer werden stellenweise verpasst.**
Prüfe zuerst den Abdeckungsprozentsatz. Unter ~95 % bedeutet echte Löcher. Prüfe dann die
**Nahtüberlappung** für die Genauigkeitsstufe, auf der du bist.

**Ein Anmalstrich hat eine Lücke zwischen zwei Regionen hinterlassen.**
Das ist das Nahtproblem. Erhöhe die Genauigkeit (was die Nahtüberlappung verringert) oder male ein
wenig über die Grenze hinaus. Die Deduplizierung im selben Frame verhindert bereits doppelte Ereignisse
durch die Überlappung.

**Ereignisse werden für einen Treffer zweimal ausgelöst.**
Zwei verschiedene Abschnitte wurden im selben Frame getroffen, was legitim ist. Wenn du wirklich
ein Ereignis pro Objektpaar willst, filtere in deinem Handler nach `elementIndex`.

**Ich habe angemalt, aber nach dem Backen hat sich nichts geändert.**
Die Labels werden ignoriert, wenn die Dreieckszahl nicht zum Quell-Mesh passt —
meist nach einem Re-Import oder einer Topologieänderung. Male erneut an, oder backe zuerst, damit das Label-
Asset in der richtigen Größe erstellt wird.

**Der Pinsel startet nicht.**
Der Play-Modus läuft, oder das Animationsfenster zeigt eine Vorschau. Beides wird als
expliziter Grund im Tab Anmalen angezeigt.

**Meine alte Pinselarbeit ist nach dem Update verschwunden.**
Das sollte nicht passieren: Masken, die vor dem Anmal-Flag erstellt wurden, werden migriert, und jedes
Nicht-Null-Label gilt als angemalt. Wurde eine Maske geleert, male erneut an und backe erneut.

**Sind die Laufzeitkosten wirklich null?**
Im eingeschwungenen Zustand ja: Hüllen sind Assets, Transformationen werden von der Hierarchie mitgeführt, und
es gibt überhaupt keine Mesh-Arbeit. Die einzige Arbeit pro Frame ist die optionale Trigger-Abfrage
und die LOD-Distanzprüfung.

**Kann ich zwei Dynamic-Collision-Komponenten an einem Objekt haben?**
Nein, und das ist absichtlich blockiert. Zwei Komponenten würden doppelte Hüllen über denselben
Flächen erzeugen, die Kontakte verdoppeln und die Ereignisse verdoppeln. Partitioniere stattdessen mit Abschnitten und
Materialgruppen.

<a id="sec-20"></a>
## 20. Kontakt

NekoAndreeva — siehe `package.json` für die Repository-URL.

---

<a id="sec-appA"></a>
## 21. Gewöhnliche Nutzung und RASCAL-Parität

### 21.1 NDC als System-Collider

Die Designregel lautet: Wer `Collider` und `Rigidbody` kennt, kennt bereits NDC, denn NDC *erzeugt* gewöhnliche Collider: `MeshCollider` auf versteckten Kindern, ein `Rigidbody` am Objekt, Standardmeldungen, gewöhnliche Layer und Physikmaterialien. `Physics.Raycast` und `Physics.OverlapSphere` brauchen überhaupt keine Änderungen.

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // hinzufügen + bauen
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // eine Standard-Unity-Nachricht
void OnTriggerStay(Collider other) { }     // eine Standard-Unity-Nachricht
```

| Aufruf | Bedeutung |
|---|---|
| `Find(go)` | Die Komponente, am Objekt oder an einem Elternobjekt |
| `Attach(go, generateNow)` | Die Komponente hinzufügen und bauen |
| `Build(go)` / `Rebuild(go)` | Aus dem gebackenen Set bauen oder zur Laufzeit erzeugen |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | Zustand, alle Hüllen auf einmal |
| `SetTrigger(go, v)` | `Collider.isTrigger` für jede Hülle |
| `SetMaterial(go, pm)` | Sofort; überlebt keinen Neuaufbau |
| `GetColliders(go)` / `ForEachCollider(go, a)` | Die Hüllen, als gewöhnliche `Collider` |
| `SetReceiver(go, t)` | Standardmeldungen zusätzlich an `t` senden |

**Nur die Zonierung ist zusätzlich.** Zonen, angemalte Materialien, LOD, Ereignisse, Masse aus Dichte und die Zustandsprüfung brauchen die NDC-API — das sind die Dinge, die ein System-Collider nicht kann.

**Kein Back-Schritt.** Aktiviere **Advanced ▸ Build at startup when nothing is baked** (oder rufe `Attach` auf). Collider werden pro Knochen aus dem Mesh bei `Awake` gebaut — eine konvexe Hülle pro Knochen, wie bei RASCALs Standard. Backen bleibt der Weg zu Zonen, Zerlegung, Abdeckung und Genauigkeit.

**Nachrichten, die dein Skript erreichen.** Unity liefert `OnCollision*` an das Objekt mit dem `Rigidbody`. Sitzt dein Skript woanders (eine Charakterwurzel, während das Rigidbody an einem Knochen hängt), setze `Advanced ▸ Also send OnCollision*/OnTrigger* to` — die Nachrichten werden dann mit `SendMessage` weitergeleitet, was auf Frames ohne Kollision nichts kostet.

### 21.2 Live-Update — die Fähigkeit, die Backen nicht ersetzen kann

Eine an einen Knochen geklebte gebackene Hülle ist in der Bind-Pose exakt und danach starr. Bei starker Verformung — eine Hocke, ein gequetschtes Glied, straff gezogene Kleidung — meldet die Hülle die Oberfläche falsch. Das Live-Update baut die Hülle aus der **aktuellen** Skinning-Pose neu auf.

Schalte es ein mit **Advanced ▸ Live update** oder `Dyc_Collision.EnableLiveUpdate(go)`.

| Einstellung | Standard | Bedeutung |
|---|---|---|
| `liveUpdate` | aus | Hüllen aus der aktuellen Pose neu bauen |
| `liveUpdateContinuous` | an | Weiterlaufen oder einen Durchlauf auf Anfrage |
| `idleCpuBudgetMs` | 0.2 | Budget, während das Mesh sich kaum bewegt |
| `activeCpuBudgetMs` | 1.0 | Budget, während es sich schnell bewegt |
| `meshUpdateThreshold` | 0.02 | Durchlauf unterhalb dieser Bewegung (Meter) überspringen |
| `maxColliderTriangles` | 5000 | Obergrenze pro Collider, damit ein schwerer Knochen das Budget nicht auffrisst |

Das Budget wird danach gewählt, wie stark sich das Mesh tatsächlich bewegt hat, also wird ein stehender Charakter mit dem Idle-Satz belastet und ein laufender mit dem aktiven Satz. Arbeit, die nicht passt, wird auf den nächsten Frame verschoben, und `OnUpdateYield` / `OnPassComplete` melden die verstrichenen Millisekunden.

**Es baut nicht jeden Frame alles neu.** Drei Mechanismen halten die Kosten vorhersagbar:

1. **Inkrementell.** Das Zentrum jedes Clusters wird mit dem vorherigen Durchlauf verglichen, und nur tatsächlich bewegte Cluster werden neu gebaut. Ein an einem Anker hängender Softbody hat einen zitternden Saum und eine fast stillstehende Mitte — die Mitte kostet nichts.
2. **Nach Priorität geordnet.** Die Warteschlange ist danach sortiert, wie weit sich jeder Cluster bewegt hat. Geht das Budget aus, geht es bei den ruhigsten Clustern aus — denen, bei denen die Ungenauigkeit am wenigsten sichtbar ist. Ohne das würde das Budget für das ausgegeben, was zufällig zuerst in der Liste steht.
3. **Nach der Uhr budgetiert**, nicht nach Clusterzahl: Die Kosten pro Frame wachsen nicht mit der Zahl der Cluster des Körpers.

`LastDirtyCount` und `LastBuiltCount` melden, was der letzte Durchlauf tatsächlich getan hat — der ehrlichste Weg, die Ersparnis zu sehen.

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"pass done in {ms:F2} ms");
live.StopAfterPass();          // den aktuellen Durchlauf beenden, dann stoppen
live.UpdateNow();              // ein voller Durchlauf, außerhalb des Budgets
```

**Voraussetzungen.** Hüllen brauchen `sourceVertices`, das vom Backen geschrieben wird; backe einen älteren Charakter neu, um das Live-Update zu aktivieren. Die Laufzeitkosten sind real — es ist die einzige Funktion, die „null Kosten pro Frame" widerspricht, und genau deshalb ist sie standardmäßig aus.

### 21.3 Überschreibungen pro Knochen

`Dyc_BoneProperties` kommt an den Knochen selbst (Add Component ▸ NekoWorks ▸ Dynamic Collision ▸ Bone Properties):

| Feld | Wirkung |
|---|---|
| `overrideMaterial` + `physicsMaterial` | Die Hüllen dieses Knochens verwenden dieses Material |
| `overrideConvex` + `convex` | Hülle statt Oberfläche (oder umgekehrt) für diesen Knochen |
| `overrideWeightThreshold` + `boneWeightThreshold` | Gewichtsschwelle pro Knochen |
| `exclude` | Keine Collider für diesen Knochen |

Ihn am Knochen zu befestigen bedeutet, dass er Umbenennungen überlebt — er hält eine Referenz, keinen Pfad.

### 21.4 Materialien nach Quellmaterial

`Advanced ▸ Materials by source material` ordnet einem Quell-`Material` ein `PhysicMaterial` zu. Hüllen werden dem Submesh zugeordnet, aus dem sie überwiegend stammen, beim Backen in `Dyc_BakedSet.sourceMaterials` aufgelöst. Priorität, höchste zuerst:

1. `Dyc_BoneProperties.physicsMaterial`;
2. die Materialzuordnung für das Quellmaterial der Hülle;
3. das Material der angemalten Gruppe.

### 21.5 Vertex-Ausschlusskarte

`Advanced ▸ Exclusion map` liest einen Texturkanal (R/G/B/A, mit einer Schwelle) und schließt Vertices aus, deren Kanalwert auf oder über der Schwelle liegt. Der Pinsel markiert *Flächen*, die Karte markiert *Vertices* — sie ergänzen einander. Das Mesh braucht UVs, die Textur braucht **Read/Write Enabled**, und ein Dreieck wird nur ausgeschlossen, wenn alle drei seiner Vertices es sind.

### 21.6 Skeleton neu ausrichten

`Advanced ▸ Attach hulls to another skeleton` baut die Hüllen aus diesem Mesh, hängt sie aber an gleichnamige Knochen einer anderen Wurzel — der `RetargetSkeleton`-Fall, für Puppet Master und ähnliche Setups. Knochen werden über den relativen Pfad aufgelöst; eine Hülle ohne gleichnamiges Gegenstück bleibt an ihrem eigenen Skeleton, und der Back-Bericht sagt, wie viele.

### 21.7 Soft-Modus — knochenlos, code- oder solvergetrieben

Der Soft-Modus (`Mode → Soft`) ist **kein** Skinning-Rig. Er ist für ein Mesh **ohne Skeleton**, dessen Form von einem Solver oder von Code erzeugt wird — NekoDynamicSoftbody und Ähnliches. Im Soft-Modus liest nichts Knochen; die Mesh-Geometrie wird unverändert übernommen.

**Was das Backen erzeugt.** Das Mesh wird in nummerierte räumliche Cluster aufgeteilt. Jede Hülle wird *relativ zu ihrem Clusterzentrum* gebaut, und das Zentrum wird als Ruhepose des Clusters (`clusterRest`) gespeichert. Das ist es, was einen Frame eine Hülle als ein Stück **verschieben und drehen** lässt.

**Ohne Solver.** Frames werden in ihren Ruheposen erzeugt, also sitzen die Hüllen exakt auf der echten Mesh-Geometrie und bewegen sich mit dem Objekt. Die Form stimmt; es gibt einfach keine Dynamik. Das ist die beabsichtigte Degradierung, kein Fehler — und genau das bedeutet „berechnet die echte Form ohne NDSC".

**Mit Solver.** Der Solver schiebt Frames (`Push` → `Apply`) und übernimmt vollständig, was eine vollständige Simulation ergibt. Ein adressierbarer Push wird nicht vom globalen Poll im selben Frame überschrieben, was relevant wird, sobald mehr als ein Körper existiert.

**Das Live-Update funktioniert auch ohne Skeleton.** `Dyc_LiveUpdate` liest die CPU-Vertices eines `MeshFilter` unverändert, also treibt jeder Code, der das Mesh verformt — ein Softbody-Solver, ein prozedurales Skript, ein eigener Deformer —, präzise Hüllen ohne plugin-spezifischen Kleber an. Eine Hülle braucht `sourceVertices`, das vom Backen geschrieben wird; backe ein älteres Asset neu.

**Die ehrliche Grenze:** Verformung, die nur auf der GPU existiert (ein Vertex-Shader, GPU-Skinning), kann auf der CPU nicht zurückgelesen werden, also sieht das Live-Update sie nicht. Verlege die Verformung auf die CPU oder behalte die gebackenen Hüllen.

### 21.8 Einzelne Hüllen steuern

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`, `HullIndexOf`, `HullCount`, `BakedSet`, `Elements` und `Groups` sind öffentlich, sodass externe Tools einzelne Hüllen ohne Reflexion durchlaufen und steuern können.

---

## Anhang A. Physikmaterial-Voreinstellungen

224 Voreinstellungen, 14 Kategorien. Die Werte sind belegte technische Näherungen, abgebildet auf Unitys Vier-Parameter-Modell. „Körperteile“ und „Gewebe & Organe“ leitet die Schmiede aus einer Gewebemischung ab, statt sie in die Tabelle zu schreiben.

| Kategorie | Anzahl | Voreinstellungen |
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


Jede Voreinstellung trägt außerdem eine **Dichte** in kg/m³ für die automatische Masse, und Voreinstellungen
der Gummi-Familie tragen die `bounceThreshold`, die sie brauchen, um überhaupt zu springen.

<a id="sec-appB"></a>
## Anhang B. Projekt-Physikprüfungen

Die Zustandsprüfung auditiert die `Physics`-Einstellungen des Projekts, denn eine Material-Voreinstellung
kann keine globale Einstellung korrigieren:

| Einstellung | Warum sie wichtig ist |
|---|---|
| `bounceThreshold` | Aufpralle, die langsamer sind als dieser Wert, springen nie. Beim Unity-Standardwert 2 wirkt eine Gummi-Voreinstellung kaputt. Senke ihn auf 0,2–0,5, um elastische Materialien zu nutzen. |
| `defaultSolverVelocityIterations` | Bei 1 flackern oder tunneln Stapel und schnelle Aufpralle. 2–4 ist meist besser, und es ist eine häufige Grundursache für Ragdoll-Jitter. |
| `gravity` | Wenn sie nicht −9.81 ist, ist jede aus −9.81 abgeleitete Masse- und Impulsintuition um denselben Faktor daneben, und Dichte-Voreinstellungen müssen korrigiert werden. |
| `defaultContactOffset` | Ein weiter Kontaktabstand lässt dünne Objekte schweben. |

Das Anwenden der empfohlenen Werte ist eine Ein-Klick-Aktion vom Tab Materialien oder der
Zustandsprüfung aus.
