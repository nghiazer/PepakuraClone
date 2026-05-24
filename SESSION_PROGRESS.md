# 4H-Unfolder — Session Progress Log

> **Last updated:** 2026-05-24 (session 22 — comprehensive review, critical bug noted, publish v0.0.2.A release)  
> **Branch:** `feat/paper-model-unfolder`  (PR #1 open against `main`)
> **Target framework:** .NET 8 / WPF  
> **SDK required:** `winget install Microsoft.DotNet.SDK.8`
> **History archive:** see [`BUGS_HISTORY.md`](BUGS_HISTORY.md) for all prior bug/tech-debt records

---

## Quick-start

```bash
cd D:\CODING\UNFOLD
dotnet restore
dotnet build
dotnet run --project src/FourHUnfolder.App
dotnet test tests/FourHUnfolder.Tests
```

---

## Architecture

```
Domain → Geometry → Application → Infrastructure → App
```
No circular dependencies. Domain has zero external dependencies.

---

## Complete Feature List

### Core pipeline
| Step | Class | Notes |
|------|-------|-------|
| OBJ load | `ObjMeshLoader` | v/vt/f, multi-material MTL (newmtl/usemtl/map_Kd), fan-triangulation |
| Multi-format | `AssimpMeshLoader` + `MultiFormatMeshLoader` | 3DS, STL, DXF, LWO/LWS, FBX, COLLADA, PLY via AssimpNet 5 |
| Dual graph | `DualGraphBuilder` | Dihedral-angle weights; zero-area face guard |
| MST | `KruskalMstBuilder` | Kruskal + path-compressed Union-Find |
| Edge marking | `EdgeMarker` | Fold / Cut / Boundary |
| Unfold | `UnfoldEngine` | BFS circle-circle apex; disconnected components |
| Overlap | `OverlapDetector` | AABB pre-check + SAT |
| Tabs | `GlueTabGenerator` | Trapezoid/Rectangle/Triangle; side-angle param; alternate-flap |
| Pieces | `PieceComputer` | Union-Find connected components |
| SVG | `SvgExporter` | Per-face affine texture; edge-dedup; grayscale |
| PDF | `PdfExporter` | Multi-page; fold/cut/tab lines; page labels via PdfSharp.Standard |

### UI features
- Split 3D/2D viewport; 3D picking; right-click Detach/Attach
- Bidirectional 3D↔2D sync
- Interactive 2D canvas: drag, rotate ±90°, flip H, lasso multi-select
- Middle-mouse pan, scroll zoom, snap-to-grid
- **Piece outline merging** — boundary polygon per piece replaces individual triangle silhouettes
- **Edge-Edit mode** (✏): hover highlight, LMB attach/detach; color in Settings
- **Rotate-by-Point mode** (⊙): pivot → handle → live rotation; undoable
- **Auto-align edge** — double-click edge → snap to nearest 90°; undoable
- **Parts alignment** — 6 toolbar buttons: Align L/R/T/B/Center-H/V; undoable
- **Edge ID labels + glue arrows** on cut edges (pair numbers 1,2,3…); color in Settings
- **Multi-texture** — per-material texture slots; TextureDialog with material list; per-face texture in 2D
- **Save/Load `.4hu`** — self-contained ZIP bundle (mesh + texture + state)
- **Unsaved changes warning** on Load/Open/Close
- **Strip-packing auto-arrange** — sort by area desc, try 90° rotation
- Export SVG + Export PDF (📑 toolbar button)
- Undo/Redo (Ctrl+Z/Y) for all edit operations

### Settings (4-panel dialog)
| Section | Key options |
|---------|------------|
| 3D View | Background, display mode, face/back color, opacity, edge overlay, lighting, camera |
| 2D View | Canvas/paper color, grid, fold/cut lines, glue tabs, face numbers, edge IDs, snap |
| Print | Margin, bleed, SVG scale, tab shape + angle + depth, alternate flaps, grayscale |
| General | Display unit: mm / inch |

---

## Build & Test Status

| Item | Result |
|------|--------|
| `dotnet build 4H-Unfolder.sln` | ✅ 0 errors, 7 warnings (NuGet NU1603 only) |
| `dotnet test` | ✅ 34 / 34 passed |
| `dotnet run --project src/FourHUnfolder.App` | ✅ App mở, không crash |
| Published `4H-Unfolder.exe` v0.0.2.A (win-x64, self-contained) | ✅ Session 22 |
| **3D multi-material texture** | ⚠ CRITICAL — bug still present (see below) |
| Edge hit zone (8px transparent Line on top) | ✅ Session 20 |
| `RebuildMaterialSlots` on project restore | ✅ Session 21 |
| `BuildWpfModel` absolute UV viewport | ✅ Session 21 |

---

## ⛔ CRITICAL BUG — 3D Multi-material Texture (unresolved)

**Symptom:** Loading a multi-material OBJ → Unfold → 3D viewport shows wrong textures (e.g. solid blue body + wrong-texture extremities). 2D canvas is correct.

**Root causes identified (session 22 review):**

| # | Location | Description |
|---|----------|-------------|
| C-1 | `MainViewModel.CommitPreview` | Calls `BuildWpfModel(_currentMesh, tex)` **without** `_materialBitmaps` → after any preview Apply/Cancel, the 3D model reverts to single-texture for ALL material groups |
| C-2 | `MainViewModel.EnterPreview` | Same: calls `BuildWpfModel(_currentMesh!, tex)` without `_materialBitmaps` |
| C-3 | `MainViewModel.RebuildMaterialSlots` | For multi-material meshes, `_materialBitmaps[i]` may be `null` if `MaterialTexturePaths[i]` is null/missing → falls back to `singleTexture` which could be a different material's texture |

**Not yet fixed — needs dedicated session.**

---

## Session 22 — Changes

| Item | Detail |
|------|--------|
| **Critical bug noted** | 3D multi-material texture still broken; root causes C-1/C-2/C-3 documented above |
| **Comprehensive review** | Full source review — see "Remaining Tech Debt" table for all findings |
| **Release v0.0.2.A** | Published win-x64 self-contained EXE; git tag `v0.0.2.A` created |

## Session 21 — Changes (kept)

| Item | Detail |
|------|--------|
| **3D absolute UV** | `BuildWpfModel` uses `BrushMappingMode.Absolute` + `Viewport=(0,0,1,1)` + `TileMode.Tile` on ImageBrush |
| **RestoreProjectState** | Added `RebuildMaterialSlots(_currentMesh)` call so project load populates `_materialBitmaps` |
| **SetMaterialTexture** | Added `OnPropertyChanged(nameof(Canvas2DTexture))` to force 2D canvas rebuild on slot change |

## Session 20 — Changes (kept)

| Item | Detail |
|------|--------|
| **3D multi-material** | `BuildWpfModel` now groups faces by `MaterialId`; creates one `GeometryModel3D` per material with its own texture from `_materialBitmaps`; single-texture fallback for unmatched materials |
| **3D hit-test fixed** | Added `_geoFaceIds` dict (geometry → faceId list); `ResolveHitFaceId` maps local vertex index to global face ID; `MainWindow.HitTestFace` uses it |
| **SetMaterialTexture** | Now rebuilds `MeshModel` with per-material bitmaps after TextureDialog changes any slot |
| **Edge hit zone** | Added `EdgeTag` class; each edge now has a thin visual `Line` (`IsHitTestVisible=false`) + a transparent `StrokeThickness=8` hit-zone Line on top; all event handlers updated |
| **Startup note** | Startup latency is HelixToolkit 3D renderer + .NET JIT cold start — use published Release build for best startup time; no code regression |

---

## Remaining Tech Debt

| ID | Priority | Description |
|----|----------|-------------|
| **CRITICAL-3D-TEX** | 🔴 Critical | 3D multi-material texture wrong — `CommitPreview`/`EnterPreview` don't pass `_materialBitmaps` to `BuildWpfModel`; see root causes C-1/C-2/C-3 above |
| TD-22-1 | 🟠 High | `AssimpMeshLoader` has **no material support** — all faces loaded via Assimp (FBX, 3DS, DAE…) get `MaterialId = -1`; multi-material texture system non-functional for non-OBJ formats |
| TD-22-2 | 🟠 High | `ProjectState` / `.4hu` does not persist per-material texture paths → on project reload, all slot assignments (except mesh default) are lost |
| TD-22-3 | 🟡 Medium | `SvgExporter` only embeds a single `texturePath`; multi-material SVG export shows at most one texture for the whole model |
| TD-22-4 | 🟡 Medium | `SvgExporter` + `PdfExporter` edge-dedup uses `HashSet<(float,float,float,float)>` — float equality is unreliable; should use canonical mesh edge ID |
| TD-22-5 | 🟡 Medium | `AssimpMeshLoader` loads with `PostProcessSteps.FlipUVs` AND `ToWpfUV` applies `1.0 - uv.Y` — double-flip cancels out but intent is undocumented; remove one layer |
| Performance | 🟢 Low | O(n²) overlap check → spatial grid for meshes > 2000 faces |

---

## File Inventory (~65 source files, ~5 400 lines)

```
Domain/Models/          Vertex Edge EdgeType Face Mesh PaperSizeModel ModelScale
Domain/DualGraph/       DualGraph GraphNode GraphEdge
Domain/Results/         UnfoldedFace GlueTab UnfoldResult
Domain/Settings/        AppSettings (View3D View2D Print General)
Domain/Persistence/     ProjectState

Geometry/Algorithms/    DualGraphBuilder KruskalMstBuilder EdgeMarker
                        UnfoldEngine OverlapDetector GlueTabGenerator PieceComputer

Application/Interfaces/ IMeshLoader IExporter
Application/Services/   MeshService UnfoldService ProjectSerializer SettingsService

Infrastructure/         ObjMeshLoader AssimpMeshLoader MultiFormatMeshLoader
                        SvgExporter PdfExporter AffineTransformHelper

App/ViewModels/         MainViewModel PieceViewModel SettingsViewModel
                        MaterialTextureViewModel
App/Controls/           PatternCanvasControl
App/Dialogs/            UnfoldSetupDialog SettingsDialog TextureDialog
App/Converters/         HexColorBrushConverter
App/                    MainWindow App

Tests/                  MstAlgorithmTests (6)  UnfoldEngineTests (9)
                        GeometryAlgorithmTests (13)  SvgExporterTests (5: AffineTransform)
App/Assets/             app.ico (6 sizes) logo.png
```

---

## Recommended Next Steps

1. **Merge PR #1**: <https://github.com/nghiazer/4H-Unfolder/pull/1>
2. Performance: spatial grid for overlap check (>2000 face meshes)
3. PDO import (Pepakura native format — reverse-engineered, complex)
4. Reload 3D model (re-apply unfold when source OBJ changes)
5. Assembly animation / step-by-step fold guide
