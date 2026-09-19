# Neko Dynamic Collision (NDC)

![Neko Dynamic Collision](Documents/cover.png)

Baked collision for Unity characters and props. NDC turns a skinned character or a static
mesh into a set of per-bone, per-region **convex hulls** — and does all the expensive work
in the **Editor**, so the runtime only loads and routes.

The goal is simple to start and deep when you need it: four controls and a Bake button
get a character working, and the technical surface — decomposition kernel, budgets,
per-bone overrides, live update, collision LOD — is one switch away.

## What this is, and what it is not

NDC aims at the middle and upper-middle of the market: **breadth comparable to the
established tools, at a fraction of the runtime cost**, with a ceiling that reaches
mid-high end. It is not aimed at the top end, and pretending otherwise would mean giving
up the thing that makes it worth using.

| | Built-in complex collider | Offline decomposition tools | NDC |
|---|---|---|---|
| Any mesh | yes | yes | yes |
| Well optimised | yes | yes | yes — background bake, progress, cancel, per-bone budgets |
| Pieces respect joints | **no** | **no** — purely geometric | **yes** — pieces carry bone labels from a weight field |
| Usable on a moving body | **no** | **no** | **yes** — the output is a convex hull |
| Runtime cost | collider cooking at load | cooking at load | zero by default — baked to assets |
| Fallback when it fails | — | — | spatial clustering, so a degenerate mesh still gets a collider |

See [Deliberately not done](#deliberately-not-done) for the top-end features NDC chooses
not to have.

## Highlights

- **Works like a system collider.** `using NekoDynamicCollision;` and you are done: NDC
  generates ordinary `Collider`s on ordinary GameObjects, so standard
  `OnCollisionEnter/Stay/Exit`, `OnTriggerEnter/Stay/Exit`, `GetComponent<Collider>()`,
  layers, physics materials, `Physics.Raycast` and `Physics.OverlapSphere` all work
  without a line of NDC-specific code. Only zoning, painted materials, LOD and events
  need the NDC API. `Dyc_Collision.Attach(gameObject)` is the whole quick path, and
  **`generateOnStart`** builds the colliders at startup when nothing has been baked — no
  editor step required.
- **Expert mode switch.** Off: four controls and a Bake button, with the technical
  sections collapsed. On: budgets, decomposition kernel, per-bone overrides, material and
  exclusion maps. One screen, one switch — nothing is hidden, only folded, so the two
  views can never drift apart.
- **Zero runtime cost by default** — no `BakeMesh`, no mesh building, no grouping at
  runtime. The hulls are baked to assets; the runtime instantiates and routes.
- **Opt-in live update, budgeted** — rebuild hulls from the *current* skinned pose inside
  a CPU budget. Incremental (only clusters that actually moved) and priority-ordered (a
  budget that runs out is spent on the clusters that changed most). Off by default.
- **Character collision LOD** — disable part of the hulls by distance, keeping the
  largest. The active camera is found automatically; no declaration and no scene manager.
  An interface (`IDyc_LodProvider`) lets your own system or a future scene optimiser take
  the decision over.
- **Convex hulls or the actual surface** — the default is convex, the only shape PhysX
  accepts on a moving body. Switch **Collider shape → Non-convex surface** and each bone
  is baked as its real surface instead, so joints have no gaps at all. A **Surface
  detail** slider runs from coarse to **no simplification at all**.
- **Decomposition, bone-aware** — a concave mesh is cut into convex pieces along its
  concavities, except the pieces carry bone labels from a weight field,
  so a piece never crosses a joint. Works for bone-less meshes too.
- **Soft bodies: boneless, solver-driven** — soft mode bakes a skeleton-less mesh into
  numbered spatial clusters with rest poses. A solver's frames drive them for full
  simulation; with no solver the hulls sit exactly on the real geometry. Live update
  reads a `MeshFilter` directly, so any code-driven deformation drives accurate hulls.
- **Spatial clustering, not index-order chunking** — triangles are grouped by position,
  so a hull never wraps air.
- **Per-region physic materials** — paint faces with a brush, and one object carries a
  soft material on one region and a hard one on another, produced in a single bake.
- **Built-in partition events** — per-bone, per-material-group routing with damage
  multipliers, same-frame de-duplication, and a string registry. No glue code.
- **Per-bone overrides** — `Dyc_BoneProperties` hangs on the bone itself and overrides
  material, convexity, weight threshold, or excludes that bone. No path lists to keep in
  sync.
- **Materials by source material** — a `Material → PhysicMaterial` association list for
  multi-material meshes. A bone's own override wins over it.
- **Vertex exclusion map** — a texture channel plus a threshold excludes vertices. The
  brush marks *faces*, the map marks *vertices*.
- **Retarget skeleton** — build the hulls from this mesh but hang them on same-named bones
  of another root (Puppet Master and similar).
- **Coverage diagnostics** — see exactly which triangles no hull covers, as a percentage
  and as highlighted faces.
- **Collision health check** — 20+ checks with a score out of 100 and one-click fixes.
- **224 material presets** in 14 categories — metals, ceramics, plastics, glass, wood,
  stone, rubber, fabrics, body parts, tissue and organs, ice, food, other — each with
  real-world values, a pair-behaviour table that reproduces Unity's own combine priority,
  and automatic mass from density.
- **Material forge** — a preset is what a thing is *made of*; the forge says what it is
  *right now*. Multiply any base by a surface condition, or derive a body part from its
  tissue mix. One click writes the whole variant set out as real `.physicMaterial` assets.
- **14 locales**, including full right-to-left for Arabic and Hebrew. Every locale carries
  the full key set.
- **Trigger polling mode** — for hit detection without a physics response: hulls become
  triggers and are polled on your schedule instead of relying on contact events.
- **Editor-only tooling, zero runtime dependencies** — all the heavy code sits in an
  Editor-only assembly definition, and the runtime assembly references nothing but
  UnityEngine: no Burst, no Jobs, no native libraries. The bundled decomposition
  library lives under `Editor/` and is loaded by the editor only.

## Requirements

- Unity 2022.3 or newer
- Editor-only tooling; the runtime assembly is a plain, dependency-free MonoBehaviour set
  that ships in player builds.

## Quick start

**The ordinary way — no baking:**

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);   // colliders exist now
}

void OnCollisionEnter(Collision c)      // a standard Unity message
{
    // ...
}
```

Or add the **Dynamic Collision** component and tick **Expert settings ▸ Build at startup
when nothing is baked**. Colliders are built per bone from the mesh at `Awake`.

**The tuned way — bake:**

1. Select your character (or prop) and add the **Dynamic Collision** component.
   It finds its own source mesh.
2. Press **Bake**. Hulls appear in the Scene view as a bind-pose wireframe.
3. Open the baker (`NekoWorks → NekoDynamicCollision → Window → Open Main Window`) to
   check coverage, paint material regions, and run the health check.

Baking is what buys zones, voxel decomposition, painted materials, coverage numbers and
the health check. It is not a prerequisite for collisions.

Full documentation: [Documents/GUIDE.en.md](Documents/GUIDE.en.md) — also available in 14
other languages in [`Documents/`](Documents).

## What it does today

**Ordinary usage, like a system collider.** `Dyc_Collision` (`Attach`, `Build`,
`Rebuild`, `SetEnabled`, `SetTrigger`, `SetMaterial`, `GetColliders`, `ForEachCollider`,
`SetReceiver`, `EnableLiveUpdate`), `generateOnStart`, a `collisionReceiver` for scripts
that do not sit on the Rigidbody object, and an `isTrigger` switch independent of the NDC
Trigger role.

**Live update** (`Dyc_LiveUpdate`) rebuilds hulls from the current shape inside a CPU
budget: incremental, priority-ordered, clock-budgeted, zero allocation per frame.
`idleCpuBudgetMs` / `activeCpuBudgetMs`, `meshUpdateThreshold`, `maxColliderTriangles`,
`OnUpdateYield` / `OnPassComplete`, `StartUpdating` / `StopUpdating` / `StopAfterPass` /
`UpdateNow`. `LastDirtyCount` / `LastBuiltCount` report what the last pass actually did.

**Character collision LOD** (`Dyc_CollisionLod`) disables part of the hulls by distance,
keeping the largest ones in the far band. Hysteresis stops a character on the boundary
from flickering. The active camera is resolved automatically by depth; `lodReference`
overrides it. `IDyc_LodProvider` and the `Simplified` event let an external system — your
own script, or a future scene optimiser — take the decision over.

**Soft bodies: boneless, solver-driven.** Soft mode bakes a skeleton-less mesh into
numbered spatial clusters with rest poses (`clusterRest`), each hull built relative to
its cluster centre so a frame can translate *and* rotate it. A solver's frames drive them
for full simulation; with no solver the hulls sit exactly on the real geometry.

**Per-bone control.** `Dyc_BoneProperties` overrides material, convexity, weight
threshold and exclusion per bone. A `Material → PhysicMaterial` association list covers
multi-material meshes, and a vertex exclusion map complements the brush. Hulls can be
retargeted onto another skeleton.

**Convex mode decomposes.** A concave mesh is cut into convex pieces along its
concavities, with bone labels so a piece never crosses a joint. Spatial clustering is the
fallback. The Expert window reports which of the two produced the parts
(`decomposed` / `clustered`).

**Non-convex mode has a detail slider.** One slider from "coarse" to **no simplification
at all**; the far right takes the surface from the mesh as-is.

**Mesh mode decomposes too.** A concave static object is cut into convex pieces instead of
being wrapped by hulls that bridge the concavity. The voxel kernel works without a
skeleton.

## Deliberately not done

These are top-end features. Adding them would mean giving up the lightweight positioning,
so they are out on purpose rather than missing by accident:

- GPU-skinned collision (collision that only exists in a vertex shader cannot be read back
  on the CPU, so live update does not see it)
- per-vertex cloth-grade collision
- solver-grade self-collision accuracy
- multi-threaded simulation

If you need these, NDC is the wrong tool — and knowing that up front is more useful than
finding out after integrating.

## Menu reference

Everything lives under one top-level slot, so installing more NekoWorks plugins
never widens the menu bar:

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

The second level exists so that a dozen entries do not read as a wall: the groups split
the list by what the action *does* — window, baking, gizmo, tools, diagnostics, help.
Group names stay English (they are part of the `[MenuItem]` path, a compile-time
constant); the leaf captions are localized.

Inside the Expert window the top panel is grouped the same way — captioned boxes for
Actions, Decomposition precision, Zones and separation, Kernel and budget, View.

## Concepts in one table

| Concept | What it is |
|---|---|
| **Element** | A partition: one bone (Skin mode) or the whole mesh (Mesh mode). Nearest ancestor wins. |
| **Material group** | A set of faces sharing one physic material and one density. Painted with the brush. |
| **Hull** | The intersection of one element and one material group, clustered spatially and cooked convex. |
| **Precision** | The single knob: maps to triangles-per-hull and hulls-per-part. |
| **Shape** | Convex hulls (default, works on moving bodies) or the actual surface (no joint gaps, kinematic only). |
| **Exclusion** | A bone subtree, a renderer, a whole material group, or a vertex mask left out of the bake. |
| **Bind pose** | Hulls are baked in bone-local space and never re-cooked. |
| **Soft cluster** | A numbered piece of a skeleton-less mesh with a rest pose, drivable by a solver or by code. |
| **LOD provider** | An external answer to "is this object far?" — NDC asks, and otherwise uses the active camera. |
| **Preset** | What a thing is made of: 224 sourced rows, from steel to breast tissue. |
| **Condition** | What it is right now: wet, oiled, bloody, frozen… The forge multiplies a preset by it. |
| **Tissue** | The generator's input for body parts. Softness is derived, not stored — `PhysicMaterial` has nowhere to keep it. |

## Uninstall

Delete `Assets/NekoDynamicCollision`. Baked assets live under
`Assets/NekoDynamicCollision/Baked/` and go with it. Remove the component from your
prefabs first if you want a clean scene.

## License

MIT. See `package.json` for author and repository details.
