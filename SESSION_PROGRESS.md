# 4H-Unfolder — Session Progress Log

> **Last updated:** 2026-05-22 (session 5 — rename + full code review)  
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
| Published `4H-Unfolder.exe` (win-x64, self-contained) | ✅ Chạy được |

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

## Open Bugs (pending fix)

> Discovered in session 5 full code review. Ordered by severity.

| ID | Severity | File | Description | Impact |
|----|----------|------|-------------|--------|
| BUG-1 | **High** | `GlueTabGenerator.cs:25` | Generator skips `EdgeIsFold[i]` but does **not** skip `EdgeIsBoundary[i]` → boundary edges on open meshes get unwanted glue tabs | Extra tabs appear on flat/open meshes; visually cluttered SVG; misleading assembly instructions |
| BUG-2 | **Medium** | `UnfoldEngine.cs:176-182` | `FindSharedLocalIndices` fills at most `n` entries; if a face shares < 2 vertices with the edge (malformed topology), `r[1]` stays `0` → `la = 3 − ls[0] − 0` → wrong apex index | Silent wrong geometry on malformed OBJ; no crash but incorrect unfold output |
| BUG-3 | **Low** | `ObjMeshLoader.cs:130-131` | `F()` calls `float.Parse()` with no guard; any malformed float token in OBJ (e.g. `"1.0e"`, `"-"`) throws unhandled `FormatException` | Hard crash on load; error message unhelpful ("Input string was not in correct format") |
| BUG-4 | **Low** | `ProjectSerializer.cs:75` | `Resolve()` returns `null` when both relative and absolute paths don't resolve; `MainViewModel` silently skips loading mesh/texture with no user message | User loads project, canvas stays empty, no explanation |

## Fixed Bugs (session 5)

| ID | Severity | File | Description | Fix |
|----|----------|------|-------------|-----|
| BUG-0 | **Critical** | `PatternCanvasControl.xaml.cs:477` | `Zoom_Changed` accessed `ZoomLabel.Text` before `ZoomLabel` was initialized — WPF fires `Slider.ValueChanged` during `InitializeComponent()` XAML parsing before named elements exist → `NullReferenceException` → app crash on every startup | Added `if (ZoomLabel == null) return;` guard; `OnDataContextChanged` re-fires the handler correctly once DataContext is set |

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

### New tech debt discovered in session 5 (pending)

| ID | Priority | File(s) | Description |
|----|----------|---------|-------------|
| TD-N1 | **High** | `EdgeMarker.cs`, `UnfoldService.cs:46-52` | `EdgeMarker.Mark()` is dead code — `UnfoldService.Unfold()` re-implements identical edge-stamping logic inline and never calls `EdgeMarker`. Maintenance risk: edge logic must be updated in two places. |
| TD-N2 | **High** | `GlueTabGenerator.cs:13-14` | Tab dimensions `TabDepth = 4f` (mm) and `TabInset = 0.15f` are hardcoded constants, not exposed in Settings. Users cannot adjust tab size for different paper thicknesses. |
| TD-N3 | **Medium** | `ProjectSerializer.cs`, `ProjectState.cs:9` | `ProjectState.Version = 2` is set but never read or checked during `Load()`. No migration path exists if the file format changes in future releases. |
| TD-N4 | **Medium** | `tests/FourHUnfolder.Tests/` | Significant test coverage gaps: no tests for `OverlapDetector`, `GlueTabGenerator` (including boundary-edge bug BUG-1), `SvgExporter.AffineTransform`, or `ObjMeshLoader` error paths. |
| TD-N5 | **Medium** | `OverlapDetector.cs:71` | SAT epsilon `1e-5f` is applied to projections along the **unnormalized** edge perpendicular axis. Effective geometric tolerance scales with edge length, making it inconsistent across differently-sized triangles. |
| TD-N6 | **Low** | `MainViewModel.cs` | Undo/redo (`_undoStack`/`_redoStack`) captures edge overrides and piece positions at the moment of edge operation, but piece drag moves between edge operations are not individually undoable. |
| TD-N7 | **Low** | `MainViewModel.cs:36` | `_currentScaleMmPerUnit` is a loose `double` field with no encapsulation. If `Unfold()` is called again with a new scale, old piece coordinates in PieceViewModel remain stale until rebuilt. |
| TD-N8 | **Low** | Geometry algorithms | Epsilon values scattered (`1e-4f` in GlueTabGenerator, `1e-5f` in OverlapDetector, `1e-6f` in UnfoldEngine×2, `1e-10f` in DualGraphBuilder) with no centralized policy or documentation of their purpose. |

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

### Immediate (bugs)
1. Fix **BUG-1** — add `EdgeIsBoundary[i]` guard in `GlueTabGenerator` (1-line fix)
2. Fix **BUG-2** — add guard in `FindSharedLocalIndices` to throw or return invalid sentinel when `n < 2`
3. Fix **BUG-3** — wrap `float.Parse()` in `ObjMeshLoader.F()` to return `0f` and log warning
4. Fix **BUG-4** — show user-facing warning in `MainViewModel` when mesh/texture not found on project load

### Near-term (high tech debt)
5. Remove **TD-N1** dead `EdgeMarker` class or wire it up properly in `UnfoldService`
6. Move **TD-N2** tab dimensions into `AppSettings.PrintSettings` and expose in Settings dialog

### Future
7. **Merge PR #1**: <https://github.com/nghiazer/4H-Unfolder/pull/1>
8. Add **PDF export** via `PdfSharp`
9. Add **piece outline merging** (compute union polygon of face triangles) for cleaner visuals
10. Performance: replace O(n²) overlap check with spatial grid for meshes > 2 000 faces
11. **TD-N3** — add version check + migration in `ProjectSerializer.Load()`
12. **TD-N4** — expand test suite to cover geometry edge cases
13. Add **auto-unfolding layout heuristic** (strip-packing aware placement)
