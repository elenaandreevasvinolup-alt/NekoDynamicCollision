# Neko Dynamic Collision (NDC) — wdrożenie i instrukcja

Zapieczona kolizja oparta na otoczkach wypukłych dla Unity. Cała kosztowna praca odbywa się w
edytorze; środowisko uruchomieniowe tylko wczytuje i kieruje. Postać ze skinningiem albo statyczna siatka
staje się zbiorem otoczek wypukłych na kość i region z **zerowym kosztem na klatkę**,
wbudowanymi zdarzeniami podziału i materiałami fizycznymi na region, malowanymi pędzlem.

Sam komponent nie zawiera wygenerowanego kodu ani atrybutów i nie zależy w czasie
działania od edytorowej części wtyczki. Usuń `Editor/` — a środowisko uruchomieniowe nadal działa.

## Spis treści

- [Część A — Szybkie wdrożenie](#sec-partA)
  - [1. Zapiecz swoją pierwszą postać](#sec-1)
  - [2. Pomaluj regiony materiałów](#sec-2)
  - [3. Ścieżka w 10 minut](#sec-3)
- [Część B — Instrukcja](#sec-partB)
  - [4. Podstawowe pojęcia](#sec-4)
  - [5. Instalacja i wymagania](#sec-5)
  - [6. Okno pieczenia](#sec-6)
  - [7. Opis menu](#sec-7)
  - [8. Precyzja](#sec-8)
  - [9. Pędzel](#sec-9)
  - [10. Grupy materiałów i presety](#sec-10)
  - [11. Diagnostyka pokrycia](#sec-11)
  - [12. Kondycja kolizji](#sec-12)
  - [13. Zdarzenia i integracja](#sec-13)
  - [14. Odpytywanie triggerów](#sec-14)
  - [15. Masa, samokolizja i LOD](#sec-15)
  - [16. Lokalizacja](#sec-16)
  - [17. Struktura katalogów](#sec-17)
  - [18. Odinstalowanie](#sec-18)
  - [19. Rozwiązywanie problemów i FAQ](#sec-19)
  - [20. Kontakt](#sec-20)
- [Dodatek A. Presety materiałów fizycznych](#sec-appA)
- [Dodatek B. Kontrole fizyki projektu](#sec-appB)

---

<a id="sec-partA"></a>
# Część A — Szybkie wdrożenie

<a id="sec-1"></a>
## 1. Zapiecz swoją pierwszą postać

1. Wybierz postać i dodaj komponent **Dynamic Collision**
   (`Add Component → Neko → Dynamic Collision` lub menu `GameObject`).
   Przy tworzeniu komponent znajduje własny `SkinnedMeshRenderer` i tworzy jedną
   domyślną grupę materiałów. Nie musisz niczego wypełniać.
2. Naciśnij **Upiecz** w inspektorze.
3. Otoczki pojawiają się w widoku Scene jako siatka w pozie wiązania. Wybierz postać, aby
   je zobaczyć; gizmo domyślnie podąża za zaznaczeniem.

Pieczenie zapisuje trzy rodzaje zasobów obok sceny:

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

Siatki otoczek są podobiektami `_Baked.asset`, więc podróżują razem z nim i
przetrwają tryb Play i kompilacje. W czasie działania nic nie jest przeliczane ponownie.

<a id="sec-2"></a>
## 2. Pomaluj regiony materiałów

Pędzel jest tym, co umożliwia „jeden obiekt, kilka materiałów fizycznych".

1. Otwórz okno pieczenia (`NekoWorks → NekoDynamicCollision → Open Main Window`).
2. Przejdź do zakładki **Materiały** i dodaj grupę dla każdego regionu — na przykład
   `Hard` i `Soft`. Wybierz preset dla każdej.
3. Przejdź do zakładki **Malowanie**, wybierz grupę do malowania, naciśnij **Rozpocznij malowanie**.
4. W widoku Scene przeciągnij po ścianach, które chcesz przypisać do tej grupy. Pomalowane
   trójkąty są natychmiast wypełniane kolorem grupy.
5. Zapiecz ponownie. Każdy region otrzymuje teraz własne otoczki wypukłe z własnym materiałem fizycznym.

Malować można tylko przy zatrzymanym trybie Play i gdy okno Animation nie
odtwarza podglądu. Sama poza nie ma znaczenia — etykiety są przechowywane według indeksu trójkąta,
więc bieżąca poza jest nieistotna.

<a id="sec-3"></a>
## 3. Ścieżka w 10 minut

| Minuta | Co zrobić |
|---|---|
| 0–2 | Dodaj komponent, naciśnij Upiecz, spójrz na gizmo. |
| 2–4 | Otwórz zakładkę **Zdrowie**, napraw wszystko oznaczone na czerwono. |
| 4–6 | Otwórz zakładkę **Pieczenie**, spójrz na wartość pokrycia. Poniżej ~95 % — zwiększ precyzję. |
| 6–9 | Dodaj grupę materiałów dla każdego regionu, pomaluj ją, zapiecz ponownie. |
| 9–10 | Ustaw warstwę interakcji na `Bullet`, podłącz nazwę zdarzenia, przetestuj w trybie Play. |

---

<a id="sec-partB"></a>
# Część B — Instrukcja

<a id="sec-4"></a>
## 4. Podstawowe pojęcia

### Otoczki wypukłe, a nie zupa z trójkątów

Dynamiczny (niekinematyczny) `Rigidbody` nie może używać niewypukłego `MeshCollider` — to
ograniczenie PhysX, a nie Unity. Dlatego każdy kształt kolizji dla poruszającego się ciała
musi być wypukły. NDC zapieka **otoczki wypukłe** i przekazuje Unity samą otoczkę, a nie
surowy podzbiór trójkątów, dlatego liczba wierzchołków nigdy nie osiąga
limitu PhysX wynoszącego 255.

### Dlaczego otoczki sztywne względem kości wystarczają

W pozie wiązania `bone.localToWorldMatrix · bindposes[i] = I`. Skinning wierzchołka
obciążonego w 100 % na jedną kość daje więc dokładnie siatkę w pozie wiązania. Innymi
słowy: **otoczka zapieczona w lokalnej przestrzeni kości jest bit w bit tym, co
dałoby przeliczenie w każdej klatce** dla sztywno obciążonych wierzchołków.

Różnią się tylko wierzchołki z mieszanymi wagami — te, które przechodzą przez staw. Są one pokrywane
przez sąsiednie otoczki, które z założenia się nakładają. Dlatego NDC może być darmowe w
czasie działania i jednocześnie dokładne tam, gdzie to ważne.

### Klastrowanie przestrzenne, a nie dzielenie według kolejności indeksów

NDC grupuje trójkąty według położenia (inicjalizacja najdalszymi punktami plus wzrost w stylu
Dijkstry po sąsiedztwie krawędzi). Alternatywa — branie trójkątów w kolejności indeksów — daje
otoczki, które nakładają się na siebie i obejmują puste powietrze, a im więcej otoczek zażądasz, tym gorzej.

### Elementy i grupy materiałów

Dwie niezależne osie:

- **Element** — *gdzie*. Jedna kość (tryb Skin) lub cała siatka (tryb Mesh).
  Element potomny zawsze wygrywa z przodkiem, więc zdarzenia nigdy nie są wysyłane dwa razy.
- **Grupa materiałów** — *co*. Zbiór ścian współdzielących jeden materiał fizyczny i jedną
  gęstość, utworzony przez malowanie.

Otoczka to przecięcie jednego elementu i jednej grupy materiałów. Jeśli grupa nie ma
pomalowanych ścian wewnątrz danej kości, dla tej pary nie powstaje żadna otoczka.

### Co robi środowisko uruchomieniowe

1. Wczytuje zapieczony zestaw.
2. Tworzy po jednym ukrytym obiekcie potomnym na każdą otoczkę pod właściwą kością, z jednostkowym
   transformem, i przypisuje wypukły `MeshCollider`.
3. Buduje tabelę wyszukiwania `Collider → hull`.
4. Umieszcza `Dyc_Relay` na każdym `Rigidbody`, do którego należy otoczka.
5. Konfiguruje warstwy, samokolizję i masę.

Potem się zatrzymuje. Nie ma żadnej pracy w `Update` poza opcjonalnym odpytywaniem triggerów i
sprawdzaniem odległości dla LOD.

<a id="sec-5"></a>
## 5. Instalacja i wymagania

- Unity 2022.3 lub nowsze.
- Skopiuj `Assets/NekoDynamicCollision` do swojego projektu. Nie ma nic do
  skonfigurowania; zestawy są ograniczone definicjami zestawów.
- Dwa zestawy:
  - `Neko.DynamicCollision.Runtime` — komponent, przekaźnik, odpytywanie triggerów, struktury
    zdarzeń i fasada integracji. Nigdy nie odwołuje się do `UnityEditor`.
  - `Neko.DynamicCollision.Editor` — pieczenie, matematyka otoczek, klastrowanie, pędzel,
    gizmo, kontrola kondycji, presety i okno. Wyłącznie platforma edytora.

<a id="sec-6"></a>
## 6. Okno pieczenia

`NekoWorks → NekoDynamicCollision → Open Main Window` (`Cmd/Ctrl+Shift+D`).

| Zakładka | Co robi |
|---|---|
| **Pieczenie** | Źródło, tryb, precyzja, pieczenie/czyszczenie/odbudowa, statystyki, pokrycie |
| **Malowanie** | Lista grup z liczbą pomalowanych trójkątów, ustawienia pędzla, reset pozy |
| **Gizmo** | Co rysuje widok Scene i w jaki sposób |
| **Części** | Lista elementów — kość, dołącz potomne, nazwa zdarzenia, mnożnik obrażeń |
| **Materiały** | Grupy materiałów, presety, tabela zachowania par, audyt fizyki projektu |
| **Zdrowie** | Ocena na 100, każdy problem, poprawki jednym kliknięciem |
| **Ustawienia** | Język, diagnostyka, otwórz folder pieczenia, reset preferencji |

<a id="sec-7"></a>
## 7. Opis menu

Wszystko znajduje się w jednym slocie najwyższego poziomu, więc instalowanie kolejnych wtyczek NekoWorks
nigdy nie poszerza paska menu.

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

Podpisy menu są lokalizowane przy wczytywaniu i przy zmianie języka; statyczne angielskie
ciągi w atrybutach są wartością zapasową, jeśli wewnętrzne API menu Unity nie jest
dostępne w Twojej wersji.

<a id="sec-8"></a>
## 8. Precyzja

Jeden parametr, cztery stopnie. Wewnętrznie rozwija się do czterech wartości:

| Precyzja | Trójkątów na otoczkę | Otoczek na element | Nakładanie szwu | Próg wagi |
|---|---|---|---|---|
| Coarse | 120 | 1 | 4.0 mm | 0.35 |
| Normal | 250 | 2 | 3.0 mm | 0.25 |
| Fine | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **Trójkątów na otoczkę** ogranicza, ile geometrii może pochłonąć jedna otoczka. Więcej trójkątów
  na otoczkę oznacza mniej, większych i luźniejszych otoczek.
- **Otoczek na element** to docelowa liczba klastrów na element. Więcej klastrów
  oznacza ściślejsze dopasowanie i więcej koliderów.
- **Nakładanie szwu** rozdmuchuje każdą otoczkę na zewnątrz, dzięki czemu sąsiednie regiony nakładają się,
  zamiast pozostawiać szczelinę. Nakładanie jest bezpieczne (trafienie nigdy nie jest pomijane, a deduplikacja
  w tej samej klatce zatrzymuje podwójne zdarzenia); szczelina nie jest.
- **Próg wagi** odrzuca wierzchołki, których waga dla dominującej kości jest poniżej
  tej wartości. Wyższy oznacza ściślejszą, bardziej „sztywno poprawną" otoczkę.

Liczba wierzchołków otoczki nigdy nie przekracza liczby unikalnych wierzchołków klastra, która
jest ograniczona do 250 — bezpiecznie poniżej limitu PhysX wynoszącego 255. Kontrola kondycji oznacza każdą
otoczkę powyżej 255 na czerwono.

### 8.1 Tryb wypukły dekomponuje — poza CC i wbudowanym złożonym koliderem

W trybie **wypukłym** wklęsła siatka jest cięta na wypukłe części wzdłuż swoich wklęsłości. To ten sam pomysł co wbudowany złożony kolider i narzędzia w stylu V-HACD (CC): weź dowolną siatkę i uzyskaj wypukłe części. NDC zachowuje szerokość i podnosi sufit:

| | Złożony kolider / CC | NDC |
|---|---|---|
| Dowolna siatka | tak | tak |
| Dobrze zoptymalizowany | tak | tak — zadanie pieczenia w tle, postęp, anulowanie, budżety na kość |
| Części respektują stawy | **nie** — czysto geometrycznie, bark może połknąć ramię | **tak** — części niosą etykiety kości z pola wag |
| Użyteczny na poruszającym się ciele | **nie** — PhysX odrzuca niewypukły kolider na niekinematycznym `Rigidbody` | **tak** — wynikiem jest otoczka wypukła |
| Koszt w czasie działania | pieczenie kolidera przy wczytaniu | zero — zapieczone w assetach |
| Wariant zapasowy | — | klastrowanie przestrzenne, więc zdegenerowana siatka i tak dostaje kolider |

Okno Expert informuje, czy części pochodzą z **dekompozycji** (cięcie wzdłuż wklęsłości), czy z **klastrowania przestrzennego** (wariant zapasowy), więc różnica jest liczbą, a nie zgadywaniem.

**Jak drobne jest cięcie**, decyduje jeden suwak — **Decomposition detail** — a rozmiar woksela w milimetrach widać w polu liczbowym tuż pod nim. To dwa widoki *jednej* liczby, więc zawsze się zgadzają: przeciągnij suwak, a pole podąży; wpisz w polu, a suwak się przesunie. Nie ma drugiego ustawienia do synchronizowania.

- **Po lewej** — gruby woksel: mniej, większych części. Najtaniej i zwykle wystarcza dla rekwizytu.
- **Po prawej** — najdrobniejszy woksel. Części podążają za powierzchnią, więc koszt **dorównuje trybowi niewypukłemu**: drobniej nie ma już nic do zyskania, tylko koszt.

Tabela precyzji powyżej dotyczy wtedy *dopasowania klastrów* — jak ciasno układa się każda część — a nie tego, ile koliderów otrzymasz.

Ten sam suwak występuje w obu trybach: dekompozycja wypukła i uproszczenie niewypukłe to dwa sposoby odpowiedzi na to samo pytanie: *ile szczegółu chcę*.

### 8.2 Szczegół niewypukły to jeden suwak

Przełącz **Collider shape → Non-convex surface**, a pojawi się suwak **Surface detail**.

| Suwak | Wynik |
|---|---|
| Skrajnie w lewo | Silnie uproszczona powierzchnia — mało trójkątów, widoczne ścięcia |
| Środek | Dobry kompromis: kształt czyta się poprawnie, kolider pozostaje tani |
| **Skrajnie w prawo** | **Żadnego uproszczenia** — powierzchnia jest brana z siatki tak jak jest |

Dlaczego suwak, a nie liczba trójkątów: „ile trójkątów na część" nie da się rozsądnie wybrać bez wiedzy, ile ma ich siatka — 500 jest grube dla torsu i precyzyjne dla palca. Suwak odpowiada na jedyne pytanie, które użytkownik naprawdę może zadać: *jak bardzo zależy mi na dokładnym kształcie*. Skrajnie prawe położenie to nie „prawie dokładnie", lecz dokładnie: uproszczenie jest całkowicie wyłączone.

Małe klastry nigdy nie są upraszczane, niezależnie od suwaka — ściągnięcie cienkiego palca na grubą siatkę sprowadza go do niczego, a pusty kolider jest gorszy niż drogi.

Zarówno tryb Skin, jak i Mesh używają tego samego suwaka.

<a id="sec-9"></a>
## 9. Pędzel

Pędzel nie „wyklucza" ścian. On je **oznacza**, a oznaczenia napędzają pieczenie.

| Akcja | Efekt |
|---|---|
| Przeciąganie lewym przyciskiem | Przypisz bieżącą grupę do trójkątów pod kursorem |
| Shift + przeciąganie | Wymaż z powrotem do grupy 0 i usuń flagę malowania |
| Kółko myszy | Promień pędzla |
| Przełącznik `X` (okno) | Odbijaj lustrzanie każde pociągnięcie względem lokalnego X = 0 obiektu |

Ustawienia: promień, X-Ray (ignoruj ściany odwrócone), lustro na X.

Wymagania egzekwowane jawnym komunikatem, a nie cichą awarią:

1. Tryb Play musi być zatrzymany.
2. Okno Animation nie może odtwarzać podglądu.

Rig **nie** musi być w pozie wiązania. Pędzel rzuca promień na siatkę
w jej **bieżącej** pozie; ponieważ skinning nigdy nie zmienia topologii, indeksy trójkątów
odwzorowują się jeden do jednego, a etykiety pozostają poprawne.

Jeśli mimo wszystko chcesz ustawić rig w pozie wiązania, przycisk **Przywróć bind pose** wylicza
lokalne transformy z `bindposes[i].inverse` i zapisuje je z powrotem, z obsługą cofania.

<a id="sec-10"></a>
## 10. Grupy materiałów i presety

Każda grupa niesie `PhysicMaterial`, gęstość w kg/m³, opcjonalną nazwę zdarzenia
i mnożnik obrażeń.

**Presety.** 224 presety w 14 kategoriach (Metal, Ceramic, Plastic, Glass, Wood,
Stone & Concrete, Rubber, Fabric & Leather, Body parts, Tissue & organs, Organic, Ice,
Food, Other) — patrz [Dodatek A](#appendix-a-physic-material-presets).
Zastosowanie presetu tworzy prawdziwy zasób `.physicMaterial` w `Baked/<Scene>/Materials/`,
więc można się do niego odwoływać, porównywać go, wrzucić do Addressables i przekazać grafikowi.

**Kuźnia materiałów.** Wiersz tabeli odpowiada „z czego to jest zrobione”; kuźnia odpowiada
„czym to jest teraz”. Mnoży preset bazowy przez **stan powierzchni** (Dry, Wet, Oiled,
Bloody, Sweaty, Icy, Frozen, Dusty, Rough, Polished, Rusted, Worn, Charred, Clothed,
Armoured). `Dry` jest tożsamością, więc nigdy nie ma `steel_dry` obok `steel`. Tarcie jest
mnożone jednocześnie na współczynniku statycznym i dynamicznym.

**Części ciała są wyprowadzane, nie wpisywane.** „Części ciała” i „Tkanki i narządy” nie są
wierszami tabeli: liczy się je z mieszanki tkanek — gęstość jest sumowana, miękkość
sumowana plus człon poduszki, a tarcie i odbicie wynikają z miękkości. „Pierś” to 80%
tłuszczu + 10% mięśnia + 10% skóry; „czaszka” to 95% kości + 5% skóry.

**Generowanie zasobów.** *Wygeneruj zasoby wariantów* zapisuje po jednym
`.physicMaterial` na stan do `Baked/<Scene>/Materials/`.

**Strategia łączenia.** Cała biblioteka używa `Multiply` dla tarcia i `Maximum`
dla odbicia. Priorytet łączenia w Unity to
`Average < Minimum < Multiply < Maximum`, więc przy tej strategii każda śliska powierzchnia
dominuje w wyniku tarcia, a każdy sprężysty materiał dominuje w wyniku odbicia —
co jest tym, czego ludzie intuicyjnie oczekują.

**Tabela zachowania par.** Zakładka Materiały wylicza każdą parę grup w Twoim
projekcie, używając rzeczywistych reguł priorytetu Unity, i pokazuje wartość, która faktycznie
zostanie zastosowana, plus prosty werdykt („przyczepne / bez odbicia"). To najszybszy sposób,
aby odpowiedzieć na pytanie „dlaczego mój lód nie jest śliski".

**Automatyczne przypisanie po nazwie.** Wypełnia każdą grupę z presetów, dopasowując nazwę grupy
do angielskich, chińskich i rosyjskich słów kluczowych.

**Szczere ograniczenie.** `PhysicMaterial` ma cztery liczby i dwa tryby łączenia. Nie
może wyrazić tarcia tocznego, tarcia anizotropowego, lepkości, odkształceń
plastycznych, temperatury ani zużycia. „Rzeczywiste parametry" oznaczają tutaj udokumentowaną
tabelę wartości i użyteczne presety — a nie symulację fizyczną.

<a id="sec-11"></a>
## 11. Diagnostyka pokrycia

Zakładka Pieczenie odpowiada na pytanie, które zwykle jest zgadywaniem: **które trójkąty nie mają
w ogóle żadnej otoczki?**

Bierze źródłową siatkę w pozie wiązania i testuje środek ciężkości każdego trójkąta względem
płaszczyzn każdej otoczki, raportując:

- ogólny procent i pasek postępu;
- rozbicie na elementy;
- listę niepokrytych trójkątów, rysowaną w widoku Scene na czerwono
  (**Pokaż niepokryte ściany**).

Traktuj wartość poniżej ~95 % jako problem: zwiększ precyzję lub sprawdź, czy kości elementów
faktycznie pokrywają cały szkielet.

<a id="sec-12"></a>
## 12. Kondycja kolizji

Ocena na 100 z listą wszystkich problemów i, gdzie to możliwe, poprawką jednym kliknięciem.

Kontrole obejmują: nic nie zostało zapieczone; otoczka przekracza limit wierzchołków PhysX; otoczki blisko
limitu; zdegenerowane klastry; trójkąty nienależące do żadnego elementu; źródłowa siatka
zmieniona od ostatniego pieczenia; grupy materiałów bez materiału, bez otoczek lub zbyt
rozfragmentowanymi otoczkami; rola Hitbox/Trigger bez warstw interakcji; brak `Rigidbody` w
łańcuchu rodziców; masa rigidbody znacznie za mała lub za duża; samokolizja ragdolla
w pełni włączona; wysłane zdarzenia bez odbiorcy; oraz kontrole fizyki na poziomie projektu z
[Dodatku B](#appendix-b-project-physics-checks).

<a id="sec-13"></a>
## 13. Zdarzenia i integracja

Każde zdarzenie niesie pełny kontekst, więc nigdy więcej nie musisz niczego wyszukiwać:

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

Trzy sposoby jego użycia:

1. **UnityEvent** — `onEvent` na komponencie, dla odbiorców rejestrowanych w kodzie.
2. **Rejestr tekstowy** — nadaj elementowi lub grupie nazwę zdarzenia i nasłuchuj przez
   `Dyc_Events.Register("Hit.Head", handler)`. Błędnie zapisane nazwy nie zgłaszają błędu, ale
   kontrola kondycji raportuje wysłania, których nikt nie odebrał.
3. **Statyczna fasada** — `Dyc_Api` dla zewnętrznych narzędzi:

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

Mnożniki obrażeń znajdują się w elemencie i w grupie i są mnożone przez siebie —
głowa ×4 to jedna liczba, a nie warstwa kodu klejącego.

**Deduplikacja.** Nakładanie szwu oznacza, że dwie sąsiednie grupy mogą w tej samej
klatce dotknąć tego samego obcego kolidera. NDC wysyła co najwyżej jedno zdarzenie na
`(element, other collider)` na klatkę, więc podzielone materiały nie dublują zdarzeń.

<a id="sec-14"></a>
## 14. Odpytywanie triggerów

Unity dostarcza wywołania zwrotne triggerów **na parę Rigidbody**, więc pojedynczy ragdoll to jedna
para rigidbody i warstwa fizyki po prostu nie może powiedzieć, która kość weszła do
objętości. Nadanie każdej kości własnego `Rigidbody` zniszczyłoby obietnicę zerowego kosztu.

Dlatego podzielone triggery są zamiast tego próbkowane:

- Każdy element jest testowany przez `Physics.OverlapBoxNonAlloc` po granicach w przestrzeni
  świata jego koliderów.
- Uwzględniane są tylko rzeczywiste triggery i nigdy własne kolidery.
- Elementy są przetwarzane porcjami: `elements / frames-per-pass` na klatkę.
- Wejście i wyjście są porównywane z poprzednim przebiegiem dla każdego elementu.

**Semantyka do zapamiętania:** to próbkowanie, a nie zdarzenie. Bardzo szybki przelot może zostać
pominięty. Zwiększ częstotliwość próbkowania lub użyj **sweep margin**, aby powiększyć obszar zapytania.

<a id="sec-15"></a>
## 15. Masa, samokolizja i LOD

**Masa z gęstości.** NDC zna objętość każdej otoczki, więc może poprawnie obliczyć masę:
`mass = hull volume × group density`, opcjonalnie znormalizowaną tak, aby cała postać
odpowiadała docelowej masie całkowitej. To usuwa najstarszy obowiązek ręcznego strojenia w
ragdollach Unity. Pojedynczy rigidbody otrzymuje sumę objętości należących do niego otoczek.

**Samokolizja.** `Ignore` (wszystkie pary), `Adjacent` (ten sam element lub przodek i
potomek) lub `On`. Zderzanie się kości ragdolla ze sobą to częsta przyczyna
drgań, a `Adjacent` to zwykłe rozwiązanie. Powyżej 200 koliderów krok jest pomijany
z ostrzeżeniem, zamiast blokować `Awake`.

**LOD.** `Disable` wyłącza kolidery powyżej pewnej odległości; `Reduce` zachowuje tylko
największą otoczkę na element. Kontrola działa co czwartą klatkę.

**Rigidbody.** Unity dostarcza wywołania zwrotne kolizji tylko do GameObject, do którego należy
`Rigidbody`. W ragdollu każda kość już go ma. W każdym innym przypadku włącz
**Auto-dodaj Rigidbody**, a NDC utworzy kinematyczny na obiekcie komponentu.

<a id="sec-16"></a>
## 16. Lokalizacja

Okno, inspektor, komunikaty kondycji i podpisy menu są zlokalizowane
na **15 języków**:

`en` (wbudowany) · `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- Angielski jest wbudowany w zestaw i stanowi wartość zapasową dla każdego brakującego klucza, więc
  częściowo przetłumaczony język degraduje się, zamiast się psuć.
- Każdy inny język to czyste dane w `Locale/<code>/strings.json` — dodanie
  kolejnego nie wymaga rekompilacji.
- Arabski i hebrajski są w pełni od prawej do lewej: układ sam się odbija lustrzanie, zamiast opierać się
  na `style.direction`, którego wsparcie w UI Toolkit jest niepełne i zależne od wersji.
- Zmień język w zakładce **Ustawienia**. Okno i menu aktualizują się
  natychmiast, bez przeładowania domeny.
- Zakładka Ustawienia pokazuje też rozwiązaną ścieżkę lokalizacji i ile języków
  znaleziono, więc błąd pakowania jest widoczny, a nie cichy.

<a id="sec-17"></a>
## 17. Struktura katalogów

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
## 18. Odinstalowanie

1. Usuń komponent **Dynamic Collision** ze swoich prefabów i scen.
2. Usuń `Assets/NekoDynamicCollision`.

Zapieczone zasoby znajdują się w `Baked/` w folderze wtyczki i znikają razem z nim. Nic
nie jest zapisywane poza folderem wtyczki, a środowisko uruchomieniowe nie zawiera kodu zależnego
od części edytorowej.

<a id="sec-19"></a>
## 19. Rozwiązywanie problemów i FAQ

**Nic się nie zderza i żadne zdarzenia nie są wywoływane.**
W łańcuchu rodziców nie ma `Rigidbody`. Unity wysyła wywołania zwrotne kolizji tylko do
obiektu, do którego należy rigidbody. Włącz **Auto-dodaj Rigidbody** lub dodaj go samodzielnie.

**Otoczki nie odpowiadają temu, co widzę.**
Gizmo domyślnie rysuje **poz wiązania** — to właśnie zostało zapieczone. Wyłącz
**Bind pose** w zakładce Gizmo, aby zobaczyć je w bieżącej pozie.

**„Otoczka ma N wierzchołków, powyżej limitu PhysX wynoszącego 255."**
Unity po cichu ignoruje otoczkę przekraczającą limit. Zmniejsz precyzję o jeden stopień;
kontrola kondycji oferuje dokładnie to jako poprawkę jednym kliknięciem.

**Trafienia w niektórych miejscach są pomijane.**
Najpierw sprawdź procent pokrycia. Poniżej ~95 % oznacza prawdziwe dziury. Następnie sprawdź
**nakładanie szwu** dla twojego stopnia precyzji.

**Pociągnięcie pędzla pozostawiło szczelinę między dwoma regionami.**
To problem szwu. Zwiększ precyzję (co zmniejsza nakładanie szwu) lub maluj
nieco poza granicę. Deduplikacja w tej samej klatce już zapobiega podwójnym zdarzeniom
wynikającym z nakładania.

**Zdarzenia wywołują się dwa razy dla jednego trafienia.**
W tej samej klatce trafiono dwa różne elementy, co jest uzasadnione. Jeśli naprawdę
chcesz jedno zdarzenie na parę obiektów, filtruj po `elementIndex` w swoim handlerze.

**Malowałem, ale po zapieczeniu nic się nie zmieniło.**
Etykiety są ignorowane, gdy liczba trójkątów nie zgadza się ze źródłową siatką —
zwykle po ponownym imporcie lub zmianie topologii. Pomaluj ponownie albo najpierw zapiecz, aby zasób
etykiet został utworzony w odpowiednim rozmiarze.

**Pędzel nie chce się uruchomić.**
Działa tryb Play lub okno Animation odtwarza podgląd. Obie przyczyny są pokazane jako
jawny powód w zakładce Malowanie.

**Moja stara praca pędzlem zniknęła po aktualizacji.**
Nie powinna: maski utworzone przed istnieniem flagi malowania są migrowane, a każda
niezerowa etykieta jest traktowana jako pomalowana. Jeśli maska została wyczyszczona, pomaluj i zapiecz ponownie.

**Czy koszt działania jest naprawdę zerowy?**
W stanie ustalonym tak: otoczki są zasobami, transformy są śledzone przez hierarchię i
nie ma w ogóle pracy na siatkach. Jedyna praca na klatkę to opcjonalne odpytywanie triggerów
i sprawdzanie odległości dla LOD.

**Czy mogę mieć dwa komponenty Dynamic Collision na jednym obiekcie?**
Nie, i jest to zablokowane celowo. Dwa komponenty utworzyłyby zduplikowane otoczki na
tych samych ścianach, podwoiły kontakty i zdarzenia. Dziel zamiast tego za pomocą elementów i
grup materiałów.

<a id="sec-20"></a>
## 20. Kontakt

NekoAndreeva — adres URL repozytorium znajdziesz w `package.json`.

---

<a id="sec-appA"></a>
## 21. Zwykłe użycie i zgodność z RASCAL

### 21.1 NDC jako kolider systemowy

Zasada projektowa jest taka, że programista znający `Collider` i `Rigidbody` zna już NDC, ponieważ NDC *tworzy* zwykłe kolidery: `MeshCollider` na ukrytych dzieciach, `Rigidbody` na obiekcie, standardowe komunikaty, zwykłe warstwy i materiały fizyczne. `Physics.Raycast` i `Physics.OverlapSphere` nie wymagają żadnych zmian.

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // dodaj + zbuduj
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // standardowy komunikat Unity
void OnTriggerStay(Collider other) { }     // standardowy komunikat Unity
```

| Wywołanie | Znaczenie |
|---|---|
| `Find(go)` | Komponent, na obiekcie lub rodzicu |
| `Attach(go, generateNow)` | Dodaj komponent i zbuduj |
| `Build(go)` / `Rebuild(go)` | Zbuduj z zapieczonego zestawu lub wygeneruj w czasie działania |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | Stan, wszystkie otoczki naraz |
| `SetTrigger(go, v)` | `Collider.isTrigger` dla każdej otoczki |
| `SetMaterial(go, pm)` | Natychmiast; nie przetrwa przebudowy |
| `GetColliders(go)` / `ForEachCollider(go, a)` | Otoczki, jako zwykłe `Collider` |
| `SetReceiver(go, t)` | Wyślij też standardowe komunikaty do `t` |

**Dodatkowe jest tylko strefowanie.** Strefy, pomalowane materiały, LOD, zdarzenia, masa z gęstości i kontrola kondycji wymagają API NDC — to rzeczy, których kolider systemowy nie potrafi.

**Bez etapu pieczenia.** Zaznacz **Advanced ▸ Build at startup when nothing is baked** (lub wywołaj `Attach`). Kolidery są budowane na kość z siatki w `Awake` — jedna otoczka wypukła na kość, jak w domyślnym ustawieniu RASCAL. Pieczenie pozostaje sposobem na strefy, dekompozycję, pokrycie i precyzję.

**Komunikaty docierające do twojego skryptu.** Unity dostarcza `OnCollision*` do obiektu z `Rigidbody`. Jeśli twój skrypt jest gdzie indziej (korzeń postaci, a `Rigidbody` na kości), ustaw `Advanced ▸ Also send OnCollision*/OnTrigger* to` — komunikaty są wtedy przekazywane przez `SendMessage`, co nie kosztuje nic w klatkach bez kolizji.

### 21.2 Aktualizacja na żywo — zdolność, której pieczenie nie zastąpi

Zapieczona otoczka przyklejona do kości jest dokładna w pozie wiązania, a potem sztywna. Przy silnej deformacji — przysiad, ściśnięta kończyna, napięta tkanina — otoczka błędnie opisuje powierzchnię. Aktualizacja na żywo odbudowuje otoczkę z **bieżącej** pozy skinningu.

Włącz ją przez **Advanced ▸ Live update** lub `Dyc_Collision.EnableLiveUpdate(go)`.

| Ustawienie | Domyślnie | Znaczenie |
|---|---|---|
| `liveUpdate` | wył. | Odbuduj otoczki z bieżącej pozy |
| `liveUpdateContinuous` | wł. | Kontynuuj albo wykonaj jeden przebieg na żądanie |
| `idleCpuBudgetMs` | 0.2 | Budżet, gdy siatka ledwie się porusza |
| `activeCpuBudgetMs` | 1.0 | Budżet, gdy porusza się szybko |
| `meshUpdateThreshold` | 0.02 | Pomiń przebieg poniżej tego ruchu (metry) |
| `maxColliderTriangles` | 5000 | Sufit na kolider, by ciężka kość nie zjadła budżetu |

Budżet jest wybierany na podstawie tego, jak bardzo siatka się faktycznie przesunęła, więc stojąca postać płaci stawkę bezczynności, a biegnąca — stawkę aktywną. Praca, która się nie mieści, jest odkładana do następnej klatki, a `OnUpdateYield` / `OnPassComplete` raportują upływające milisekundy.

**Nie odbudowuje wszystkiego co klatkę.** Trzy mechanizmy utrzymują przewidywalny koszt:

1. **Przyrostowo.** Środek każdego klastra jest porównywany z poprzednim przebiegiem i odbudowywane są tylko klastry, które faktycznie się przesunęły. Miękkie ciało wiszące na punkcie zaczepienia ma drżący brzeg i niemal nieruchomy środek — środek nie kosztuje nic.
2. **Według priorytetu.** Kolejka jest sortowana według tego, jak daleko przesunął się każdy klaster. Jeśli budżet się skończy, skończy się na najspokojniejszych klastrach — tych, w których niedokładność jest najmniej widoczna. Bez tego budżet poszedłby na to, co przypadkiem znalazło się pierwsze na liście.
3. **Budżetowany zegarem**, nie liczbą klastrów: koszt na klatkę nie rośnie wraz z liczbą klastrów ciała.

`LastDirtyCount` i `LastBuiltCount` raportują, co faktycznie zrobił ostatni przebieg, co jest najuczciwszym sposobem zobaczenia oszczędności.

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"pass done in {ms:F2} ms");
live.StopAfterPass();          // dokończ bieżący przebieg, potem zatrzymaj
live.UpdateNow();              // jeden pełny przebieg, poza budżetem
```

**Wymagania.** Otoczki potrzebują `sourceVertices`, zapisywanego przez pieczenie; zapiecz ponownie starszą postać, aby włączyć aktualizację na żywo. Koszt w czasie działania jest realny — to jedyna funkcja sprzeczna z „zerowym kosztem na klatkę", i właśnie dlatego jest domyślnie wyłączona.

### 21.3 Nadpisania na kość

`Dyc_BoneProperties` umieszcza się na samej kości (Add Component ▸ NekoWorks ▸ Dynamic Collision ▸ Bone Properties):

| Pole | Działanie |
|---|---|
| `overrideMaterial` + `physicsMaterial` | Otoczki tej kości używają tego materiału |
| `overrideConvex` + `convex` | Otoczka zamiast powierzchni (lub odwrotnie) dla tej kości |
| `overrideWeightThreshold` + `boneWeightThreshold` | Próg wagi na kość |
| `exclude` | Brak koliderów dla tej kości |

Umieszczenie na kości oznacza, że przetrwa zmiany nazw — trzyma referencję, nie ścieżkę.

### 21.4 Materiały według materiału źródłowego

`Advanced ▸ Materials by source material` mapuje źródłowy `Material` na `PhysicMaterial`. Otoczki są przypisywane według podsiatki, z której głównie pochodzą, rozwiązywanej przy pieczeniu do `Dyc_BakedSet.sourceMaterials`. Priorytet, od najwyższego:

1. `Dyc_BoneProperties.physicsMaterial`;
2. powiązanie materiału dla materiału źródłowego otoczki;
3. materiał pomalowanej grupy.

### 21.5 Mapa wykluczania wierzchołków

`Advanced ▸ Exclusion map` odczytuje kanał tekstury (R/G/B/A, z progiem) i wyklucza wierzchołki, których wartość kanału jest równa progowi lub wyższa. Pędzel oznacza *ściany*, mapa oznacza *wierzchołki* — uzupełniają się. Siatka potrzebuje UV, tekstura **Read/Write Enabled**, a trójkąt jest wykluczany tylko wtedy, gdy wykluczone są wszystkie trzy jego wierzchołki.

### 21.6 Przekierowanie szkieletu

`Advanced ▸ Attach hulls to another skeleton` buduje otoczki z tej siatki, ale wiesza je na kościach o tych samych nazwach innego korzenia — przypadek `RetargetSkeleton`, dla Puppet Master i podobnych konfiguracji. Kości są rozwiązywane po ścieżce względnej; otoczka bez odpowiednika o tej samej nazwie zostaje na własnym szkielecie, a raport pieczenia podaje, ile ich było.

### 21.7 Tryb miękki — bez kości, sterowany kodem lub solverem

Tryb miękki (`Mode → Soft`) **nie** jest rigiem ze skinningiem. Jest dla siatki **bez szkieletu**, której kształt tworzy solver lub kod — NekoDynamicSoftbody i podobne. W trybie miękkim nic nie czyta kości; geometria siatki jest brana tak jak jest.

**Co tworzy pieczenie.** Siatka jest dzielona na ponumerowane klastry przestrzenne. Każda otoczka jest budowana *względem środka swojego klastra*, a środek jest zapisywany jako poza spoczynkowa klastra (`clusterRest`). To właśnie pozwala ramce przesuwać **i obracać** otoczkę jako jedną całość.

**Bez solvera.** Ramki są tworzone w pozach spoczynkowych, więc otoczki leżą dokładnie na rzeczywistej geometrii siatki i poruszają się z obiektem. Kształt jest poprawny; po prostu nie ma dynamiki. To zamierzona degradacja, nie porażka — i to właśnie znaczy „oblicza rzeczywisty kształt bez NDSC".

**Z solverem.** Solver popycha ramki (`Push` → `Apply`) i przejmuje całkowicie, dając pełną symulację. Adresowalny push nie jest nadpisywany przez globalne odpytywanie w tej samej klatce, co ma znaczenie, gdy tylko istnieje więcej niż jedno ciało.

**Aktualizacja na żywo działa też bez szkieletu.** `Dyc_LiveUpdate` odczytuje wierzchołki CPU `MeshFilter` tak jak są, więc dowolny kod deformujący siatkę — solver miękkiego ciała, skrypt proceduralny, własny deformer — napędza dokładne otoczki bez kleju specyficznego dla wtyczki. Otoczka potrzebuje `sourceVertices`, zapisywanego przez pieczenie; zapiecz ponownie starszy asset.

**Uczciwa granica:** deformacja istniejąca tylko na GPU (vertex shader, skinning GPU) nie może zostać odczytana z powrotem na CPU, więc aktualizacja na żywo jej nie widzi. Przenieś deformację na CPU albo zachowaj zapieczone otoczki.

### 21.8 Sterowanie pojedynczymi otoczkami

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`, `HullIndexOf`, `HullCount`, `BakedSet`, `Elements` i `Groups` są publiczne, więc zewnętrzne narzędzia mogą iterować i sterować pojedynczymi otoczkami bez refleksji.

---

## Dodatek A. Presety materiałów fizycznych

224 presety, 14 kategorii. Wartości to inżynierskie przybliżenia ze źródeł, odwzorowane na czteroparametrowy model Unity. „Części ciała” i „Tkanki i narządy” są wyprowadzane przez kuźnię z mieszanki tkanek, a nie wpisywane do tabeli.

| Kategoria | Liczba | Presety |
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


Każdy preset niesie także **gęstość** w kg/m³ do automatycznego obliczania masy, a presety
z rodziny gumy niosą `bounceThreshold`, potrzebny im, aby w ogóle się odbijać.

<a id="sec-appB"></a>
## Dodatek B. Kontrole fizyki projektu

Kontrola kondycji analizuje ustawienia `Physics` projektu, ponieważ preset materiału
nie może naprawić ustawienia globalnego:

| Ustawienie | Dlaczego to ważne |
|---|---|
| `bounceThreshold` | Uderzenia wolniejsze niż ta wartość nigdy się nie odbijają. Przy domyślnej wartości Unity równej 2 preset gumy wygląda na zepsuty. Zmniejsz ją do 0.2–0.5, aby używać materiałów sprężystych. |
| `defaultSolverVelocityIterations` | Przy wartości 1 stosy i szybkie uderzenia drgają lub przenikają. 2–4 jest zwykle lepsze, i to częsta główna przyczyna drgań ragdolla. |
| `gravity` | Jeśli nie wynosi −9.81, każda intuicja dotycząca masy i impulsu wyprowadzona z −9.81 jest błędna o ten sam współczynnik, a presety gęstości wymagają korekty. |
| `defaultContactOffset` | Duża szczelina kontaktu sprawia, że cienkie obiekty wyglądają, jakby unosiły się w powietrzu. |

Zastosowanie zalecanych wartości to czynność jednym kliknięciem z zakładki Materiały lub z
kontroli kondycji.
