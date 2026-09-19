# Neko Dynamic Collision (NDC) — Deployment & Manual

Baked convex-hull collision for Unity. All the expensive work happens in the
Editor; the runtime only loads and routes. A skinned character or a static mesh
becomes a set of per-bone, per-region convex hulls with **zero per-frame cost**,
built-in partition events, and per-region physic materials painted with a brush.

The component itself contains no generated code, no attributes and no runtime
dependency on the plugin's editor half. Delete `Editor/` and the runtime still works.

## Contents

- [Part A — Quick deployment](#sec-partA)
  - [1. Bake your first character](#sec-1)
  - [2. Paint material regions](#sec-2)
  - [3. The 10-minute path](#sec-3)
- [Part B — Manual](#sec-partB)
  - [4. Core concepts](#sec-4)
  - [5. Installation and requirements](#sec-5)
  - [6. The baker window](#sec-6)
  - [7. Menu reference](#sec-7)
  - [8. Precision](#sec-8)
  - [9. The brush](#sec-9)
  - [10. Material groups and presets](#sec-10)
  - [11. Coverage diagnostics](#sec-11)
  - [12. Collision health](#sec-12)
  - [13. Events and integration](#sec-13)
  - [14. Trigger polling](#sec-14)
  - [15. Mass, self-collision and LOD](#sec-15)
  - [16. Localization](#sec-16)
  - [17. Directory structure](#sec-17)
  - [18. Uninstall](#sec-18)
  - [19. Troubleshooting and FAQ](#sec-19)
  - [20. Contact](#sec-20)
- [Appendix A. Physic material presets](#sec-appA)
- [Appendix B. Project physics checks](#sec-appB)

---

<a id="sec-partA"></a>
# Part A — Quick deployment

<a id="sec-1"></a>
## 1. Bake your first character

1. Select your character and add the **Dynamic Collision** component
   (`Add Component → Neko → Dynamic Collision`, or the `GameObject` menu).
   On creation the component finds its own `SkinnedMeshRenderer` and creates one
   default material group. You do not have to fill anything in.
2. Press **Bake** in the inspector.
3. Hulls appear in the Scene view as a bind-pose wireframe. Select the character to
   see them; the gizmo follows your selection by default.

Baking writes three kinds of asset next to the scene:

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

The hull meshes are sub-assets of `_Baked.asset`, so they travel with it and
survive Play mode and builds. Nothing is recomputed at runtime.

<a id="sec-2"></a>
## 2. Paint material regions

The brush is what makes "one object, several physic materials" possible.

1. Open the baker (`NekoWorks → NekoDynamicCollision → Open Main Window`).
2. Go to the **Materials** tab and add a group per region — for example
   `Hard` and `Soft`. Pick a preset for each.
3. Go to the **Paint** tab, pick the group you want to paint, press **Start painting**.
4. In the Scene view, drag over the faces you want in that group. The painted
   triangles are filled with the group colour immediately.
5. Re-bake. Each region now gets its own convex hulls with its own physic material.

You can paint only while Play mode is stopped and the Animation window is not
previewing. The pose itself does not matter — labels are stored per triangle index,
so the current pose is irrelevant.

<a id="sec-3"></a>
## 3. The 10-minute path

| Minute | Do this |
|---|---|
| 0–2 | Add the component, press Bake, look at the gizmo. |
| 2–4 | Open the **Health** tab, fix everything red. |
| 4–6 | Open the **Bake** tab, look at the coverage number. Below ~95%, raise the precision. |
| 6–9 | Add a material group per region, paint it, re-bake. |
| 9–10 | Set the interact layer to `Bullet`, wire an event name, test in Play mode. |

---

<a id="sec-partB"></a>
# Part B — Manual

<a id="sec-4"></a>
## 4. Core concepts

### Hulls, not triangle soup

A dynamic (non-kinematic) `Rigidbody` cannot use a non-convex `MeshCollider` — that
is a PhysX restriction, not a Unity one. So every collision shape for a moving body
must be convex. NDC bakes **convex hulls**, and feeds Unity the hull itself rather
than the raw triangle subset, which is why the vertex count can never hit the PhysX
ceiling of 255.

### Convex hulls, or the actual surface

Convex is the default because it is the only shape PhysX accepts on a moving body, but
it has a price: a hull must close over a concavity, so at a concave joint — armpit,
groin, neck — the hulls of two neighbouring bones cannot meet. A gap remains, and a
thin hit can pass through it. That is a property of convexity, not a defect of the bake.

`Collider shape → Non-convex surface` bakes the actual per-bone surface instead, with
no hull inflation. Joints become seamless, exactly like the built-in collider with
**Convex** unticked. The price belongs to PhysX and the health check states it
plainly: kinematic or animation-driven bodies only, no mesh-mesh collisions, no fast
broadphase. Choose it for hit detection on mobile, not for physics blocking. The mode
is stored per baked hull, so a set baked before this option existed stays fully convex.

### Why bone-rigid hulls are enough

At bind pose, `bone.localToWorldMatrix · bindposes[i] = I`. Skinning for a vertex
weighted 100% to one bone therefore evaluates to exactly the bind-pose mesh. In
other words: **a hull baked in bone-local space is bit-for-bit what a per-frame
re-cook would produce** for rigidly weighted vertices.

Only blend-weighted vertices — the ones crossing a joint — differ. Those are covered
by neighbouring hulls, which overlap by construction. This is why NDC can be free at
runtime and still be accurate where it matters.

### Spatial clustering, not index-order chunking

NDC groups triangles by position (farthest-point seeding plus Dijkstra-style growth
over edge adjacency). The alternative — taking triangles in index order — produces
hulls that overlap each other and wrap air, and get worse the more hulls you ask for.

### Elements and material groups

Two independent axes:

- **Element** — *where*. One bone (Skin mode) or the whole mesh (Mesh mode).
  A child element always beats an ancestor, so events are never dispatched twice.
- **Material group** — *what*. A set of faces sharing one physic material and one
  density, created by painting.

A hull is the intersection of one element and one material group. If a group has no
painted faces inside a given bone, no hull is produced for that pair.

### What the runtime does

1. Loads the baked set.
2. Creates one hidden child object per hull under the right bone, with an identity
   transform, and assigns a convex `MeshCollider`.
3. Builds a `Collider → hull` lookup table.
4. Places a `Dyc_Relay` on every `Rigidbody` that owns a hull.
5. Configures layers, self-collision and mass.

Then it stops. There is no `Update` work beyond an optional trigger poll and a
distance check for LOD.

<a id="sec-5"></a>
## 5. Installation and requirements

- Unity 2022.3 or newer.
- Copy `Assets/NekoDynamicCollision` into your project. There is nothing to
  configure; the assemblies are scoped by assembly definitions.
- Two assemblies:
  - `Neko.DynamicCollision.Runtime` — the component, relay, trigger poll, event
    structs and the integration facade. Never references `UnityEditor`.
  - `Neko.DynamicCollision.Editor` — the baker, hull maths, clustering, brush,
    gizmo, health check, presets and the window. Editor-only platform.

<a id="sec-6"></a>
## 6. The baker window

`NekoWorks → NekoDynamicCollision → Open Main Window` (`Cmd/Ctrl+Shift+D`).

| Tab | What it does |
|---|---|
| **Bake** | Source, mode, precision, bake/clear/rebuild, statistics, coverage |
| **Paint** | Group list with painted-triangle counts, brush settings, pose reset |
| **Gizmo** | What the Scene view draws and how |
| **Parts** | The element list — bone, include-children, event name, damage multiplier |
| **Materials** | Material groups, presets, pair-behaviour table, project physics audit |
| **Health** | Score out of 100, every issue, one-click fixes |
| **Settings** | Language, diagnostics, open baked folder, reset preferences |

The **Bake** tab carries the source, the collider shape, the precision and the bake
actions. **Exclusions** live in the component's Advanced foldout, and they are three
different things:

- **Excluded bones** — a listed bone *and its whole subtree* get no zone and no
  collider. This is where IK targets, bone tips and helper bones belong.
- **Excluded renderers** — a `SkinnedMeshRenderer` that is not baked at all: hair, a
  separate cloth renderer, props.
- **Exclude from bake** on a material group — its faces are left out of the bake
  entirely, so a cape or a harness gets no hitbox without changing its material.

The Expert window (`Bone precision`) carries the rarely-touched decisions in a folded
block at the top: collider shape, self-collision mode and LOD.

<a id="sec-7"></a>
## 7. Menu reference

Everything lives under a single top-level slot so that installing more NekoWorks
plugins never widens the menu bar.

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

Menu captions are localized at load time and on language change; the static English
strings in the attributes are the fallback if Unity's internal menu API is not
available in your version.

<a id="sec-8"></a>
## 8. Precision

One knob, four steps. Internally it expands to four values:

| Precision | Triangles per hull | Hulls per part | Seam overlap | Weight threshold |
|---|---|---|---|---|
| Coarse | 120 | 1 | 4.0 mm | 0.35 |
| Normal | 250 | 2 | 3.0 mm | 0.25 |
| Fine | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **Triangles per hull** caps how much geometry one hull may absorb. More triangles
  per hull means fewer, larger, looser hulls.
- **Hulls per part** is the target number of clusters per element. More clusters
  means a tighter fit and more colliders.
- **Seam overlap** inflates each hull outwards so that neighbouring regions overlap
  instead of leaving a gap. Overlap is safe (a hit is never missed, and same-frame
  de-duplication stops double events); a gap is not.
- **Weight threshold** discards vertices whose weight for the dominant bone is below
  the value. Higher means a tighter, more "rigidly correct" hull.

The vertex count of a hull can never exceed the cluster's unique vertex count, which
is capped at 250 — safely under the PhysX limit of 255. The health check flags any
hull above 255 in red.

### 8.1 Convex mode decomposes — past the built-in complex collider

In **convex** mode a concave mesh is cut into convex pieces along its concavities. That
is the same idea as the built-in complex collider: take any
mesh, produce convex parts. NDC keeps the breadth and raises the ceiling:

| | Built-in complex collider | NDC |
|---|---|---|
| Any mesh | yes | yes |
| Well optimised | yes | yes — background bake job, progress, cancel, per-bone budgets |
| Pieces respect joints | **no** — purely geometric, a shoulder can swallow an arm | **yes** — pieces carry bone labels from a weight field |
| Usable on a moving body | **no** — PhysX refuses a non-convex collider on a non-kinematic `Rigidbody` | **yes** — the output is a convex hull |
| Runtime cost | collider cooking at load | zero — baked to assets |
| Fallback | — | spatial clustering, so a degenerate mesh still gets a collider |

The Expert window reports whether the parts came from **decomposition** (cut along
concavities) or from **spatial clustering** (the fallback), so the difference is a number
and not a guess.

**How fine the cut is** is one slider — **Decomposition detail** — with the voxel size in
millimetres in the number field right below it. The two are two views of *one* number, so
they always agree: drag the slider and the field follows, type in the field and the slider
moves. There is no second setting to keep in sync.

- **Left** — a coarse voxel: fewer, larger pieces. Cheapest, and usually enough for a
  prop.
- **Right** — the finest voxel. The pieces track the surface, so the cost **matches the
  non-convex mode**: there is nothing finer to gain, only cost.

The precision table above then applies to *cluster fitting* — how tightly each piece is
fitted — not to how many colliders you get.

The same slider appears in both modes: convex decomposition and non-convex simplification
are the two ways to answer the same question, *how much detail do I want*.

### 8.2 Non-convex detail is one slider

Switch **Collider shape → Non-convex surface** and a **Surface detail** slider appears.

| Slider | Result |
|---|---|
| Far left | Heavily simplified surface — few triangles, visible faceting |
| Middle | A good trade: the shape reads correctly, the collider stays cheap |
| **Far right** | **No simplification at all** — the surface is taken from the mesh as-is |

The reason it is a slider and not a triangle count: "how many triangles per piece" cannot
be chosen sensibly without knowing how many the mesh has — 500 is coarse for a torso and
precise for a finger. The slider answers the only question a user can actually ask: *how
much do I care about the exact shape*. The far right position is not "almost exact", it is
exact: simplification is switched off entirely.

Small clusters are never simplified regardless of the slider — pulling a thin finger down
to a coarse grid collapses it to nothing, and an empty collider is worse than an expensive
one.

Both skin and mesh mode use the same slider.

<a id="sec-9"></a>
## 9. The brush

The brush does not "exclude" faces. It **tags** them, and tags drive the bake.

| Action | Effect |
|---|---|
| Left drag | Assign the current group to the triangles under the cursor |
| Shift + drag | Erase back to group 0 and clear the painted flag |
| Mouse wheel | Brush radius |
| `X` toggle (window) | Mirror every stroke across the object's local X = 0 |

Settings: radius, X-Ray (ignore back-facing triangles), mirror on X.

Requirements, enforced with an explicit message rather than a silent failure:

1. Play mode must be stopped.
2. The Animation window must not be previewing.

The rig does **not** need to be in bind pose. The brush raycasts against the mesh
in its **current** pose; because skinning never changes topology, triangle indices
map one-to-one and the labels stay correct.

If you want the rig in bind pose anyway, the **Reset to bind pose** button solves the
local transforms from `bindposes[i].inverse` and writes them back, with undo.

<a id="sec-10"></a>
## 10. Material groups and presets

Each group carries a `PhysicMaterial`, a density in kg/m³, an optional event name
and a damage multiplier.

**Presets.** 224 presets across 14 categories (Metal, Ceramic, Plastic, Glass, Wood,
Stone & Concrete, Rubber, Fabric & Leather, Body parts, Tissue & organs, Organic, Ice,
Food, Other) — see [Appendix A](#appendix-a-physic-material-presets).
Applying a preset creates a real `.physicMaterial` asset under `Baked/<Scene>/Materials/`,
so it can be referenced, diffed, put in Addressables and handed to an artist.

**The material forge.** A table row answers "what is this made of". A game usually
asks something else: "what is this *right now*". Dry steel, wet steel, rusty steel and
steel covered in blood are four different feels, and keeping them as separate rows
would mean 224 × 15 ≈ 3 400 rows nobody can maintain. So the forge multiplies a base
preset by a **surface condition**:

| | Conditions |
|---|---|
| Multipliers | Dry, Wet, Oiled, Bloody, Sweaty, Icy, Frozen, Dusty, Rough, Polished, Rusted, Worn, Charred |
| Layers | Clothed (lighter, grippier), Armoured (heavier, smoother) |

Friction is multiplied on both the static and the dynamic coefficient — otherwise a wet
surface would still be "sticky at the start". `Dry` is the identity and returns the
preset untouched, so there is never a `steel_dry` next to a `steel`.

**Body parts are derived, not typed.** `Body parts` and `Tissue & organs` are not rows
in the table at all. They are computed from a **tissue mix**: density is additive,
softness is additive plus a cushion term, and friction and bounce are derived from
softness — soft tissue grips more and bounces less, hard tissue does the opposite.
"Breast" is 80% fat + 10% muscle + 10% skin with a thick cushion; "skull" is 95% bone +
5% skin with none. That is why the numbers can be explained rather than asserted.

**Generating assets.** *Generate variant assets* writes one `.physicMaterial` per
condition into `Baked/<Scene>/Materials/`, so an artist or a designer can pick them
straight from the Project window without opening the plugin.

**Combine strategy.** The whole library uses `Multiply` for friction and `Maximum`
for bounce. Unity's combine priority is
`Average < Minimum < Multiply < Maximum`, so with this strategy any slippery surface
dominates the friction result and any bouncy material dominates the bounce result —
which is what people intuitively expect.

**Pair-behaviour table.** The Materials tab resolves every pair of groups in your
project using Unity's real priority rules and shows the value that will actually
apply, plus a plain-language verdict ("grippy / no bounce"). This is the fastest way
to answer "why is my ice not slippery".

**Auto-assign by name.** Fills every group from presets by matching the group name
against English, Chinese and Russian keywords, body parts included — a group called
"chest" becomes chest tissue, not "flesh".

**Honest limitation.** A `PhysicMaterial` has four numbers and two combine modes. It
cannot express rolling friction, anisotropic friction (velvet), viscosity, plastic
deformation, temperature or wear. Softness is an *input* to the generator, not a
hidden property: it is never stored in the asset, because there is nowhere to store it.
"Real-world parameters" here means a sourced lookup table and usable presets — not a
physical simulation.

<a id="sec-11"></a>
## 11. Coverage diagnostics

The Bake tab answers the question that is usually guesswork: **which triangles have
no hull at all?**

It takes the source mesh in bind pose and tests every triangle centroid against every
hull's planes, reporting:

- an overall percentage and a progress bar;
- a per-element breakdown;
- the list of uncovered triangles, drawable in the Scene view in red
  (**Show uncovered faces**).

Treat below ~95% as a problem: raise the precision, or check that the element bones
actually cover the whole skeleton.

<a id="sec-12"></a>
## 12. Collision health

A score out of 100 with every issue listed and, where possible, a one-click fix.

The checks include: nothing baked; hull over the PhysX vertex ceiling; hulls close to
the ceiling; degenerate clusters; triangles belonging to no element; the source mesh
changed since the last bake; material groups with no material, no hulls, or too many
fragmented hulls; a Hitbox/Trigger role with no interact layers; no `Rigidbody` in the
parent chain; rigidbody mass that is far too small or too large; ragdoll self-collision
fully on; dispatched events with no listener; and the project-level physics checks in
[Appendix B](#appendix-b-project-physics-checks).

<a id="sec-13"></a>
## 13. Events and integration

Every event carries a full context, so you never have to look anything up again:

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

Three ways to consume it:

1. **UnityEvent** — `onEvent` on the component, for code-registered listeners.
2. **String registry** — give an element or a group an event name and listen with
   `Dyc_Events.Register("Hit.Head", handler)`. Misspelled names raise no error, but
   the health check reports dispatches that nobody received.
3. **Static facade** — `Dyc_Api` for external tools:

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

Damage multipliers live on the element and the group and are multiplied together —
head ×4 is one number, not a layer of glue code.

**De-duplication.** Seam overlap means two adjacent groups can both touch the same
foreign collider in one frame. NDC dispatches at most one event per
`(element, other collider)` per frame, so partitioned materials do not double events.

<a id="sec-14"></a>
## 14. Trigger polling

Unity delivers trigger callbacks **per Rigidbody pair**, so a single ragdoll is one
rigidbody pair and the physics layer simply cannot tell you which bone entered a
volume. Giving every bone its own `Rigidbody` would destroy the zero-cost promise.

So partitioned triggers are sampled instead:

- Each element is tested with `Physics.OverlapBoxNonAlloc` over the world-space bounds
  of its colliders.
- Only actual triggers are considered, and never your own colliders.
- Elements are processed in slices: `elements / frames-per-pass` per frame.
- Enter and exit are diffed against the previous pass for each element.

**Semantics to remember:** this is sampling, not an event. A very fast pass can be
missed. Raise the sample rate, or use the **sweep margin** to expand the query box.

<a id="sec-15"></a>
## 15. Mass, self-collision and LOD

**Mass from density.** NDC knows every hull's volume, so it can compute mass properly:
`mass = hull volume × group density`, optionally normalised so the whole character
matches a target total mass. This removes the oldest hand-tuning chore in Unity
ragdolls. A single rigidbody receives the sum of the volumes of the hulls it owns.

**Self-collision.** `Ignore` (all pairs), `Adjacent` (same element, or ancestor and
descendant) or `On`. Ragdoll bones colliding with each other is a common source of
jitter, and `Adjacent` is the usual answer. Above 200 colliders the step is skipped
with a warning rather than blocking `Awake`.

**LOD.** `Disable` turns colliders off past a distance; `Reduce` keeps only the
largest hull per element. The check runs every fourth frame.

**Rigidbody.** Unity only delivers collision callbacks to the GameObject that owns the
`Rigidbody`. For a ragdoll, every bone already has one. For anything else, enable
**Auto-add Rigidbody** and NDC creates a kinematic one on the component's object.

<a id="sec-16"></a>
## 16. Localization

The window, the inspector, the health messages and the menu captions are localized
into **15 languages**:

`en` (built in) · `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- English is built into the assembly and is the fallback for any missing key, so a
  partially translated language degrades instead of breaking.
- Every other language is pure data in `Locale/<code>/strings.json` — adding one
  needs no recompile.
- Arabic and Hebrew are fully right-to-left: the layout mirrors rather than relying
  on `style.direction`, whose UI Toolkit support is incomplete and version-dependent.
- Change the language in the **Settings** tab. The window and the menu update
  immediately, no domain reload.
- The Settings tab also shows the resolved locale path and how many languages were
  found, so a packaging mistake is visible instead of silent.

<a id="sec-17"></a>
## 17. Directory structure

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
## 18. Uninstall

1. Remove the **Dynamic Collision** component from your prefabs and scenes.
2. Delete `Assets/NekoDynamicCollision`.

The baked assets live under `Baked/` inside the plugin folder and go with it. Nothing
is written outside the plugin folder, and the runtime contains no code that depends
on the editor half.

<a id="sec-19"></a>
## 19. Troubleshooting and FAQ

**Nothing collides, and no events fire.**
There is no `Rigidbody` in the parent chain. Unity only sends collision callbacks to
the object that owns the rigidbody. Enable **Auto-add Rigidbody**, or add one yourself.

**The hulls do not match what I see.**
The gizmo draws the **bind pose** by default — that is what was baked. Switch
**Bind pose** off in the Gizmo tab to see them in the current pose.

**"Hull has N vertices, over the PhysX limit of 255."**
Unity silently ignores an over-limit convex hull. Lower the precision one step; the
health check offers exactly that as a one-click fix.

**Hits are missed in places.**
Check the coverage percentage first. Below ~95% means real holes. Then check the
**seam overlap** for the precision step you are on.

**A paint stroke left a gap between two regions.**
That is the seam problem. Raise the precision (which lowers seam overlap) or paint a
little past the boundary. Same-frame de-duplication already prevents double events
from the overlap.

**Events fire twice for one hit.**
Two different elements were hit in the same frame, which is legitimate. If you truly
want one event per object pair, filter by `elementIndex` in your handler.

**I painted but nothing changed after baking.**
The labels are ignored when the triangle count does not match the source mesh —
usually after a re-import or a topology change. Re-paint, or bake first so the label
asset is created with the right size.

**The brush will not start.**
Play mode is running, or the Animation window is previewing. Both are shown as an
explicit reason in the Paint tab.

**My old brush work disappeared after updating.**
It should not: masks created before the painted-flag existed are migrated, and any
non-zero label is treated as painted. If a mask was cleared, re-paint and re-bake.

**Is the runtime cost really zero?**
In steady state, yes: hulls are assets, transforms are followed by the hierarchy, and
there is no mesh work at all. The only per-frame work is the optional trigger poll
and the LOD distance check.

**Can I have two Dynamic Collision components on one object?**
No, and it is blocked on purpose. Two components would create duplicate hulls over the
same faces, double the contacts and double the events. Partition with elements and
material groups instead.

<a id="sec-20"></a>
## 20. Contact

NekoAndreeva — see `package.json` for the repository URL.

---

<a id="sec-appA"></a>
## 21. Ordinary usage and the technical surface

### 21.1 NDC as a system collider

The design rule is that a developer who knows `Collider` and `Rigidbody` already knows
NDC, because NDC *creates* ordinary colliders: `MeshCollider`s on hidden children,
a `Rigidbody` on the object, standard messages, ordinary layers and physics materials.
`Physics.Raycast` and `Physics.OverlapSphere` need no changes at all.

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // add + build
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // a standard Unity message
void OnTriggerStay(Collider other) { }     // a standard Unity message
```

| Call | Meaning |
|---|---|
| `Find(go)` | The component, on the object or a parent |
| `Attach(go, generateNow)` | Add the component and build |
| `Build(go)` / `Rebuild(go)` | Build from the baked set, or generate at runtime |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | State, all hulls at once |
| `SetTrigger(go, v)` | `Collider.isTrigger` for every hull |
| `SetMaterial(go, pm)` | Immediate; does not survive a rebuild |
| `GetColliders(go)` / `ForEachCollider(go, a)` | The hulls, as ordinary `Collider`s |
| `SetReceiver(go, t)` | Also send standard messages to `t` |

**Only zoning is extra.** Zones, painted materials, LOD, events, mass from density and
the health check need the NDC API — they are the things a system collider cannot do.

**No bake step.** Tick **Advanced ▸ Build at startup when nothing is baked** (or call
`Attach`). Colliders are built per bone from the mesh at `Awake` — one convex hull per
bone. Baking remains the way to get zones, decomposition,
coverage and precision.

**Messages reaching your script.** Unity delivers `OnCollision*` to the object with the
`Rigidbody`. If your script sits elsewhere (a character root while the Rigidbody is on a
bone), set `Advanced ▸ Also send OnCollision*/OnTrigger* to` — the messages are then
forwarded with `SendMessage`, which costs nothing on frames without a collision.

### 21.2 Live update — the capability baking cannot replace

A baked hull glued to a bone is exact at bind pose and rigid afterwards. Under strong
deformation — a crouch, a squeezed limb, cloth pulled tight — the hull misreports the
surface. Live update rebuilds the hull from the **current** skinned pose.

Turn it on with **Advanced ▸ Live update** or `Dyc_Collision.EnableLiveUpdate(go)`.

| Setting | Default | Meaning |
|---|---|---|
| `liveUpdate` | off | Rebuild hulls from the current pose |
| `liveUpdateContinuous` | on | Keep going, or run one pass on request |
| `idleCpuBudgetMs` | 0.2 | Budget while the mesh barely moves |
| `activeCpuBudgetMs` | 1.0 | Budget while it moves fast |
| `meshUpdateThreshold` | 0.02 | Skip the pass below this movement (metres) |
| `maxColliderTriangles` | 5000 | Ceiling per collider, so one heavy bone cannot eat the budget |

The budget is chosen from how much the mesh actually moved, so a standing character is
charged the idle rate and a running one the active rate. Work that does not fit is
deferred to the next frame, and `OnUpdateYield` / `OnPassComplete` report elapsed
milliseconds.

**It does not rebuild everything every frame.** Three mechanisms keep the cost
predictable:

1. **Incremental.** Each cluster's centre is compared with the previous pass, and only
   clusters that actually moved are rebuilt. A soft body hanging from an anchor has a
   trembling hem and a nearly still middle — the middle costs nothing.
2. **Priority-ordered.** The queue is sorted by how far each cluster moved. If the
   budget runs out, it runs out on the calmest clusters — the ones where the
   inaccuracy is least visible. Without this, the budget would be spent on whatever
   happened to be first in the list.
3. **Budgeted by the clock**, not by cluster count: the per-frame cost does not grow
   with how many clusters the body has.

`LastDirtyCount` and `LastBuiltCount` report what the last pass actually did, which is
the honest way to see the saving.

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"pass done in {ms:F2} ms");
live.StopAfterPass();          // finish the current pass, then stop
live.UpdateNow();              // one full pass, outside the budget
```

**Requirements.** Hulls need `sourceVertices`, which is written by the bake;
re-bake an older character to enable live update. The runtime cost is real — it is the
one feature that contradicts "zero per-frame cost", which is exactly why it is off by
default.

### 21.3 Per-bone overrides

`Dyc_BoneProperties` goes on the bone itself (Add Component ▸ NekoWorks ▸ Dynamic
Collision ▸ Bone Properties):

| Field | Effect |
|---|---|
| `overrideMaterial` + `physicsMaterial` | This bone's hulls use that material |
| `overrideConvex` + `convex` | Hull instead of surface (or the reverse) for this bone |
| `overrideWeightThreshold` + `boneWeightThreshold` | Per-bone weight cutoff |
| `exclude` | No colliders for this bone |

Hanging it on the bone means it survives renames — it holds a reference, not a path.

### 21.4 Materials by source material

`Advanced ▸ Materials by source material` maps a source `Material` to a
`PhysicMaterial`. Hulls are attributed by the submesh they mostly come from, resolved at
bake into `Dyc_BakedSet.sourceMaterials`. Priority, highest first:

1. `Dyc_BoneProperties.physicsMaterial`;
2. the material association for the hull's source material;
3. the painted group's material.

### 21.5 Vertex exclusion map

`Advanced ▸ Exclusion map` reads a texture channel (R/G/B/A, with a threshold) and
excludes vertices whose channel value is at or above it. The brush marks *faces*, the
map marks *vertices* — they complement each other. The mesh needs UVs, the texture needs
**Read/Write Enabled**, and a triangle is excluded only when all three of its vertices
are.

### 21.6 Retarget skeleton

`Advanced ▸ Attach hulls to another skeleton` builds the hulls from this mesh but hangs
them on same-named bones of another root — the `RetargetSkeleton` case, for Puppet
Master and similar setups. Bones are resolved by relative path; a hull with no
same-named counterpart stays on its own skeleton and the bake report says how many did.

### 21.7 Soft mode — boneless, code- or solver-driven

Soft mode (`Mode → Soft`) is **not** a skinned rig. It is for a mesh with **no
skeleton** whose shape is produced by a solver or by code — NekoDynamicSoftbody and
similar. Nothing in soft mode reads bones; the mesh geometry is taken as-is.

**What the bake produces.** The mesh is split into numbered spatial clusters. Each hull
is built *relative to its cluster centre*, and the centre is stored as the cluster's
rest pose (`clusterRest`). That is what lets a frame translate **and rotate** the hull
as one piece.

**Without a solver.** Frames are created at their rest poses, so the hulls sit exactly
on the real mesh geometry and move with the object. The shape is right; there is simply
no dynamics. This is the intended degradation, not a failure — and it is what "computes
the real shape without NDSC" means.

**With a solver.** The solver pushes frames (`Push` → `Apply`) and takes over
completely, giving full simulation. An addressable push is not overwritten by the global
poll on the same frame, which matters as soon as more than one body exists.

**Live update also works without a skeleton.** `Dyc_LiveUpdate` reads a `MeshFilter`'s
CPU vertices as-is, so any code that deforms the mesh — a soft-body solver, a procedural
script, a custom deformer — drives accurate hulls with no plugin-specific glue. A hull
needs `sourceVertices`, which is written by the bake; re-bake an older asset.

**The honest limit:** deformation that exists only on the GPU (a vertex shader, GPU
skinning) cannot be read back on the CPU, so live update does not see it. Move the
deformation to the CPU, or keep the baked hulls.

### 21.8 Driving individual hulls

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`, `HullIndexOf`, `HullCount`, `BakedSet`, `Elements` and `Groups` are
public, so external tools can iterate and drive individual hulls without reflection.

---

### 21.9 Character collision LOD

This LOD is for **characters**, not for the scene. Ordinary collision LOD works on static
props; a character is a hundred hulls each, and on mobile it is the crowd — not the level
— that decides the frame budget.

Turn it on with **Expert settings ▸ Character collision LOD**, or set
`Advanced.collisionLod`.

| Setting | Default | Meaning |
|---|---|---|
| `collisionLod` | off | Disable part of the hulls by distance |
| `lodNearDistance` | 12 | Full detail up to this distance (m) |
| `lodFarDistance` | 40 | Beyond this, only the largest hulls stay |
| `lodFarHullCount` | 6 | How many hulls stay enabled in the far band |
| `lodHysteresis` | 1.5 | Hysteresis in metres, so a character on the boundary does not flicker |
| `lodRequireVisibility` | off | Also require the character to be on screen |
| `lodReference` | empty | Distance reference; empty means the active camera |

In the far band the hulls kept are the **largest by volume**, sorted once at build time.
A distant fighter still takes hits on the body and stops paying for the fingers. Disabling
a hull is a `Collider.enabled` flag — no rebuild, no re-cook.

**The camera is found automatically.** Not just `Camera.main`: that is hard-wired to the
MainCamera tag, while a real project draws from whichever camera is active — view
switching, third person, a camera inside a vehicle. NDC walks the enabled cameras and
takes the one with the **highest depth**, which is what the player actually sees. If none
is found it warns once and the LOD stays in the near band, rather than failing silently.

**Bring your own decision.** Implement the interface and NDC asks you instead of
measuring:

```csharp
public interface IDyc_LodProvider
{
    bool TryGetDistance(Transform target, out float distance);
}

Dyc_CollisionLod.Provider = myProvider;
Dyc_CollisionLod.Simplified += (target, far) => { /* ... */ };
```

Precedence: **external provider → explicit reference → active camera.**

NDC deliberately has **no scene manager**. Scene optimisers create one because they work
on a whole scene; NDC works on one object, and a scene-level object created just for it
would be an extra entity to keep alive. The interface is there for whoever wants to take
that decision over.

### 21.10 Scope and licensing

**What NDC deliberately does not do.** These are top-end features; adding them would mean
giving up the lightweight positioning, so they are out on purpose rather than missing by
accident:

- GPU-skinned collision — collision that only exists in a vertex shader cannot be read
  back on the CPU, so live update does not see it
- per-vertex cloth-grade collision
- solver-grade self-collision accuracy
- multi-threaded simulation

**Runtime footprint.** The runtime assembly has **zero dependencies** — no Burst, no
Jobs, no native libraries. All the heavy code sits in an Editor-only assembly definition
and never ships in a player build. The bundled decomposition library lives under
`Editor/` and is loaded by the editor only.

**Localisation.** 14 locales ship with the package, each carrying the full key set,
including full right-to-left layout for Arabic and Hebrew.

**Licence.** MIT — see `LICENSE`. Bundled third-party components are listed in
`THIRD PARTY NOTICES.md`, which takes precedence for those components.


---

## Appendix A. Physic material presets

224 presets, 14 categories. Values are sourced engineering approximations, mapped onto
Unity's four-parameter model. `Body parts` and `Tissue & organs` are derived by the
forge from a tissue mix rather than typed into the table.

| Category | Count | Presets |
|---|---|---|
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

Every preset also carries a **density** in kg/m³ for automatic mass, and rubber-family
presets carry the `bounceThreshold` they need in order to bounce at all.

Each of the 224 can be multiplied by the 14 non-identity surface conditions, so the
forge reaches roughly 3 100 usable materials without a single extra table row.

<a id="sec-appB"></a>
## Appendix B. Project physics checks

The health check audits the project's `Physics` settings, because a material preset
cannot fix a global setting:

| Setting | Why it matters |
|---|---|
| `bounceThreshold` | Impacts slower than this never bounce. At the Unity default of 2, a rubber preset looks broken. Lower it to 0.2–0.5 to use elastic materials. |
| `defaultSolverVelocityIterations` | At 1, stacks and fast impacts jitter or tunnel. 2–4 is usually better, and it is a common root cause of ragdoll jitter. |
| `gravity` | If it is not −9.81, every mass and impulse intuition derived from −9.81 is off by the same factor, and density presets need correcting. |
| `defaultContactOffset` | A wide contact gap makes thin objects look like they float. |

Applying the recommended values is a one-click action from the Materials tab or the
health check.
