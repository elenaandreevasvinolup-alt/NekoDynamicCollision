# Neko Dynamic Collision (NDC) — déploiement et manuel

Collision par enveloppes convexes cuites pour Unity. Tout le travail coûteux se
déroule dans l'éditeur ; l'exécution ne fait que charger et router. Un personnage
skiné ou un maillage statique devient un ensemble d'enveloppes convexes par os et
par région avec **zéro coût par image**, des événements de partition intégrés et des matériaux physiques par région peints au pinceau.

Le composant lui-même ne contient aucun code généré, aucun attribut et aucune
dépendance d'exécution à la partie éditeur du plugin. Supprimez `Editor/` et l'exécution fonctionne toujours.

## Sommaire

- [Partie A — Déploiement rapide](#sec-partA)
  - [1. Cuire votre premier personnage](#sec-1)
  - [2. Peindre les régions de matériaux](#sec-2)
  - [3. Le parcours en 10 minutes](#sec-3)
- [Partie B — Manuel](#sec-partB)
  - [4. Concepts fondamentaux](#sec-4)
  - [5. Installation et prérequis](#sec-5)
  - [6. La fenêtre de cuisson](#sec-6)
  - [7. Référence des menus](#sec-7)
  - [8. Précision](#sec-8)
  - [9. Le pinceau](#sec-9)
  - [10. Groupes de matériaux et préréglages](#sec-10)
  - [11. Diagnostics de couverture](#sec-11)
  - [12. Santé des collisions](#sec-12)
  - [13. Événements et intégration](#sec-13)
  - [14. Interrogation des déclencheurs](#sec-14)
  - [15. Masse, auto-collision et LOD](#sec-15)
  - [16. Localisation](#sec-16)
  - [17. Structure des répertoires](#sec-17)
  - [18. Désinstallation](#sec-18)
  - [19. Dépannage et FAQ](#sec-19)
  - [20. Contact](#sec-20)
- [Annexe A. Préréglages de matériaux physiques](#sec-appA)
- [Annexe B. Vérifications physiques du projet](#sec-appB)

---

<a id="sec-partA"></a>
# Partie A — Déploiement rapide

<a id="sec-1"></a>
## 1. Cuire votre premier personnage

1. Sélectionnez votre personnage et ajoutez le composant **Dynamic Collision**
   (`Add Component → Neko → Dynamic Collision`, ou le menu `GameObject`).
   À la création, le composant trouve son propre `SkinnedMeshRenderer` et crée un
   groupe de matériaux par défaut. Vous n'avez rien à renseigner.
2. Appuyez sur **Cuire** dans l'inspecteur.
3. Les enveloppes apparaissent dans la vue Scene sous forme de fil de fer en pose de
   liaison. Sélectionnez le personnage pour les voir ; le gizmo suit votre sélection par défaut.

La cuisson écrit trois types d'asset à côté de la scène :

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

Les maillages d'enveloppes sont des sous-assets de `_Baked.asset`, ils voyagent donc
avec lui et survivent au mode Play comme aux builds. Rien n'est recalculé à l'exécution.

<a id="sec-2"></a>
## 2. Peindre les régions de matériaux

Le pinceau est ce qui rend possible « un objet, plusieurs matériaux physiques ».

1. Ouvrez le cuiseur (`NekoWorks → NekoDynamicCollision → Open Main Window`).
2. Allez dans l'onglet **Matériaux** et ajoutez un groupe par région — par exemple
   `Hard` et `Soft`. Choisissez un préréglage pour chacun.
3. Allez dans l'onglet **Peinture**, choisissez le groupe à peindre, appuyez sur **Commencer la peinture**.
4. Dans la vue Scene, faites glisser sur les faces que vous voulez dans ce groupe. Les
   triangles peints sont immédiatement remplis de la couleur du groupe.
5. Relancez la cuisson. Chaque région obtient désormais ses propres enveloppes convexes avec son propre matériau physique.

Vous ne pouvez peindre que lorsque le mode Play est arrêté et que la fenêtre Animation
ne fait pas de prévisualisation. La pose elle-même n'a pas d'importance — les étiquettes
sont stockées par index de triangle, donc la pose actuelle n'a aucune incidence.

<a id="sec-3"></a>
## 3. Le parcours en 10 minutes

| Minute | Faites ceci |
|---|---|
| 0–2 | Ajoutez le composant, appuyez sur Cuire, regardez le gizmo. |
| 2–4 | Ouvrez l'onglet **Santé**, corrigez tout ce qui est rouge. |
| 4–6 | Ouvrez l'onglet **Cuisson**, regardez le chiffre de couverture. En dessous de ~95 %, augmentez la précision. |
| 6–9 | Ajoutez un groupe de matériaux par région, peignez-le, relancez la cuisson. |
| 9–10 | Réglez la couche d'interaction sur `Bullet`, câblez un nom d'événement, testez en mode Play. |

---

<a id="sec-partB"></a>
# Partie B — Manuel

<a id="sec-4"></a>
## 4. Concepts fondamentaux

### Des enveloppes, pas une soupe de triangles

Un `Rigidbody` dynamique (non cinématique) ne peut pas utiliser un `MeshCollider` non
convexe — c'est une restriction de PhysX, pas d'Unity. Toute forme de collision d'un
corps en mouvement doit donc être convexe. NDC cuit des **enveloppes convexes**, et
fournit à Unity l'enveloppe elle-même plutôt que le sous-ensemble brut de triangles,
ce qui explique pourquoi le nombre de sommets ne peut jamais atteindre le plafond PhysX de 255.

### Pourquoi des enveloppes rigides par os suffisent

En pose de liaison, `bone.localToWorldMatrix · bindposes[i] = I`. Le skinning d'un sommet
pondéré à 100 % sur un os donne donc exactement le maillage en pose de liaison. Autrement
dit : **une enveloppe cuite dans l'espace local de l'os est, bit pour bit, ce que
produirait une recuisson par image** pour des sommets à pondération rigide.

Seuls les sommets à pondération mixte — ceux qui traversent une jointure — diffèrent.
Ils sont couverts par les enveloppes voisines, qui se chevauchent par construction. C'est
pourquoi NDC peut être gratuit à l'exécution et rester précis là où ça compte.

### Un regroupement spatial, pas un découpage par ordre d'index

NDC regroupe les triangles par position (amorçage par point le plus éloigné, puis
croissance de type Dijkstra sur l'adjacence des arêtes). L'alternative — prendre les
triangles dans l'ordre des index — produit des enveloppes qui se chevauchent et enveloppent du vide, et qui empirent à mesure que vous en demandez davantage.

### Éléments et groupes de matériaux

Deux axes indépendants :

- **Élément** — *où*. Un os (mode Skin) ou le maillage entier (mode Mesh).
  Un élément enfant l'emporte toujours sur un ancêtre, donc les événements ne sont jamais distribués deux fois.
- **Groupe de matériaux** — *quoi*. Un ensemble de faces partageant un matériau physique et
  une densité, créé par peinture.

Une enveloppe est l'intersection d'un élément et d'un groupe de matériaux. Si un groupe
n'a aucune face peinte à l'intérieur d'un os donné, aucune enveloppe n'est produite pour cette paire.

### Ce que fait l'exécution

1. Charge l'ensemble cuit.
2. Crée un objet enfant masqué par enveloppe sous le bon os, avec une transformation
   identité, et assigne un `MeshCollider` convexe.
3. Construit une table de correspondance `Collider → enveloppe`.
4. Place un `Dyc_Relay` sur chaque `Rigidbody` qui possède une enveloppe.
5. Configure les couches, l'auto-collision et la masse.

Puis il s'arrête. Il n'y a aucun travail dans `Update` au-delà d'une interrogation
facultative des déclencheurs et d'une vérification de distance pour le LOD.

<a id="sec-5"></a>
## 5. Installation et prérequis

- Unity 2022.3 ou plus récent.
- Copiez `Assets/NekoDynamicCollision` dans votre projet. Il n'y a rien à
  configurer ; les assemblys sont portés par des définitions d'assembly.
- Deux assemblys :
  - `Neko.DynamicCollision.Runtime` — le composant, le relais, l'interrogation des
    déclencheurs, les structures d'événements et la façade d'intégration. Ne référence jamais `UnityEditor`.
  - `Neko.DynamicCollision.Editor` — le cuiseur, les mathématiques des enveloppes, le
    regroupement, le pinceau, le gizmo, le contrôle de santé, les préréglages et la fenêtre. Plateforme réservée à l'éditeur.

<a id="sec-6"></a>
## 6. La fenêtre de cuisson

`NekoWorks → NekoDynamicCollision → Open Main Window` (`Cmd/Ctrl+Shift+D`).

| Onglet | Ce qu'il fait |
|---|---|
| **Cuisson** | Source, mode, précision, cuire/effacer/reconstruire, statistiques, couverture |
| **Peinture** | Liste des groupes avec les comptes de triangles peints, réglages du pinceau, réinitialisation de pose |
| **Gizmo** | Ce que la vue Scene dessine et comment |
| **Pièces** | La liste des éléments — os, inclure les enfants, nom d'événement, multiplicateur de dégâts |
| **Matériaux** | Groupes de matériaux, préréglages, table de comportement des paires, audit physique du projet |
| **Santé** | Score sur 100, chaque problème, corrections en un clic |
| **Paramètres** | Langue, diagnostics, ouvrir le dossier cuit, réinitialiser les préférences |

<a id="sec-7"></a>
## 7. Référence des menus

Tout vit sous un unique emplacement de premier niveau, afin qu'installer d'autres plugins
NekoWorks n'élargisse jamais la barre de menus.

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

Les libellés de menu sont localisés au chargement et lors d'un changement de langue ; les
chaînes anglaises statiques des attributs servent de repli si l'API interne de menus
d'Unity n'est pas disponible dans votre version.

<a id="sec-8"></a>
## 8. Précision

Un seul réglage, quatre étapes. En interne, il se déploie en quatre valeurs :

| Précision | Triangles par enveloppe | Enveloppes par pièce | Chevauchement de jointure | Seuil de poids |
|---|---|---|---|---|
| Coarse | 120 | 1 | 4.0 mm | 0.35 |
| Normal | 250 | 2 | 3.0 mm | 0.25 |
| Fine | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **Triangles par enveloppe** plafonne la quantité de géométrie qu'une enveloppe peut
  absorber. Plus de triangles par enveloppe signifie des enveloppes moins nombreuses, plus grandes et plus lâches.
- **Enveloppes par pièce** est le nombre cible de groupes par élément. Plus de groupes
  signifie un ajustement plus serré et plus de collideurs.
- **Chevauchement de jointure** gonfle chaque enveloppe vers l'extérieur afin que les
  régions voisines se chevauchent au lieu de laisser un vide. Le chevauchement est sûr
  (un impact n'est jamais manqué, et la déduplication dans la même image arrête les événements doubles) ; un vide ne l'est pas.
- **Seuil de poids** écarte les sommets dont le poids pour l'os dominant est inférieur à
  la valeur. Plus il est élevé, plus l'enveloppe est serrée et « rigoureusement correcte ».

Le nombre de sommets d'une enveloppe ne peut jamais dépasser le nombre de sommets uniques
du groupe, lui-même plafonné à 250 — bien en dessous de la limite PhysX de 255. Le
contrôle de santé signale en rouge toute enveloppe dépassant 255.

### 8.1 Le mode convexe décompose — au-delà de CC et du collideur complexe intégré

En mode **convexe**, un maillage concave est découpé en pièces convexes le long de ses concavités. C'est la même idée que le collideur complexe intégré et les outils de type V-HACD (CC) : prenez n'importe quel maillage, produisez des pièces convexes. NDC garde l'ampleur et relève le plafond :

| | Collideur complexe / CC | NDC |
|---|---|---|
| N'importe quel maillage | oui | oui |
| Bien optimisé | oui | oui — tâche de cuisson en arrière-plan, progression, annulation, budgets par os |
| Les pièces respectent les articulations | **non** — purement géométrique, une épaule peut avaler un bras | **oui** — les pièces portent des étiquettes d'os issues d'un champ de poids |
| Utilisable sur un corps en mouvement | **non** — PhysX refuse un collideur non convexe sur un `Rigidbody` non cinématique | **oui** — la sortie est une enveloppe convexe |
| Coût à l'exécution | cuisson du collideur au chargement | nul — cuit dans les assets |
| Repli | — | regroupement spatial, pour qu'un maillage dégénéré obtienne quand même un collideur |

La fenêtre Expert indique si les pièces viennent de la **décomposition** (découpe le long des concavités) ou du **regroupement spatial** (le repli), donc la différence est un nombre et non une supposition.

**La finesse de la coupe** tient à un seul curseur — **Decomposition detail** —, la taille de voxel en millimètres figurant dans le champ numérique juste en dessous. Les deux sont deux vues d'*un* seul nombre, donc ils s'accordent toujours : faites glisser le curseur et le champ suit, saisissez dans le champ et le curseur bouge. Il n'y a pas de second réglage à maintenir synchronisé.

- **À gauche** — un voxel grossier : des pièces moins nombreuses et plus grandes. Le moins cher, et généralement suffisant pour un accessoire.
- **À droite** — le voxel le plus fin. Les pièces suivent la surface, donc le coût **égale le mode non convexe** : il n'y a rien de plus fin à gagner, seulement du coût.

Le tableau de précision ci-dessus s'applique alors à l'*ajustement des groupes* — la façon dont chaque pièce épouse la forme —, et non au nombre de collideurs obtenus.

Le même curseur apparaît dans les deux modes : la décomposition convexe et la simplification non convexe sont les deux façons de répondre à la même question : *combien de détail je veux*.

### 8.2 Le détail non convexe tient à un seul curseur

Basculez **Collider shape → Non-convex surface** et un curseur **Surface detail** apparaît.

| Curseur | Résultat |
|---|---|
| Tout à gauche | Surface fortement simplifiée — peu de triangles, facettes visibles |
| Au milieu | Un bon compromis : la forme se lit correctement, le collideur reste peu coûteux |
| **Tout à droite** | **Aucune simplification** — la surface est reprise telle quelle depuis le maillage |

Pourquoi un curseur et non un nombre de triangles : « combien de triangles par pièce » ne peut pas être choisi judicieusement sans savoir combien le maillage en compte — 500 est grossier pour un torse et précis pour un doigt. Le curseur répond à la seule question qu'un utilisateur peut réellement poser : *à quel point la forme exacte m'importe*. La position tout à droite n'est pas « presque exacte », elle est exacte : la simplification est entièrement désactivée.

Les petits groupes ne sont jamais simplifiés, quelle que soit la position du curseur — tirer un doigt fin vers une grille grossière le réduit à rien, et un collideur vide est pire qu'un collideur coûteux.

Les modes Skin et Mesh utilisent le même curseur.

<a id="sec-9"></a>
## 9. Le pinceau

Le pinceau n'« exclut » pas les faces. Il les **marque**, et les marques pilotent la cuisson.

| Action | Effet |
|---|---|
| Glisser bouton gauche | Assigne le groupe courant aux triangles sous le curseur |
| Shift + glisser | Efface en revenant au groupe 0 et retire le marquage peint |
| Molette de la souris | Rayon du pinceau |
| Bascule `X` (fenêtre) | Reflète chaque trait selon l'axe X = 0 local de l'objet |

Réglages : rayon, X-Ray (ignorer les triangles orientés vers l'arrière), miroir sur X.

Exigences, imposées par un message explicite plutôt qu'un échec silencieux :

1. Le mode Play doit être arrêté.
2. La fenêtre Animation ne doit pas être en prévisualisation.

Le rig n'a **pas** besoin d'être en pose de liaison. Le pinceau lance ses rayons contre le
maillage dans sa pose **actuelle** ; comme le skinning ne change jamais la topologie, les
index de triangles correspondent un à un et les étiquettes restent correctes.

Si vous voulez quand même le rig en pose de liaison, le bouton **Réinitialiser à la pose
de liaison** résout les transformations locales à partir de `bindposes[i].inverse` et les réécrit, avec annulation.

<a id="sec-10"></a>
## 10. Groupes de matériaux et préréglages

Chaque groupe porte un `PhysicMaterial`, une densité en kg/m³, un nom d'événement
facultatif et un multiplicateur de dégâts.

**Préréglages.** 224 préréglages répartis en 14 catégories (Metal, Ceramic, Plastic,
Glass, Wood, Stone & Concrete, Rubber, Fabric & Leather, Body parts, Tissue & organs,
Organic, Ice, Food, Other) — voir [Annexe A](#appendix-a-physic-material-presets).
Appliquer un préréglage crée un véritable asset `.physicMaterial` sous
`Baked/<Scene>/Materials/`, donc il peut être référencé, comparé, mis dans Addressables
et remis à un artiste.

**La forge de matériaux.** Une ligne du tableau répond « de quoi c'est fait » ; la forge
répond « ce que c'est maintenant ». Elle multiplie un préréglage de base par un **état de
surface** (Dry, Wet, Oiled, Bloody, Sweaty, Icy, Frozen, Dusty, Rough, Polished, Rusted,
Worn, Charred, Clothed, Armoured). `Dry` est l'identité, il n'y a donc jamais de
`steel_dry` à côté d'un `steel`. La friction est multipliée sur le coefficient statique
et dynamique à la fois.

**Les parties du corps sont dérivées, pas saisies.** « Parties du corps » et « Tissus et
organes » ne sont pas des lignes du tableau : elles sont calculées à partir d'un mélange
de tissus — la densité est additive, la souplesse est additive plus un terme de coussin,
et la friction et le rebond suivent la souplesse. « Sein » est 80 % graisse + 10 % muscle
+ 10 % peau ; « crâne » est 95 % os + 5 % peau.

**Générer les assets.** *Générer les assets de variantes* écrit un `.physicMaterial` par
état dans `Baked/<Scene>/Materials/`.

**Stratégie de combinaison.** Toute la bibliothèque utilise `Multiply` pour la friction et
`Maximum` pour le rebond. La priorité de combinaison d'Unity est
`Average < Minimum < Multiply < Maximum`, donc avec cette stratégie toute surface glissante
domine le résultat de friction et tout matériau rebondissant domine le résultat de rebond —
ce que les gens attendent intuitivement.

**Table de comportement des paires.** L'onglet Matériaux résout chaque paire de groupes de
votre projet selon les vraies règles de priorité d'Unity et affiche la valeur qui s'appliquera
réellement, plus un verdict en langage clair (« adhérent / sans rebond »). C'est le moyen le
plus rapide de répondre à « pourquoi ma glace n'est pas glissante ».

**Attribution automatique par nom.** Remplit chaque groupe à partir des préréglages en
faisant correspondre le nom du groupe à des mots-clés anglais, chinois et russes.

**Limitation honnête.** Un `PhysicMaterial` possède quatre nombres et deux modes de
combinaison. Il ne peut pas exprimer la friction de roulement, la friction anisotrope, la
viscosité, la déformation plastique, la température ou l'usure. « Paramètres du monde réel »
désigne ici une table de correspondance sourcée et des préréglages utilisables — pas une simulation physique.

<a id="sec-11"></a>
## 11. Diagnostics de couverture

L'onglet Cuisson répond à la question qui relève habituellement de la conjecture : **quels
triangles n'ont aucune enveloppe ?**

Il prend le maillage source en pose de liaison et teste le centroïde de chaque triangle
contre les plans de chaque enveloppe, puis rapporte :

- un pourcentage global et une barre de progression ;
- une répartition par élément ;
- la liste des triangles non couverts, dessinables dans la vue Scene en rouge
  (**Afficher les faces non couvertes**).

Considérez un résultat inférieur à ~95 % comme un problème : augmentez la précision, ou
vérifiez que les os des éléments couvrent réellement tout le squelette.

<a id="sec-12"></a>
## 12. Santé des collisions

Un score sur 100 avec chaque problème listé et, lorsque c'est possible, une correction en un clic.

Les contrôles incluent : rien de cuit ; une enveloppe au-dessus du plafond de sommets PhysX ;
des enveloppes proches du plafond ; des groupes dégénérés ; des triangles n'appartenant à
aucun élément ; le maillage source modifié depuis la dernière cuisson ; des groupes de
matériaux sans matériau, sans enveloppes ou avec trop d'enveloppes fragmentées ; un rôle
Hitbox/Trigger sans couches d'interaction ; aucun `Rigidbody` dans la chaîne des parents ;
une masse de rigidbody bien trop petite ou trop grande ; l'auto-collision du ragdoll
entièrement activée ; des événements distribués sans auditeur ; et les contrôles physiques au niveau du projet de [l'Annexe B](#appendix-b-project-physics-checks).

<a id="sec-13"></a>
## 13. Événements et intégration

Chaque événement porte un contexte complet, vous n'avez donc jamais à rechercher quoi que ce soit :

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

Trois façons de l'exploiter :

1. **UnityEvent** — `onEvent` sur le composant, pour les auditeurs enregistrés par code.
2. **Registre par chaîne** — donnez un nom d'événement à un élément ou à un groupe et
   écoutez avec `Dyc_Events.Register("Hit.Head", handler)`. Les noms mal orthographiés ne
   lèvent aucune erreur, mais le contrôle de santé signale les distributions que personne n'a reçues.
3. **Façade statique** — `Dyc_Api` pour les outils externes :

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

Les multiplicateurs de dégâts vivent sur l'élément et sur le groupe et sont multipliés
entre eux — tête ×4 est un seul nombre, pas une couche de code de raccordement.

**Déduplication.** Le chevauchement de jointure signifie que deux groupes adjacents peuvent
tous deux toucher le même collideur étranger dans une même image. NDC distribue au plus un
événement par `(élément, autre collideur)` et par image, les matériaux partitionnés ne produisent donc pas d'événements doubles.

<a id="sec-14"></a>
## 14. Interrogation des déclencheurs

Unity livre les rappels de déclencheurs **par paire de Rigidbody**, donc un seul ragdoll est
une seule paire de rigidbody et la couche physique ne peut tout simplement pas vous dire
quel os est entré dans un volume. Donner à chaque os son propre `Rigidbody` détruirait la promesse du coût nul.

Les déclencheurs partitionnés sont donc échantillonnés :

- Chaque élément est testé avec `Physics.OverlapBoxNonAlloc` sur les bornes en espace monde
  de ses collideurs.
- Seuls les véritables déclencheurs sont considérés, et jamais vos propres collideurs.
- Les éléments sont traités par tranches : `elements / frames-per-pass` par image.
- L'entrée et la sortie sont comparées à la passe précédente pour chaque élément.

**Sémantique à retenir :** il s'agit d'un échantillonnage, pas d'un événement. Une passe
très rapide peut être manquée. Augmentez la fréquence d'échantillonnage, ou utilisez la **marge de balayage** pour agrandir la boîte de requête.

<a id="sec-15"></a>
## 15. Masse, auto-collision et LOD

**Masse à partir de la densité.** NDC connaît le volume de chaque enveloppe, il peut donc
calculer correctement la masse : `mass = hull volume × group density`, avec normalisation
facultative pour que le personnage entier corresponde à une masse totale cible. Cela supprime
la plus ancienne corvée de réglage manuel des ragdolls Unity. Un seul rigidbody reçoit la somme des volumes des enveloppes qu'il possède.

**Auto-collision.** `Ignore` (toutes les paires), `Adjacent` (même élément, ou ancêtre et
descendant) ou `On`. Des os de ragdoll qui entrent en collision entre eux sont une source
fréquente de tremblements, et `Adjacent` est la réponse habituelle. Au-delà de 200 collideurs,
l'étape est ignorée avec un avertissement plutôt que de bloquer `Awake`.

**LOD.** `Disable` désactive les collideurs au-delà d'une distance ; `Reduce` ne conserve
que la plus grande enveloppe par élément. La vérification s'exécute une image sur quatre.

**Rigidbody.** Unity ne livre les rappels de collision qu'au GameObject qui possède le
`Rigidbody`. Pour un ragdoll, chaque os en a déjà un. Pour tout le reste, activez **Ajout
automatique de Rigidbody** et NDC en crée un cinématique sur l'objet du composant.

<a id="sec-16"></a>
## 16. Localisation

La fenêtre, l'inspecteur, les messages de santé et les libellés de menu sont localisés en
**15 langues** :

`en` (intégré) · `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- L'anglais est intégré à l'assembly et sert de repli pour toute clé manquante, donc une
  langue partiellement traduite se dégrade au lieu de casser.
- Toutes les autres langues sont de pures données dans `Locale/<code>/strings.json` — en
  ajouter une ne nécessite aucune recompilation.
- L'arabe et l'hébreu sont entièrement de droite à gauche : la mise en page se reflète au
  lieu de s'appuyer sur `style.direction`, dont la prise en charge par UI Toolkit est
  incomplète et dépend de la version.
- Changez la langue dans l'onglet **Paramètres**. La fenêtre et le menu se mettent à jour
  immédiatement, sans rechargement de domaine.
- L'onglet Paramètres affiche aussi le chemin de locale résolu et le nombre de langues trouvées, de sorte qu'une erreur d'empaquetage soit visible au lieu d'être silencieuse.

<a id="sec-17"></a>
## 17. Structure des répertoires

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
## 18. Désinstallation

1. Retirez le composant **Dynamic Collision** de vos prefabs et de vos scènes.
2. Supprimez `Assets/NekoDynamicCollision`.

Les assets cuits vivent sous `Baked/` dans le dossier du plugin et partent avec lui. Rien
n'est écrit en dehors du dossier du plugin, et l'exécution ne contient aucun code dépendant
de la partie éditeur.

<a id="sec-19"></a>
## 19. Dépannage et FAQ

**Rien n'entre en collision et aucun événement ne se déclenche.**
Il n'y a aucun `Rigidbody` dans la chaîne des parents. Unity n'envoie les rappels de
collision qu'à l'objet qui possède le rigidbody. Activez **Ajout automatique de Rigidbody**, ou ajoutez-en un vous-même.

**Les enveloppes ne correspondent pas à ce que je vois.**
Le gizmo dessine la **pose de liaison** par défaut — c'est ce qui a été cuit. Désactivez
**Pose de liaison** dans l'onglet Gizmo pour les voir dans la pose actuelle.

**« L'enveloppe a N sommets, au-delà de la limite PhysX de 255. »**
Unity ignore silencieusement une enveloppe convexe hors limite. Baissez la précision d'un
cran ; le contrôle de santé propose exactement cela comme correction en un clic.

**Des impacts sont manqués par endroits.**
Vérifiez d'abord le pourcentage de couverture. En dessous de ~95 % signifie de vrais trous.
Vérifiez ensuite le **chevauchement de jointure** pour l'étape de précision qui est la vôtre.

**Un trait de pinceau a laissé un vide entre deux régions.**
C'est le problème de jointure. Augmentez la précision (ce qui abaisse le chevauchement de
jointure) ou peignez un peu au-delà de la frontière. La déduplication dans la même image
empêche déjà les événements doubles issus du chevauchement.

**Les événements se déclenchent deux fois pour un seul impact.**
Deux éléments différents ont été touchés dans la même image, ce qui est légitime. Si vous
voulez vraiment un événement par paire d'objets, filtrez par `elementIndex` dans votre gestionnaire.

**J'ai peint mais rien n'a changé après la cuisson.**
Les étiquettes sont ignorées lorsque le nombre de triangles ne correspond pas au maillage
source — généralement après un réimport ou un changement de topologie. Repeignez, ou lancez
d'abord une cuisson pour que l'asset d'étiquettes soit créé à la bonne taille.

**Le pinceau ne démarre pas.**
Le mode Play est actif, ou la fenêtre Animation est en prévisualisation. Les deux sont
affichés comme raison explicite dans l'onglet Peinture.

**Mon ancien travail au pinceau a disparu après une mise à jour.**
Cela ne devrait pas : les masques créés avant l'existence du marquage peint sont migrés, et
toute étiquette non nulle est traitée comme peinte. Si un masque a été effacé, repeignez et relancez la cuisson.

**Le coût à l'exécution est-il vraiment nul ?**
En régime stable, oui : les enveloppes sont des assets, les transformations sont suivies par
la hiérarchie, et il n'y a aucun travail sur maillage. Le seul travail par image est
l'interrogation facultative des déclencheurs et la vérification de distance du LOD.

**Puis-je avoir deux composants Dynamic Collision sur un même objet ?**
Non, et c'est bloqué exprès. Deux composants créeraient des enveloppes doubles sur les mêmes
faces, doubleraient les contacts et doubleraient les événements. Partitionnez plutôt avec
des éléments et des groupes de matériaux.

<a id="sec-20"></a>
## 20. Contact

NekoAndreeva — voir `package.json` pour l'URL du dépôt.

---

<a id="sec-appA"></a>
## 21. Usage ordinaire et parité avec RASCAL

### 21.1 NDC comme collideur système

La règle de conception est qu'un développeur qui connaît `Collider` et `Rigidbody` connaît déjà NDC, parce que NDC *crée* des collideurs ordinaires : des `MeshCollider` sur des enfants masqués, un `Rigidbody` sur l'objet, des messages standard, des couches et des matériaux physiques ordinaires. `Physics.Raycast` et `Physics.OverlapSphere` n'ont besoin d'aucune modification.

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // ajouter + construire
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // un message Unity standard
void OnTriggerStay(Collider other) { }     // un message Unity standard
```

| Appel | Signification |
|---|---|
| `Find(go)` | Le composant, sur l'objet ou un parent |
| `Attach(go, generateNow)` | Ajouter le composant et construire |
| `Build(go)` / `Rebuild(go)` | Construire depuis l'ensemble cuit, ou générer à l'exécution |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | État, toutes les enveloppes d'un coup |
| `SetTrigger(go, v)` | `Collider.isTrigger` pour chaque enveloppe |
| `SetMaterial(go, pm)` | Immédiat ; ne survit pas à une reconstruction |
| `GetColliders(go)` / `ForEachCollider(go, a)` | Les enveloppes, comme des `Collider` ordinaires |
| `SetReceiver(go, t)` | Envoyer aussi les messages standard à `t` |

**Seul le zonage est en plus.** Les zones, les matériaux peints, le LOD, les événements, la masse à partir de la densité et le contrôle de santé nécessitent l'API NDC — ce sont les choses qu'un collideur système ne peut pas faire.

**Pas d'étape de cuisson.** Cochez **Advanced ▸ Build at startup when nothing is baked** (ou appelez `Attach`). Les collideurs sont construits par os depuis le maillage à l'`Awake` — une enveloppe convexe par os, comme le réglage par défaut de RASCAL. La cuisson reste le moyen d'obtenir zones, décomposition, couverture et précision.

**Les messages qui atteignent votre script.** Unity livre `OnCollision*` à l'objet portant le `Rigidbody`. Si votre script est ailleurs (une racine de personnage alors que le Rigidbody est sur un os), réglez `Advanced ▸ Also send OnCollision*/OnTrigger* to` — les messages sont alors transmis avec `SendMessage`, ce qui ne coûte rien sur les images sans collision.

### 21.2 Live update — la capacité que la cuisson ne peut pas remplacer

Une enveloppe cuite collée à un os est exacte en pose de liaison et rigide ensuite. Sous une forte déformation — un accroupissement, un membre comprimé, un tissu tendu — l'enveloppe rend mal compte de la surface. Live update reconstruit l'enveloppe à partir de la pose skinning **actuelle**.

Activez-le avec **Advanced ▸ Live update** ou `Dyc_Collision.EnableLiveUpdate(go)`.

| Réglage | Défaut | Signification |
|---|---|---|
| `liveUpdate` | off | Reconstruire les enveloppes depuis la pose actuelle |
| `liveUpdateContinuous` | on | Continuer, ou exécuter une passe à la demande |
| `idleCpuBudgetMs` | 0.2 | Budget quand le maillage bouge à peine |
| `activeCpuBudgetMs` | 1.0 | Budget quand il bouge vite |
| `meshUpdateThreshold` | 0.02 | Sauter la passe en dessous de ce mouvement (mètres) |
| `maxColliderTriangles` | 5000 | Plafond par collideur, pour qu'un os lourd ne mange pas le budget |

Le budget est choisi d'après l'ampleur réelle du mouvement du maillage : un personnage debout est facturé au tarif idle, un personnage qui court au tarif actif. Le travail qui ne rentre pas est reporté à l'image suivante, et `OnUpdateYield` / `OnPassComplete` rapportent les millisecondes écoulées.

**Il ne reconstruit pas tout à chaque image.** Trois mécanismes rendent le coût prévisible :

1. **Incrémental.** Le centre de chaque groupe est comparé à la passe précédente, et seuls les groupes qui ont réellement bougé sont reconstruits. Un corps souple suspendu à une ancre a un bord qui tremble et un milieu presque immobile — le milieu ne coûte rien.
2. **Ordonné par priorité.** La file est triée selon l'ampleur du déplacement de chaque groupe. Si le budget s'épuise, il s'épuise sur les groupes les plus calmes — ceux où l'imprécision se voit le moins. Sans cela, le budget serait dépensé sur ce qui se trouve par hasard en tête de liste.
3. **Budgété à l'horloge**, et non par nombre de groupes : le coût par image ne croît pas avec le nombre de groupes du corps.

`LastDirtyCount` et `LastBuiltCount` rapportent ce que la dernière passe a réellement fait, ce qui est la façon honnête de voir l'économie.

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"pass done in {ms:F2} ms");
live.StopAfterPass();          // terminer la passe en cours, puis s'arrêter
live.UpdateNow();              // une passe complète, hors budget
```

**Prérequis.** Les enveloppes ont besoin de `sourceVertices`, écrit par la cuisson ; recuisez un personnage ancien pour activer live update. Le coût à l'exécution est réel — c'est la seule fonction qui contredit « zéro coût par image », et c'est précisément pourquoi elle est désactivée par défaut.

### 21.3 Surcharges par os

`Dyc_BoneProperties` se place sur l'os lui-même (Add Component ▸ NekoWorks ▸ Dynamic Collision ▸ Bone Properties) :

| Champ | Effet |
|---|---|
| `overrideMaterial` + `physicsMaterial` | Les enveloppes de cet os utilisent ce matériau |
| `overrideConvex` + `convex` | Enveloppe au lieu de surface (ou l'inverse) pour cet os |
| `overrideWeightThreshold` + `boneWeightThreshold` | Seuil de poids par os |
| `exclude` | Aucun collideur pour cet os |

L'accrocher à l'os signifie qu'il survit aux renommages — il détient une référence, pas un chemin.

### 21.4 Matériaux par matériau source

`Advanced ▸ Materials by source material` associe un `Material` source à un `PhysicMaterial`. Les enveloppes sont attribuées d'après le sous-maillage dont elles proviennent majoritairement, résolu à la cuisson dans `Dyc_BakedSet.sourceMaterials`. Priorité, de la plus haute à la plus basse :

1. `Dyc_BoneProperties.physicsMaterial` ;
2. l'association de matériau pour le matériau source de l'enveloppe ;
3. le matériau du groupe peint.

### 21.5 Carte d'exclusion des sommets

`Advanced ▸ Exclusion map` lit un canal de texture (R/G/B/A, avec un seuil) et exclut les sommets dont la valeur de canal atteint ou dépasse ce seuil. Le pinceau marque des *faces*, la carte marque des *sommets* — ils se complètent. Le maillage a besoin d'UV, la texture doit être en **Read/Write Enabled**, et un triangle n'est exclu que lorsque ses trois sommets le sont.

### 21.6 Réorienter le squelette

`Advanced ▸ Attach hulls to another skeleton` construit les enveloppes à partir de ce maillage mais les accroche à des os de même nom d'une autre racine — le cas `RetargetSkeleton`, pour Puppet Master et les configurations similaires. Les os sont résolus par chemin relatif ; une enveloppe sans homonyme reste sur son propre squelette et le rapport de cuisson indique combien.

### 21.7 Mode soft — sans os, piloté par code ou par solveur

Le mode soft (`Mode → Soft`) n'est **pas** un rig skinning. Il est destiné à un maillage **sans squelette** dont la forme est produite par un solveur ou par du code — NekoDynamicSoftbody et similaires. Rien dans le mode soft ne lit les os ; la géométrie du maillage est prise telle quelle.

**Ce que produit la cuisson.** Le maillage est découpé en groupes spatiaux numérotés. Chaque enveloppe est construite *par rapport au centre de son groupe*, et le centre est stocké comme pose de repos du groupe (`clusterRest`). C'est ce qui permet à un frame de translater **et faire tourner** l'enveloppe d'un seul tenant.

**Sans solveur.** Les frames sont créés à leurs poses de repos, donc les enveloppes reposent exactement sur la géométrie réelle du maillage et bougent avec l'objet. La forme est correcte ; il n'y a simplement aucune dynamique. C'est la dégradation prévue, pas un échec — et c'est ce que signifie « calcule la forme réelle sans NDSC ».

**Avec solveur.** Le solveur pousse les frames (`Push` → `Apply`) et prend complètement le relais, donnant une simulation complète. Un push adressable n'est pas écrasé par le polling global dans la même image, ce qui compte dès qu'il existe plus d'un corps.

**Live update fonctionne aussi sans squelette.** `Dyc_LiveUpdate` lit les sommets CPU d'un `MeshFilter` tels quels, donc tout code qui déforme le maillage — un solveur de corps souple, un script procédural, un déformeur personnalisé — pilote des enveloppes précises sans colle propre au plugin. Une enveloppe a besoin de `sourceVertices`, écrit par la cuisson ; recuisez un asset ancien.

**La limite honnête :** une déformation qui n'existe que sur le GPU (un vertex shader, du skinning GPU) ne peut pas être relue sur le CPU, donc live update ne la voit pas. Déplacez la déformation vers le CPU, ou gardez les enveloppes cuites.

### 21.8 Piloter des enveloppes individuelles

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`, `HullIndexOf`, `HullCount`, `BakedSet`, `Elements` et `Groups` sont publics, donc des outils externes peuvent itérer et piloter des enveloppes individuelles sans réflexion.

---

## Annexe A. Préréglages de matériaux physiques

224 préréglages, 14 catégories. Les valeurs sont des approximations d'ingénierie sourcées, projetées sur le modèle à quatre paramètres d'Unity. « Parties du corps » et « Tissus et organes » sont dérivés par la forge à partir d'un mélange de tissus, et non saisis dans le tableau.

| Catégorie | Nombre | Préréglages |
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


Chaque préréglage porte aussi une **densité** en kg/m³ pour la masse automatique, et les
préréglages de la famille du caoutchouc portent le `bounceThreshold` dont ils ont besoin pour rebondir tout court.

<a id="sec-appB"></a>
## Annexe B. Vérifications physiques du projet

Le contrôle de santé audite les réglages `Physics` du projet, car un préréglage de matériau
ne peut pas corriger un réglage global :

| Réglage | Pourquoi c'est important |
|---|---|
| `bounceThreshold` | Les impacts plus lents que cette valeur ne rebondissent jamais. À la valeur par défaut d'Unity de 2, un préréglage de caoutchouc semble cassé. Abaissez-la à 0,2–0,5 pour utiliser des matériaux élastiques. |
| `defaultSolverVelocityIterations` | À 1, les piles et les impacts rapides tremblent ou traversent. 2–4 est généralement mieux, et c'est une cause racine fréquente des tremblements de ragdoll. |
| `gravity` | Si elle ne vaut pas −9,81, toute intuition de masse et d'impulsion dérivée de −9,81 est décalée du même facteur, et les préréglages de densité doivent être corrigés. |
| `defaultContactOffset` | Un écart de contact large fait paraître les objets fins comme s'ils flottaient. |

Appliquer les valeurs recommandées est une action en un clic depuis l'onglet Matériaux ou
le contrôle de santé.
