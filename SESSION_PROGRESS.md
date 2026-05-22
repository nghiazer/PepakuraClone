# 4H-Unfolder — Session Progress Log

> **Last updated:** 2026-05-22 (session 7 — multi-page layout + full code review)  
> **Branch:** `feat/paper-model-unfolder`  (PR #1 open against `main`)
> **Target framework:** .NET 8 / WPF  
> **SDK required:** `winget install Microsoft.DotNet.SDK.8`

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
| OBJ load | `ObjMeshLoader` | v/vt/f, MTL map_Kd, fan-triangulation, negative-index guard |
| Dual graph | `DualGraphBuilder` | Dihedral-angle weights; zero-area face guard |
| MST | `KruskalMstBuilder` | Kruskal + path-compressed Union-Find |
| Edge marking | `EdgeMarker` | Fold / Cut / Boundary |
| Unfold | `UnfoldEngine` | BFS circle-circle apex; disconnected components |
| Overlap | `OverlapDetector` | AABB pre-check + SAT |
| Tabs | `GlueTabGenerator` | Trapezoidal, tagged with FaceId |
| Pieces | `PieceComputer` | Union-Find connected components |
| SVG | `SvgExporter` | Edge-deduplicated; settings-driven; grayscale support |

### UI features
- Split 3D/2D viewport (2D hidden until after first Unfold)
- 3D mouse picking: left-click face → selection overlay (yellow, z-offset) + 2D highlight
- Right-click 3D face → Detach face / Detach piece / Attach to neighbour
- Bidirectional 3D↔2D sync (clicking 2D piece updates 3D overlay)
- Interactive 2D canvas: drag pieces, rotate ±90°, flip H, auto-arrange
- Grid toggle (fast-path, no rebuild) + Snap to grid
- Texture load/replace/remove with live preview (Apply/Cancel)
- Unfold setup dialog: real-world scale + paper size
- Save/Load `.pmc` project (edge overrides + piece layouts + scale)
- Export SVG

### Settings (4-panel dialog)
| Section | Key options |
|---------|------------|
| 3D View | Background, display mode, face/back color, opacity, edge overlay, lighting, camera FOV/clip |
| 2D View | Canvas/paper color, grid show+size+color, fold/cut colors+widths+dash, glue tabs, face numbers, piece gap, snap-to-grid, default zoom |
| Print | Margin, bleed, SVG scale, include tabs/fold/cut/label, grayscale, print line colors |
| General | **Display unit: mm / inch** |

---

## Build & Test Status

| Item | Result |
|------|--------|
| `dotnet build 4H-Unfolder.sln` | ✅ 0 errors, 0 warnings |
| `dotnet test` | ✅ 16 / 16 passed |
| `dotnet run --project src/FourHUnfolder.App` | ✅ App mở, không crash |
| Published `4H-Unfolder.exe` (win-x64, self-contained) | ✅ Chạy được, Unfold/Export active |

---

## All Bugs Fixed (cumulative)

| Session | Severity | Bug | Fix |
|---------|----------|-----|-----|\
| 4 (build) | Critical | `MainViewModel.cs` — extra spurious `}` after `CommitPreview` body, closing the class too early → `CS1022` | Removed extra brace |
| 4 (build) | High | `App.xaml.cs` — `FourHUnfolder.Application` namespace shadowed `System.Windows.Application` type → `CS0118` | Changed to `System.Windows.Application` explicit |
| 4 (build) | High | `PatternCanvasControl.xaml.cs` — `file static class` with `operator *` → static classes can't hold user-defined operators → `CS0715` | Replaced with `private Point Sc(Point p)` instance method |
| 4 (build) | High | `MainWindow.xaml.cs` — missing `using System.Windows.Media` → `VisualTreeHelper` / `PointHitTestParameters` not found | Added using |
| 4 (build) | High | `MainViewModel.cs` — missing `using System.IO` explicit (WPF temp-csproj skips implicit usings) → `Path`/`File` not found | Added `using System.IO` |
| 4 (build) | High | `MainViewModel.cs` — `Application.Current` resolved to `FourHUnfolder.Application.Current` → `CS0234` | Added `using WpfApp = System.Windows.Application` alias |
| 4 (build) | Medium | `CommitPreview(_revert: true)` used old parameter name `_revert` after rename to `revert` → `CS1739` | Updated call site |
| 4 (build) | Medium | `PieceViewModel.FaceData` missing `EdgeIsBoundary` field used by `PatternCanvasControl` → `CS1061` | Added field + populated in `Create()` |
| 1 | Critical | `PatternCanvasControl` drag broken — wrong mouse-capture element | `RootCanvas.CaptureMouse()` |
| 1 | Critical | Project save wrote `ScaleMmPerUnit = 1.0` hardcoded | Use `_currentScaleMmPerUnit` |
| 1 | High | Compile error — `MainWindow.xaml.cs` missing `using System.Linq` | Added import |
| 1 | High | `UnfoldSetupDialog` custom-size boxes always disabled (self-ref binding) | Code-behind only |
| 1 | Medium | `CommitPreview` had identical ternary branches | Clarified logic |
| 1 | Low | Degenerate triangles produced NaN normals → poisoned MST weights | Guard in `DualGraphBuilder` |
| 1 | Low | OBJ negative vertex indices not handled | Return -1 |
| 2 | Critical | `SettingsDialog.xaml` — 4 StackPanels as direct children of ScrollViewer (ContentControl allows only one) | Wrapped in `<Grid>` |
| 2 | High | `SettingsViewModel.DisplayUnits` contained `"mm (millimetre)"` but stored value is `"mm"` → ComboBox always blank | Changed list to `["mm", "inch"]` |
| 2 | Medium | `PatternCanvasControl._vm!.GridVisible` — null-forgiving could throw | Replaced with null-conditional guard |
| 2 | Low | `SvgExporter` face fill not greyed when `GrayscaleOutput = true` | CSS fill driven by setting |

---

## Open Bugs (session 7 review)

| ID | Severity | File | Description | Impact |
|----|----------|------|-------------|--------|
| BUG-S7-1 | **Critical** | `MainViewModel.cs:368`, `SvgExporter.cs` | SVG export re-runs `Unfold()` without `_currentScaleMmPerUnit` → raw model-unit coordinates used instead of mm. All piece `PositionX/Y/Rotation` from canvas are completely ignored. | Exported SVG has wrong physical size AND ignores user arrangement — unusable for printing |
| BUG-S7-2 | **High** | `ProjectState.cs`, `MainViewModel.cs` | `PagesWide` and `PagesTall` are not saved to `.pmc` file. On load, canvas resets to 1×1 page while piece positions reference multi-page coordinates. | After load, pieces on page 2+ are rendered off-canvas background; user must re-run Auto-arrange |
| BUG-S7-3 | **Medium** | `MainViewModel.cs:719-724` | `RunAutoArrange` wrap condition `curX > gap &&` prevents wrapping when a piece is wider than `paperW - 2*gap`. Such pieces overflow the right edge of the page. | Pieces larger than the page appear cut off by the page background |
| BUG-S7-4 | **Medium** | `MainViewModel.cs:751-760` | `EnsurePageForPosition` compares piece **centroid** against page edge, not the actual bounding box corner. | Pieces dragged so centroid is on-page but edge overflows don't trigger a new page |
| BUG-S7-5 | **Low** | `SvgExporter.cs:106` | Page label hardcoded to `"FourHUnfolder Export"` — not configurable, not the filename. | Cosmetic issue in exported SVG |
| BUG-S7-6 | **Low** | `Mesh.cs:52` | `GetOrAddEdge` silently overwrites `FaceB` if 3+ faces share the same edge (non-manifold topology) → dual graph loses face association with no error. | Silent wrong unfold on non-manifold OBJ files |
| BUG-S7-7 | **Low** | `MainViewModel.cs:477` | `SelectFace3D` scans all pieces × all faces (`O(pieces × faces)`) on every 3D click. | Noticeable lag on meshes >2 000 faces when clicking in 3D viewport |

## Fixed Bugs

| Session | ID | Severity | File | Description | Fix |
|---------|-----|----------|------|-------------|-----|
| 6 | BUG-1 | **High** | `GlueTabGenerator.cs` | Boundary edges on open meshes got unwanted glue tabs (`EdgeIsBoundary[i]` not checked) | Added `|| face.EdgeIsBoundary[i]` guard alongside `EdgeIsFold[i]` check |
| 6 | BUG-2 | **Medium** | `UnfoldEngine.cs` | `FindSharedLocalIndices` returned wrong apex index on malformed topology | Added guard: throws `InvalidOperationException` if `n < 2`; caller catches and places face at origin fallback |
| 6 | BUG-3 | **Low** | `ObjMeshLoader.cs` | `float.Parse()` threw `FormatException` on malformed OBJ float tokens | Changed to `float.TryParse()`, returns `0f` on bad input |
| 6 | BUG-4 | **Low** | `ProjectSerializer.cs` + `ProjectState.cs` | No user feedback when mesh/texture file not found on project load | Added `Warnings` list to `ProjectState` (JSON-ignored); `ProjectSerializer.Load()` appends warnings for unresolved paths; `MainViewModel` shows warnings in `StatusText` |
| 5 | BUG-0 | **Critical** | `PatternCanvasControl.xaml.cs:477` | `Zoom_Changed` accessed `ZoomLabel.Text` before `ZoomLabel` was initialized — crash on every startup | Added `if (ZoomLabel == null) return;` guard |

---

## Tech Debt Status

### Resolved this session
| ID | Was | Resolution |
|----|-----|-----------|
| TD-2 | Shared edges drawn twice in 2D canvas | `HashSet<int>` dedup by mesh edge ID |
| TD-3 | O(n²) SAT — slow on large meshes | AABB pre-check rejects non-overlapping pairs |
| TD-4 | Memory leak in PatternCanvasControl (dangling PropertyChanged handlers) | Explicit subscription dict + unsubscribe on rebuild |
| TD-5 | Selection overlay rebuilt on every click | Frozen `Model3DGroup` cache per group ID |
| TD-6 | SVG fold/cut lines drawn twice per shared edge | Canonical-key HashSet dedup |
| TD-N1 | `EdgeMarker.Mark()` dead code — same logic duplicated inline in `UnfoldService` | `EdgeMarker` signature changed to `IReadOnlySet<int>`; `UnfoldService` now calls `_edgeMarker.Mark()` |
| TD-N2 | `GlueTabDepthMm`/`TabInsetRatio` hardcoded constants | Added to `AppSettings.PrintSettings`; `GlueTabGenerator.Generate()` now accepts params; exposed as sliders in Settings → Print panel |
| TD-N3 | `ProjectState.Version` never validated on load | `ProjectSerializer.Load()` throws `InvalidDataException` if `Version > CurrentVersion` |
| TD-N5 | SAT epsilon `1e-5f` on unnormalized axis — inconsistent tolerance | Epsilon now scaled by `axis.Length()` for consistent geometric tolerance |
| TD-N8 | Epsilon values scattered across geometry files | Created `GeometryConstants.cs` in Geometry project; all geometry algorithms import via `using static` |

### New tech debt discovered in session 7

| ID | Priority | File(s) | Description | Suggestion |
|----|----------|---------|-------------|-----------|
| TD-S7-1 | **High** | `MainViewModel.cs`, `SvgExporter.cs` | `SvgExporter.Export()` receives no layout data — ignores `_currentScaleMmPerUnit`, piece `PositionX/Y`, and `Rotation`. Export doesn't match canvas view. | Pass `IEnumerable<PieceLayout>` (scale + per-piece transform) to `Export()`; apply affine transform per piece in SVG |
| TD-S7-2 | **High** | `ProjectState.cs`, `BuildProjectState()` | `PagesWide`/`PagesTall` not serialized to `.pmc` — multi-page layout lost on project save/load | Add `PagesWide`/`PagesTall` fields to `ProjectState`; restore in `RestoreProjectState` |
| TD-S7-3 | **Medium** | `MainViewModel.cs RebuildPieces()`, `PatternCanvasControl.cs OnPiecesChanged()` | Each `Pieces.Add()` triggers `Dispatcher.Invoke(RebuildAll)` synchronously → N+1 full canvas rebuilds per unfold. For a 50-piece mesh = 51 rebuilds. | Use `ObservableRangeCollection` or suppress collection events during batch add; fire one rebuild at the end |
| TD-S7-4 | **Medium** | `MainViewModel.cs:164-168` | `OnSettingsChanged` rebuilds the expensive 3D WPF model on ANY settings change (2D, Print, General), not just 3D-view changes | Guard: only call `BuildWpfModel` when `View3D` properties actually changed |
| TD-S7-5 | **Medium** | `MainViewModel.cs:696` | `RunAutoArrange` always sets `PagesWide = 1` — auto-arrange never uses horizontal pages. Wide models produce very tall single-column layouts. | Support 2-D strip packing; allow pieces to fill right before going down |
| TD-S7-6 | **Low** | `MainViewModel.cs:140` | `_settingsService.SettingsChanged` subscription never unsubscribed from `MainViewModel` | Add unsubscription on dispose or rely on documented singleton lifetime |
| TD-S7-7 | **Low** | `PatternCanvasControl.xaml.cs DrawPageAt()` | Method signature uses namespace-qualified `Domain.Settings.AppSettings.View2DSettings?` parameter | Add `using` import; use short type name |

### Remaining tech debt (deferred)

| ID | Priority | Description |
|----|----------|-------------|
| TD-N4 | **Medium** | Test coverage gaps: no tests for `OverlapDetector`, `GlueTabGenerator`, `SvgExporter.AffineTransform`, `ObjMeshLoader` error paths |
| TD-N6 | **Low** | Undo/redo scope covers edge ops only — piece drag moves not individually undoable |
| TD-N7 | **Low** | `_currentScaleMmPerUnit` is a loose `double` field; no centralized scale context object |

### All resolved ✓

| ID | Was | Resolution |
|----|-----|-----------|
| TD-1 | No repositioning of new pieces after join/split → pieces stack at origin | New pieces spawned by a join/split are placed to the **right of the paper boundary** in a 4-column grid, so they're visible and drag-able; existing pieces keep their saved positions. (Fold-cycle display was confirmed by-design.) |
| TD-7 | Triangle-grid visible on pieces; boundary edges indistinguishable from cut edges | Removed polygon stroke; boundary edges drawn as thin dark grey; `EdgeIsBoundary[]` propagated through `UnfoldedFace` from `UnfoldEngine` |
| TD-8 | Texture not embedded in SVG | UV coords (`UVCoords[]`) added to `UnfoldedFace`; `SvgExporter` computes per-face affine transform, embeds texture as base-64 data URI with clip paths |
| TD-9 | No undo/redo | `EditSnapshot` record (edge overrides + piece positions); `_undoStack`/`_redoStack`; `UndoCommand`/`RedoCommand`; `PushUndoState()` called in ToggleEdge, DetachFace, DetachPiece, AttachFaces; Ctrl+Z/Y keyboard shortcuts |

---

## File Inventory (57 source files, ~4 700 lines)

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

Infrastructure/         ObjMeshLoader SvgExporter

App/ViewModels/         MainViewModel PieceViewModel SettingsViewModel
App/Controls/           PatternCanvasControl
App/Dialogs/            UnfoldSetupDialog SettingsDialog
App/Converters/         HexColorBrushConverter
App/                    MainWindow App

Tests/                  MstAlgorithmTests (6) UnfoldEngineTests (9)
```

---

## Recommended Next Steps

### Critical (must fix before any real use)
1. Fix **BUG-S7-1** — SVG export must apply `_currentScaleMmPerUnit` and piece `PositionX/Y/Rotation`; this is the primary output of the app
2. Fix **BUG-S7-2** — persist `PagesWide`/`PagesTall` in `ProjectState`

### High priority
3. Fix **BUG-S7-3** — `RunAutoArrange` wrap condition for pieces wider than page
4. Fix **BUG-S7-4** — `EnsurePageForPosition` use bounding box, not centroid
5. Fix **TD-S7-5** — support horizontal pages in `RunAutoArrange` (2D strip packing)

### Medium priority
6. Fix **TD-S7-3** — batch `Pieces.Add()` to avoid N+1 canvas rebuilds
7. Fix **TD-S7-4** — `OnSettingsChanged` only rebuilds 3D model when View3D settings change

### Future
8. **Merge PR #1**: <https://github.com/nghiazer/4H-Unfolder/pull/1>
9. Add **PDF export** via `PdfSharp`
10. Add **piece outline merging** (compute union polygon of face triangles) for cleaner visuals
11. Performance: replace O(n²) overlap check with spatial grid for meshes > 2 000 faces
12. **TD-N4** — expand test suite to cover geometry edge cases
13. Add **auto-unfolding layout heuristic** (strip-packing aware placement)
