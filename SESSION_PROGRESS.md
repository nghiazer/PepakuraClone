# 4H-Unfolder — Session Progress Log

> **Last updated:** 2026-05-23 (session 19 — remaining tech debt TD-S13/S14, review fixes)  
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
| `dotnet build 4H-Unfolder.sln` | ✅ 0 errors, 6 warnings (NuGet version hint) |
| `dotnet test` | ✅ 34 / 34 passed |
| `dotnet run --project src/FourHUnfolder.App` | ✅ App mở, không crash |
| Published `4H-Unfolder.exe` (win-x64, self-contained) | ✅ Session 19 |
| PDF export (`PdfExporter`, multi-page) | ✅ Session 18 |
| Piece outline merging (boundary polygon) | ✅ Session 18 |
| AffineTransformHelper + 5 unit tests | ✅ Session 18 |
| TD-N7: `ScaleMmPerUnit` property (was loose field) | ✅ Session 18 |
| Strip-packing: sort by area + try 90° rotation | ✅ Session 18 |
| TD-S13/S14 tech debt resolved (all 6 items) | ✅ Session 19 |
| Review fixes: `DegenerateEdge` constant, tab pen hoisted | ✅ Session 19 |

---

## Session 19 — Changes

| Item | Detail |
|------|--------|
| **TD-S13-1** | `_dotRedBrush` now frozen via `HexBrush("#dc3232","#dc3232")` |
| **TD-S13-2** | `ShowVtxDots()` shows dots for selected pieces only; falls back to all if none selected |
| **TD-S13-3** | `RebuildAll()` saves pivot GroupId+position; tries to restore phase=1 after rebuild |
| **TD-S14-1** | Lasso now uses `AnyVertexInLasso()` — checks actual canvas-space vertex positions |
| **TD-S14-2** | Middle-mouse pan uses velocity-based acceleration (1×–2.5×) |
| **TD-S14-3** | SettingsDialog live-applies paper size on `DefaultPaperSizeName` change; Cancel reverts |
| **Review fix** | `ReconstructApex` uses `DegenerateEdge` constant (was hardcoded `1e-6f`) |
| **Review fix** | Tab outline pen hoisted outside per-tab loop in `PdfExporter` |

## Session 18 — Changes (kept)

| Item | Detail |
|------|--------|
| **TD-N7 fixed** | `_currentScaleMmPerUnit` → `private double ScaleMmPerUnit { get; set; }` |
| **SVG affine tests** | Extracted `AffineTransformHelper` (public static); 5 unit tests in `SvgExporterTests.cs` |
| **PDF export** | `PdfExporter` + `ExportPdfCommand`; 📑 toolbar button; PdfSharp.Standard 1.51 |
| **Piece outline** | `BuildPieceOutline()` in canvas — chains non-fold boundary edges into polygon; drawn over fills |
| **Strip-packing** | `RunAutoArrange` now sorts pieces by area desc (FFD), tries 90° rotation for narrower footprint |

---

## Remaining Tech Debt

| ID | Priority | Description |
|----|----------|-------------|
| Performance | Low | O(n²) overlap check → spatial grid for meshes > 2000 faces |

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
