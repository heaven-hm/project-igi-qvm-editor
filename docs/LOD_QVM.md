# What `lod.qvm` is, and what editing it can and cannot do

Definitive answer to [issue #7](https://github.com/heaven-hm/project-igi-qvm-editor/issues/7),
grounded in a decompilation of the retail `IGI.exe`, an independent open-source
reimplementation, and measurements over the shipped game data.

> **Short answer.** `lod.qvm` does not contain any 3D geometry and is not a
> graphics-quality slider. It is a compiled QScript (`lod.qsc`) that the game
> runs as a task container to build two kinds of task: one level-wide
> `LODSettings` task (the fallback quality/percentage rules) and one
> `ModelLODSettings` task **per model**, which holds five distances in metres:
> when to step down to each lower-detail mesh and when to stop drawing the
> model. The five meshes themselves are separate `.mef` files (`name_1` …
> `name_5`). Raising the distances in `lod.qvm` delays the switch to the
> low-detail meshes, but it cannot make a distant object *more detailed* than
> the meshes that exist — for that the `.mef` models must be improved.

## 1. Primary evidence

| Claim | Source |
| --- | --- |
| The game loads the compiled script at `LOCAL:lod.qvm` / runs `LOCAL:lod.qsc` through a container that creates the LOD tasks | [IGI-RE `src/animtrig/lodsettings.c:190-200`](https://github.com/neonixmoddingteam/IGI-RE/blob/master/src/animtrig/lodsettings.c), hook `0x4EDC00`; [OpenIGI `Igi1ModelLodTable.cs:8-15`](https://github.com/OpenIGI/OpenIGI/blob/main/src/Game/OpenIGI.Presentation.Igi1/World/Igi1ModelLodTable.cs) |
| Two distinct task types: level `LODSettings` with 8 floats, and per-model `ModelLODSettings` with `char name[16]` + `float dist[5]` | [IGI-RE `lodsettings.c:23-41`](https://github.com/neonixmoddingteam/IGI-RE/blob/master/src/animtrig/lodsettings.c), registered at `:94-109` |
| Parameter order is name + `dist[0..4]`, range 0..1000 | [IGI-RE `lodsettings.c:104-109`](https://github.com/neonixmoddingteam/IGI-RE/blob/master/src/animtrig/lodsettings.c) |
| The decompiled script's authored parameter names are `Distance to LOD 1` … `Distance to LOD 4`, `Distance to cutoff` | [OpenIGI `Igi1ModelLodTable.cs:48-58`](https://github.com/OpenIGI/OpenIGI/blob/main/src/Game/OpenIGI.Presentation.Igi1/World/Igi1ModelLodTable.cs) |
| The engine clamps the five distances to be non-decreasing, finds the model, and stores `dist[i] * 4096` on it | `ModelLODSettings_Changed`, [IGI-RE `lodsettings.c:156-173`](https://github.com/neonixmoddingteam/IGI-RE/blob/master/src/animtrig/lodsettings.c), hook `0x4EDB60` |
| `lod_select` uses that table when present: `d = fov * ((dist - r) * k)`; `d > dists[4]` means "do not draw"; otherwise scan buckets to pick LOD 0..4. The complexity/percentage branch is used **only when there is no authored table** | [IGI-RE `src/lod/mesh3d.c:155-207`](https://github.com/neonixmoddingteam/IGI-RE/blob/master/src/lod/mesh3d.c) |
| Level-wide `LODSettings` converts three detail and three cull percentages by ×0.02, keeps `blend` raw and scales `range` by 4096; defaults are blend 1.0 / range 4.0 | [IGI-RE `lodsettings.c:113-138`](https://github.com/neonixmoddingteam/IGI-RE/blob/master/src/animtrig/lodsettings.c) |
| LOD chains are named `aaa_bb_1` … `_5`: start from the requested name, increment its **last character**, stop at the first miss, at most 5 levels. The `100/(i*i)` distances written by `VirModel_InitLods` are never read | [`docs/parity/LOD_CHAINS.md`](https://github.com/heaven-hm/project-igi-editor/blob/master/docs/parity/LOD_CHAINS.md) §1-2, tracing `src/model/model.c:145-178` |
| Measured on the 14 retail bundles: 6605 model names, 3981 (60.3 %) with a chain longer than one; a 5-level pine tree's meshes shrink 33,888 → 1,824 bytes across `_1`..`_5` | [`LOD_CHAINS.md`](https://github.com/heaven-hm/project-igi-editor/blob/master/docs/parity/LOD_CHAINS.md) §3, and [`project-igi-converter/docs/game_model_naming.md`](https://github.com/heaven-hm/project-igi-converter/blob/master/docs/game_model_naming.md) |
| Measured on retail `LOD.QVM`: 462 `ModelLODSettings` lines covering 450 distinct model names (all ending `_1`); 31 name models absent from shipped bundles, which the engine applies to its `"missing"` fallback | [`LOD_CHAINS.md`](https://github.com/heaven-hm/project-igi-editor/blob/master/docs/parity/LOD_CHAINS.md) §7 (`[lod-qvm]` test line) |

These are primary-source claims: hooks are addresses in the retail executable,
and the two independent implementations (`IGI-RE` C decompilation and OpenIGI
C# runtime) agree on the task shapes and the ×4096 conversion.

## 2. What the file contains

A decompiled `lod.qvm` is a QScript of `Task_New(...)` calls. The two relevant
shapes are:

```text
Task_New(-1, "LODSettings", "",
         <splineDetail>, <rigidDetail>, <boneDetail>,
         <splineCull>, <rigidCull>, <boneCull>, <blend>, <range>);

Task_New(-1, "ModelLODSettings", "300_01_1", "300_01_1",
         70.0, 170.0, 300.0, 350.0, 500.0);
```

For a `ModelLODSettings` entry the engine reads a name plus five floats
(`gModelLODSettingsParams`, 6 fields, `lodsettings.c:95,104-109`). Decompiled
scripts name them as follows:

| Field | Meaning |
| --- | --- |
| `Model` | Name of the **LOD 1** mesh, e.g. `300_01_1`; must resolve to a loaded model |
| `Distance to LOD 1` | Start of the first bucket (usually `0.0`) |
| `Distance to LOD 2` | Switch to `300_01_2` |
| `Distance to LOD 3` | Switch to `300_01_3` |
| `Distance to LOD 4` | Switch to `300_01_4` |
| `Distance to cutoff` | Past this depth the object is not drawn at all (`lod_select` returns -1) |

The comparison depth is not the raw camera distance: it is
`fov * ((dist - radius) * k)`, where `dist` is to the mesh centre and `k` is a
scene scale (`mesh3d.c:173`). Note the engine's quirk at `mesh3d.c:177-181`:
when the **first** authored distance is exactly zero, the scan starts at LOD 1
(the second mesh), not LOD 0 — so a zero first value does not mean "LOD 1 is
drawn near the camera".

Every authored table is **forced ascending** at load time
(`lodsettings.c:162-165`), so writing a smaller distance after a larger one is
silently corrected rather than producing a reverse switch. Distances are
authored in metres but stored multiplied by 4096 (world units).

`LODSettings`, despite its similar name, is a different task: it sets the
level-wide percentages used by the *complexity-ratio* fallback for models that
have **no** `ModelLODSettings` entry. Changing it will not affect any of the
450 models with an authored table.

## 3. Direct answers to the question

**"What's the function of lod.qvm?"** It is the game's authored LOD
configuration: a compiled script that assigns per-model switch distances and
a cull distance, plus a global fallback for unconfigured models.

**"Is it related to rendering quality/distance?"** Yes — to *distance*, and to
which pre-existing detail level is drawn. It does not hold textures, meshes or
polygon counts.

**"Can I make distant objects render better by patching it?"** Partly:

1. **Raising the `Distance to LOD 2/3/4` values** keeps the higher-detail mesh
   for longer. **Raising `Distance to cutoff`** stops distant objects from
   disappearing. Both are legitimate edits, and the engine will accept them
   as long as each value stays ≥ the previous one and ≤ 1000 (`lodsettings.c:
   51-52`, `:105-109`).
2. The *quality ceiling* is the mesh at the selected LOD. The shipped lower
   LODs are drastically simpler (the 940_01 tree example: 33,888 bytes at `_1`
   versus 1,824 at `_5`), so a large distance only keeps a low-polygon mesh on
   screen longer. Improving distant appearance therefore requires better
   `aaa_bb_2.mef` … `_5.mef` assets, which is the maintainer's advice in the
   issue thread. This is a 3D modelling task, not a QVM-script task.
3. A model name in `lod.qvm` must match a mesh the game actually loads. 31
   authored entries in retail `LOD.QVM` point at models that no shipped bundle
   contains; the engine resolves them to the `"missing"` fallback model
   (`ModelLODSettings_Changed` → `VirModel_Find`). Adding distance rows for new
   models only works if those model files exist.
4. Compiling the edited script back to QVM must use the correct game version:
   this editor selects QVM v5 (`v0_85`) vs v7 (`v0_87`) via
   `QUtils.ReadQVMVersion` and `QCompiler.QCompile`. IGI 1 and IGI 2 are not
   interchangeable.

## 4. How this editor handles `lod.qvm`

Before this change the QVM Editor treated `lod.qvm` like every other QVM:
`QCompiler.QDecompile` runs `decompile.bat` to turn it into QSC text, which is
loaded into Scintilla; `QCompiler.QCompile` turns the edited QSC back into QVM.
The only LOD-specific affordance was a one-line `LODSettings` calltip
("Level Of Details Settings") in `QUtils.gameObjectsInfo`, and the generic
model-name tooltip, which resolves `\d{3}_\d{2}_\d{1}` ids against
`IGIModels.json`. Nothing explained the five numbers, so the file looked like
opaque script — exactly the reporter's complaint.

This change adds a dependency-free reader (`QVMEditor/Utils/LodQvmInfo.cs`) and
wires it into the existing double-click calltip and load path:

* Opening a script with LOD tasks shows a summary (number of authored tables,
  presence of the level-wide task).
* Double-clicking `ModelLODSettings` or `LODSettings` shows a precise
  explanation of the parameters.
* Double-clicking an authored model name (for example `300_01_1`) shows that
  model's five distances in metres **and** the engine's clamped values in raw
  world units.
* `QUtils.gameObjectsInfo` now carries accurate LOD task descriptions.

Ordinary scripts are unaffected: non-LOD tokens fall back to the previous
behaviour.

## 5. Verification

Host-side tests (no game files, no Windows required):

```sh
sh tests/run-lod-tests.sh
```

They cover task parsing (including a deliberately incomplete row being
skipped), level settings, the engine's monotonize-and-scale rule, all three
calltip paths, and detection of non-LOD scripts. On this host: 21 assertions,
all passing.

The whole WinForms solution also compiles with the project's existing
`QVMEditor.csproj` (Mono `msbuild QVMEditor.sln /p:Configuration=Release`, with
the two NuGet packages from `packages.config` restored) — the build produced
`QVMEditor.exe` with no errors. The UI was **not** launched: this host has no
Windows game installation, so no live-game or screenshot claim is made. The
engine behaviour being mirrored is not inferred from the test data — each rule
cites its hook and source line in §1.

## 6. Scope note

This repository is archived and upstream development moved to
[project-igi-converter](https://github.com/heaven-hm/project-igi-converter) and
[project-igi-editor](https://github.com/heaven-hm/project-igi-editor). The
format findings above cite those current repositories because they contain the
retail-engine tracing and measured corpus data; the code change here is kept
local to the archived editor so the answer is available where the question was
asked.
