# 4H-Unfolder — Session Progress Log

> **Last updated:** 2026-05-24 (session 25 — Feature A orientation dialog, Feature B 3D edge hover + RMB pivot, publish v0.0.2.D)
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
- **Assembly animation** (🎬) — step-by-step fold guide with:
  - Phase 1: true paper-fold — faces rotate around shared fold edges (BFS spanning tree + accumulated Matrix4x4)
  - Phase 2: fly-in — folded shape translates to final 3-D position
  - Per-material texture display (assembled + current piece)
  - Amber emissive overlay on current piece; ghost translucent for upcoming pieces
  - Play/Pause auto-animation; step controls ⏮ ◀ ▶ ⏭

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
| Published `4H-Unfolder.exe` v0.0.2.D (win-x64, self-contained) | ✅ Session 25 |

---

## Session 25 — Changes

| Item | Detail |
|------|--------|
| **Feature A — Model Orientation Dialog** | New `ModelOrientationDialog.xaml/.cs` + `ModelOrientationViewModel.cs` shown after every `LoadMesh`. 3D colored cube (HelixViewport3D, orbitable) with 6 axis-labeled faces. Two ComboBoxes: Up axis + Front axis (each → ±X/Y/Z). `Mesh.ApplyTransform(Matrix4x4)` + `Mesh.FlipUVsVertical()` added. |
| **Feature B — 3D Edge Hover + RMB Pivot** | Context menu removed from 3D viewport. `MouseMove` → screen-space edge proximity (8px threshold); hover fold edge = red highlight, hover cut edge = green highlight; LMB = `ToggleEdge`. RMB click (non-drag) = `LookAt(hitPt, 300)` to set rotation pivot. All orbit/zoom/pan mouse actions preserved. |
| **AppSettings** | +`EdgeHoverDetachColor` (#ff3333), `EdgeHoverAttachColor` (#33cc33) in `View3DSettings` |
| **SettingsDialog (3D panel)** | New "3D Edge Edit Mode" GroupBox with 2 configurable color pickers |
| **MainViewModel** | +`CurrentMesh` (read-only), +`EdgeHighlightModel`, +`HoverEdge(edgeId, isDetach)`, +`ClearEdgeHover()`, +`BuildThinCylinder()` |
| **Build/Test** | ✅ 0 errors / 34 tests passed / app starts clean |
| **Release v0.0.2.D** | Published win-x64 self-contained EXE |

## Session 24 — Changes

| Item | Detail |
|------|--------|
| **`PieceFoldTree.cs`** (new) | BFS spanning tree of fold edges per piece; `ComputeFoldAngle` via signed angle between 3-D face normals; `Geometry/Algorithms` layer |
| **`AssemblyViewModel.cs`** (rewrite) | Two-phase animation: Phase 1 paper-fold (accumulated `Matrix4x4` per face, 600ms) + Phase 2 fly-in lerp (600ms); total 1200ms per step |
| **Texture in animation** | Assembled + current pieces use per-material `ImageBrush`; current piece adds `EmissiveMaterial(amber #ff,cc,00 α=90)` overlay; ghost stays translucent solid |
| **`MainViewModel`** | `OpenAssemblyAnimation` now passes `_materialBitmaps` to `AssemblyViewModel` |
| **publish/ cleanup** | Removed 272 root-level DLL/EXE artifacts; kept only `v0.0.2.A/` and `v0.0.2.B/` |
| **Release v0.0.2.C** | Published win-x64 self-contained EXE |

## Session 23 — Changes (kept)

| Item | Detail |
|------|--------|
| **`AssemblyStep.cs`** (new) | Lightweight DTO for one assembly step (Domain/Results) |
| **`AssemblyPlanner.cs`** (new) | BFS piece-adjacency planner from `EdgeType.Cut` edges (Geometry/Algorithms) |
| **`AssemblyViewModel.cs`** (new) | Original Approach A flat→3D animation; replaced in session 24 |
| **`AssemblyAnimationWindow.xaml/.cs`** (new) | Dark-theme window with HelixViewport3D, progress bar, step controls |
| **`MainViewModel`** | Added `OpenAssemblyAnimationCommand`; fixed `_canExport` missing `[NotifyCanExecuteChangedFor]` for PDF + animation commands |
| **`MainWindow.xaml`** | Added 🎬 button to toolbar |
| **Bug fix** | `OpenAssemblyAnimationCommand` stayed disabled after Unfold — now fixed |

---

## Remaining Tech Debt

| ID | Priority | Description |
|----|----------|-------------|
| TD-22-1 | 🟠 High | `AssimpMeshLoader` has **no material support** — all faces loaded via Assimp (FBX, 3DS, DAE…) get `MaterialId = -1`; multi-material texture system non-functional for non-OBJ formats |
| TD-22-2 | 🟠 High | `ProjectState` / `.4hu` does not persist per-material texture paths → on project reload, all slot assignments (except mesh default) are lost |
| TD-22-3 | 🟡 Medium | `SvgExporter` only embeds a single `texturePath`; multi-material SVG export shows at most one texture for the whole model |
| TD-22-4 | 🟡 Medium | `SvgExporter` + `PdfExporter` edge-dedup uses `HashSet<(float,float,float,float)>` — float equality is unreliable; should use canonical mesh edge ID |
| TD-22-5 | 🟡 Medium | `AssimpMeshLoader` loads with `PostProcessSteps.FlipUVs` AND `ToWpfUV` applies `1.0 - uv.Y` — double-flip cancels out but intent is undocumented; remove one layer |
| TD-24-1 | 🟡 Medium | `PieceFoldTree` fold animation: angles computed from 3D normals applied in flat space — fold direction may be wrong for non-trivial pieces |
| TD-25-1 | 🟢 Low | `ModelOrientationDialog` shown on every mesh load; add "don't ask again" setting for users who always use Y-up Z-front models |
| TD-25-2 | 🟢 Low | `FindNearestEdge` in `MainWindow.xaml.cs` projects all edges per `MouseMove` — O(n) per frame; add spatial pre-filter for meshes > 5 000 edges |
| Performance | 🟢 Low | O(n²) overlap check → spatial grid for meshes > 2000 faces |

---

## File Inventory (~67 source files, ~5 800 lines)

```
Domain/Models/          Vertex Edge EdgeType Face Mesh PaperSizeModel ModelScale
Domain/DualGraph/       DualGraph GraphNode GraphEdge
Domain/Results/         UnfoldedFace GlueTab UnfoldResult AssemblyStep
Domain/Settings/        AppSettings (View3D View2D Print General)
Domain/Persistence/     ProjectState

Geometry/Algorithms/    DualGraphBuilder KruskalMstBuilder EdgeMarker
                        UnfoldEngine OverlapDetector GlueTabGenerator PieceComputer
                        AssemblyPlanner PieceFoldTree

Application/Interfaces/ IMeshLoader IExporter
Application/Services/   MeshService UnfoldService ProjectSerializer SettingsService

Infrastructure/         ObjMeshLoader AssimpMeshLoader MultiFormatMeshLoader
                        SvgExporter PdfExporter AffineTransformHelper

App/ViewModels/         MainViewModel PieceViewModel SettingsViewModel
                        MaterialTextureViewModel AssemblyViewModel
App/Controls/           PatternCanvasControl
App/ViewModels/         MainViewModel PieceViewModel SettingsViewModel
                        MaterialTextureViewModel AssemblyViewModel ModelOrientationViewModel
App/Controls/           PatternCanvasControl
App/Dialogs/            UnfoldSetupDialog SettingsDialog TextureDialog
                        AssemblyAnimationWindow ModelOrientationDialog
App/Converters/         HexColorBrushConverter
App/                    MainWindow App

Tests/                  MstAlgorithmTests (6)  UnfoldEngineTests (9)
                        GeometryAlgorithmTests (13)  SvgExporterTests (5: AffineTransform)
App/Assets/             app.ico (6 sizes) logo.png
```

---

## Recommended Next Steps

1. **Merge PR #1**: <https://github.com/nghiazer/4H-Unfolder/pull/1>
2. Fix TD-22-1: Assimp material support (multi-format multi-texture)
3. Fix TD-22-2: persist per-material texture paths in `.4hu` project files
4. Fix TD-24-1: PieceFoldTree fold animation direction accuracy
5. Performance: spatial grid for overlap check (>2000 face meshes)
6. PDO import (Pepakura native format — reverse-engineered, complex)
