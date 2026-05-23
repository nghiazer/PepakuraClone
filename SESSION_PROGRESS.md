# 4H-Unfolder — Session Progress Log

> **Last updated:** 2026-05-23 (session 16 — Pepakura parity features)  
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
| Multi-format load | `AssimpMeshLoader` + `MultiFormatMeshLoader` | 3DS, STL, DXF, LWO/LWS, FBX, COLLADA, PLY via Assimp |
| Dual graph | `DualGraphBuilder` | Dihedral-angle weights; zero-area face guard |
| MST | `KruskalMstBuilder` | Kruskal + path-compressed Union-Find |
| Edge marking | `EdgeMarker` | Fold / Cut / Boundary |
| Unfold | `UnfoldEngine` | BFS circle-circle apex; disconnected components |
| Overlap | `OverlapDetector` | AABB pre-check + SAT |
| Tabs | `GlueTabGenerator` | Trapezoid / Rectangle / Triangle shapes; alternate-flap placement option |
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
- **Edge-Edit mode** (✏ toolbar toggle): hover highlights edges, left-click attach/detach; highlight color configurable in Settings → 2D View
- **Rotate-by-Point mode** (⊙ toolbar toggle, icon-only): white dots at all piece vertices; click pivot → click handle → live rotation follows mouse; click anywhere to confirm; Ctrl+Z undoable
- **2D canvas texture rendering**: per-triangle affine UV mapping (Cramer's rule) — 2D pieces reflect texture in real-time; updates on change/remove
- **App icon** (`Assets/app.ico`, 6 sizes 16–256px) embedded in exe and window title bar
- Unfold setup dialog: real-world scale + paper size
- **Save/Load `.4hu` project bundle** — self-contained ZIP: embeds mesh + texture + state; not readable in text editors; backward-compatible load of legacy `.pmc` files
- **Edge ID numbers + glue arrows** on cut edges — matching pair numbers (1, 2, 3…) with outward arrows, configurable color; `ShowEdgeIds` in Settings → 2D View
- **Auto-align edge** — double-click any edge line on the 2D canvas → snaps piece rotation so that edge is exactly horizontal or vertical (nearest 90°); undoable
- **Parts alignment toolbar** — 6 new buttons: Align Left / Right / Top / Bottom / Center-H / Center-V on selected pieces; uses rotated AABB for precise alignment; undoable
- **Glue tab shapes** — Settings → Print: Trapezoid (default), Rectangle, Triangle
- **Alternate flap placement** — Settings → Print toggle: only one side of each cut edge pair gets a tab (halves tab count)
- **Multi-format import** — 3DS, STL, DXF, LWO/LWS, FBX, COLLADA, PLY via AssimpNet 5; `MultiFormatMeshLoader` routes by extension
- Export SVG

### Settings (4-panel dialog)
| Section | Key options |
|---------|------------|
| 3D View | Background, display mode, face/back color, opacity, edge overlay, lighting, camera FOV/clip |
| 2D View | Canvas/paper color, grid, fold/cut colors+widths+dash, glue tabs, face numbers, piece gap, snap-to-grid, default zoom, **edge ID show+color** |
| Print | Margin, bleed, SVG scale, include tabs/fold/cut/label, grayscale, print line colors, **tab shape**, **alternate flaps** |
| General | **Display unit: mm / inch** |

---

## Build & Test Status

| Item | Result |
|------|--------|
| `dotnet build 4H-Unfolder.sln` | ✅ 0 errors, 0 warnings |
| `dotnet test` | ✅ 29 / 29 passed |
| `dotnet run --project src/FourHUnfolder.App` | ✅ App mở, không crash |
| Published `4H-Unfolder.exe` (win-x64, self-contained) | ✅ Session 16 |
| `.4hu` bundle save/load | ✅ Session 15 |
| Edge ID labels + glue arrows on cut edges | ✅ Session 16 |
| Auto-align edge (double-click snap) | ✅ Session 16 |
| Parts alignment (L/R/T/B/CH/CV) | ✅ Session 16 |
| Tab shapes: Trapezoid/Rectangle/Triangle | ✅ Session 16 |
| Alternate flap placement | ✅ Session 16 |
| Multi-format import: 3DS/STL/DXF/LWO/FBX/DAE/PLY | ✅ Session 16 |
| 2D texture mapping (affine UV per triangle, DPI-agnostic) | ✅ Session 12 fix |
| App icon embedded in exe | ✅ Session 11 |
| Rotate/flip pieces are now undoable (Ctrl+Z) | ✅ Session 12 |
| Edge-Edit mode (hover highlight + LMB attach/detach) | ✅ Session 13 |
| Rotate-by-Point mode (pivot + handle → live rotation) | ✅ Session 13 |
| Click empty 3D/2D canvas → deselect all pieces | ✅ Session 14 |
| Middle-mouse pan in 2D canvas (scrollbars hidden) | ✅ Session 14 |
| Icon-only compact buttons in both toolbars | ✅ Session 14 |
| Paper size moved to Settings → 2D View (live on apply) | ✅ Session 14 |
| Lasso rubber-band selection (LMB drag on empty canvas) | ✅ Session 14 |
| OBJ invalid vertex refs silently skipped (no crash) | ✅ Session 12 |

---

## All Bugs Fixed (cumulative)

| Session | Severity | Bug | Fix |
|---------|----------|-----|-----|
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

## Open Bugs

> None outstanding — all known bugs resolved as of session 10.

## Fixed Bugs

| Session | ID | Severity | File | Description | Fix |
|---------|-----|----------|------|-------------|-----|
| 6 | BUG-1 | **High** | `GlueTabGenerator.cs` | Boundary edges on open meshes got unwanted glue tabs (`EdgeIsBoundary[i]` not checked) | Added `|| face.EdgeIsBoundary[i]` guard alongside `EdgeIsFold[i]` check |
| 6 | BUG-2 | **Medium** | `UnfoldEngine.cs` | `FindSharedLocalIndices` returned wrong apex index on malformed topology | Added guard: throws `InvalidOperationException` if `n < 2`; caller catches and places face at origin fallback |
| 6 | BUG-3 | **Low** | `ObjMeshLoader.cs` | `float.Parse()` threw `FormatException` on malformed OBJ float tokens | Changed to `float.TryParse()`, returns `0f` on bad input |
| 6 | BUG-4 | **Low** | `ProjectSerializer.cs` + `ProjectState.cs` | No user feedback when mesh/texture file not found on project load | Added `Warnings` list to `ProjectState` (JSON-ignored); `ProjectSerializer.Load()` appends warnings for unresolved paths; `MainViewModel` shows warnings in `StatusText` |
| 8 | BUG-S7-1 | **Critical** | `MainViewModel.cs`, `SvgExporter.cs` | SVG export re-ran unfold with raw model-unit coords, ignoring `PositionX/Y/Rotation` of pieces on canvas | Added `BuildExportLayout()`: applies per-piece rotation+translation (mm) from `PieceViewModel`; caches `_lastUnfoldResult` for UV coord lookup |
| 8 | BUG-S7-2 | **High** | `ProjectState.cs`, `MainViewModel.cs` | `PagesWide`/`PagesTall` not saved to `.pmc` → multi-page layout lost on project load | Added `PagesWide`/`PagesTall` to `ProjectState`; saved in `BuildProjectState`, restored with `Math.Max(1,…)` guard in `RestoreProjectState` |
| 9 | BUG-S7-3 | **Medium** | `MainViewModel.cs RunAutoArrange` | When a page overflows vertically, algorithm incremented `PagesTall` but never moved to a new column — wide models created very tall single-column layouts; oversized pieces caused incorrect overflow. | Rewrote packing to fill pages **horizontally** (`pageCol` advances on vertical overflow); `PagesWide` grows, `PagesTall = 1` from auto-arrange |
| 9 | BUG-S7-4 | **Medium** | `PatternCanvasControl.cs Canvas_MouseUp`, `MainViewModel.cs` | `EnsurePageForPosition` called with piece centroid — a piece's actual edge could extend beyond page without triggering expansion | Canvas now computes `rightMm = posX + allX.Max()` and `bottomMm = posY + allY.Max()` before calling `EnsurePageForPosition` |
| 10 | BUG-S7-5 | **Low** | `SvgExporter.cs:106` | SVG page label hardcoded `"FourHUnfolder Export"` | Changed to `Path.GetFileNameWithoutExtension(filePath)` |
| 10 | BUG-S7-6 | **Low** | `Mesh.cs:52` | `GetOrAddEdge` overwrote `FaceB` on non-manifold topology | Added guard: only assign `FaceB` when it's still `-1`; extra faces silently skipped |
| 10 | BUG-S7-7 | **Low** | `MainViewModel.cs:SelectFace3D` | O(pieces×faces) linear scan per 3D click | Added `_faceToGroup` dict (faceId→groupId), rebuilt in `RebuildPieces`; O(1) primary lookup with linear fallback |
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
| ~~TD-S7-1~~ | ~~High~~ | ~~Fixed s8~~ | ~~SVG export ignores canvas layout~~ | Fixed: `BuildExportLayout()` applies rotation + translation per piece |
| ~~TD-S7-2~~ | ~~High~~ | ~~Fixed s8~~ | ~~PagesWide/PagesTall not in ProjectState~~ | Fixed: added to `ProjectState`, saved/restored |
| ~~TD-S7-3~~ | ~~Medium~~ | ~~Fixed s9~~ | ~~N+1 canvas rebuilds per unfold~~ | Fixed: `BatchingPieces` flag suppresses per-Add rebuilds; `PiecesVersion++` triggers one final rebuild |
| ~~TD-S7-4~~ | ~~Medium~~ | ~~Fixed s9~~ | ~~OnSettingsChanged rebuilt 3D model for all settings~~ | Fixed: `View3DHash()` compares key View3D fields; rebuild only when they change |
| ~~TD-S7-5~~ | ~~Medium~~ | ~~Fixed s9~~ | ~~RunAutoArrange only created vertical pages~~ | Fixed: auto-arrange now fills pages horizontally (`PagesWide` grows); oversized pieces move to next page column |
| ~~TD-S7-6~~ | ~~Low~~ | ~~Fixed s10~~ | ~~SettingsChanged never unsubscribed~~ | Fixed: `MainViewModel` implements `IDisposable`; `App.OnExit` disposes it |
| ~~TD-N4~~ | ~~Medium~~ | ~~Fixed s10~~ | ~~No tests for OverlapDetector, GlueTabGenerator, ObjMeshLoader~~ | Fixed: `GeometryAlgorithmTests.cs` added (13 new tests: 4 OverlapDetector, 5 GlueTabGenerator, 4 ObjMeshLoader) |
| ~~TD-N6~~ | ~~Low~~ | ~~Fixed s10~~ | ~~Undo/redo didn't cover piece drag moves~~ | Fixed: `_preDragPositions` captured on `MouseDown`; `PushDragUndo()` called on `MouseUp` if piece actually moved |
| ~~Unrotated bbox~~ | ~~Low~~ | ~~Fixed s10~~ | ~~`EnsurePageForPosition` used unrotated bbox~~ | Fixed: all 4 rotated bbox corners computed in `Canvas_MouseUp`; max(x',y') used for page expansion check |
| ~~TD-S7-3~~ | ~~Medium~~ | ~~Fixed s9~~ | ~~(duplicate — already fixed above)~~ | — |
| ~~TD-S7-4~~ | ~~Medium~~ | ~~Fixed s9~~ | ~~(duplicate — already fixed above)~~ | — |
| ~~TD-S7-5~~ | ~~Medium~~ | ~~Fixed s9~~ | ~~(duplicate — already fixed above)~~ | — |
| ~~TD-S7-6~~ | ~~Low~~ | ~~Fixed s10~~ | ~~(duplicate — already fixed above)~~ | — |
| ~~TD-S7-7~~ | ~~Low~~ | ~~Fixed s12~~ | ~~`DrawPageAt()` used namespace-qualified parameter type~~ | Fixed: added `using FourHUnfolder.Domain.Settings;`, shortened to `AppSettings.View2DSettings?` |

### Session 12 fixes (review R-series)

| ID | Was | Resolution |
|----|-----|-----------|
| BUG-R1 | `TabData` lost `FaceId`/`LocalEdgeIdx`; `BuildExportLayout` created tabs with (0,0) | Added fields to `TabData`; populated from `GlueTab` in `PieceViewModel.Create()`; used in `BuildExportLayout` |
| BUG-R2 | Rotate ±90° and Flip-H not undoable | `RotateSelected`/`FlipH_Click` now capture pre-state and call `PushDragUndo` |
| BUG-R3 | `ObjMeshLoader` passed -1 or out-of-bounds vertex index to `AddFace` → crash | Added bounds check in `ParseFace`; invalid triangles silently skipped |
| BUG-TEX | `BuildTextureBrush` used `Stretch=None` + pixel Viewport → DPI mismatch for 72-DPI images | Changed to `Viewport=(0,0,1,1)` + `Stretch=Fill`; source now UV [0,1] (DPI-agnostic) |
| TD-R2 | `_edgeOverrides.ToDictionary(k=>k.Key, v=>v.Value)` confusing idiom | Replaced with `new Dictionary<int,EdgeType>(_edgeOverrides)` |
| TD-S7-7 | `DrawPageAt` parameter used fully-qualified `Domain.Settings.AppSettings.View2DSettings?` | Added `using FourHUnfolder.Domain.Settings;`; shortened to `AppSettings.View2DSettings?` |

### Session 14 tech debt

| ID | Priority | Description | Suggestion |
|----|----------|-------------|-----------|
| TD-S14-1 | Low | Lasso does bbox vs bbox intersection — a very thin rotated piece could have a large bbox and be incorrectly included | Use SAT (Separating Axis Theorem) instead of AABB intersection |
| TD-S14-2 | Low | Pan speed is 1:1 pixel, no acceleration — for very large canvases, panning to far corners requires many swipes | Add inertia or pan speed multiplier |
| TD-S14-3 | Low | Paper size in Settings applies only on OK (not live while dialog is open) | Connect SettingsViewModel.DefaultPaperSizeName change directly to VM |

### Session 13 tech debt

| ID | Priority | Description | Suggestion |
|----|----------|-------------|-----------|
| TD-S13-1 | Low | `_dotRedBrush` in PatternCanvasControl is unfrozen static brush; not thread-safe if ever accessed off-UI thread | Call `.Freeze()` on creation |
| TD-S13-2 | Low | Vertex dots shown for ALL pieces at once — for complex meshes (500+ faces) this can be hundreds of overlapping dots | Show dots only for the selected piece, or use a spatial threshold |
| TD-S13-3 | Low | Rotate-by-point phase resets on any RebuildAll (zoom change, settings change) which may feel abrupt | Preserve phase across rebuilds that don't change piece topology |

### Remaining tech debt (intentionally deferred)

| ID | Priority | Description | Reason deferred |
|----|----------|-------------|----------------|
| TD-N7 | **Low** | `_currentScaleMmPerUnit` loose double field | Refactoring risk with no user-visible benefit |
| SvgExporter.AffineTransform | **Low** | No unit tests for affine transform math | Complex to isolate; SVG visual output is manual-verify territory |

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

Tests/                  MstAlgorithmTests (6) UnfoldEngineTests (9) GeometryAlgorithmTests (13: OverlapDetector×4, GlueTabGenerator×5, ObjMeshLoader×4)
App/Assets/             app.ico (6 sizes) logo.png
```

---

## Recommended Next Steps

### Remaining (intentionally deferred)
1. **TD-N7** — `_currentScaleMmPerUnit` loose double field (low risk, no user-visible benefit)
2. **SvgExporter.AffineTransform** tests — complex to isolate unit test for matrix math

### Future features
1. **Merge PR #1**: <https://github.com/nghiazer/4H-Unfolder/pull/1>
2. Add **PDF export** via `PdfSharp`
3. Add **piece outline merging** (compute union polygon of face triangles) for cleaner visuals
4. Performance: replace O(n²) overlap check with spatial grid for meshes > 2 000 faces
5. Add **auto-unfolding layout heuristic** (strip-packing aware placement)
