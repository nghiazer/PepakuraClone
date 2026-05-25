# 4H-Unfolder — Bug & Tech Debt History

> Moved from `SESSION_PROGRESS.md` at session 18.  
> This file accumulates all resolved issues from all past sessions.

---

## Session 30 Changes (archived from SESSION_PROGRESS)

| Item | Detail |
|------|--------|
| **PDO Phase 1** | `PdoMeshLoader.cs` (new): PD6 header + cipher + pre-geo skip (120 bytes) + geometry (vertices, fan-triangulated shapes, edge skip) |
| **PDO Phase 2** | UV extraction from `unk13` per point (offsets 20-35); texture section: zlib decompress RGB24 → `mesh.EmbeddedTextures`; `EmbeddedTextureData` record in Domain |
| **3D texture display** | `MainViewModel.BitmapFromEmbedded`: `BitmapSource.Create(Rgb24) → PngBitmapEncoder → BitmapImage`; stored at `_materialBitmaps[-1]`; picked up by `BuildWpfModel` |
| **Routing + dialog** | `MultiFormatMeshLoader` routes `.pdo`; open-mesh filter + tooltip updated |
| **Tests** | `PdoMeshLoaderTests` (7): geometry, vertex count, UVs finite, embedded texture size; 41/41 suite green |

---

## Session 29 Changes (archived from SESSION_PROGRESS)

| Item | Detail |
|------|--------|
| **TD-28-4** | 3D viewport background auto-switch on theme change; `AppSettings.BackgroundColor` default → `"#e8ecf4"` (Light) |
| **TD-28-1** | SettingsDialog footer buttons: 9 new semantic keys in both themes; footer → DynamicResource |
| **TD-28-3** | AssemblyAnimationWindow / TextureDialog / UnfoldSetupDialog / ModelOrientationDialog fully theme-aware; 5 new theme keys (AssemblyStepBg, AssemblyCtrlBg, CtrlBtnBg/Fg/Border) |
| **TD-24-1** | PieceFoldTree: added `EdgeDir3D` to `FoldNode`; AssemblyViewModel `ComputeFoldTransforms` adds `signCorr` to fix antiparallel axis sign bug |

---

## Session 28 Changes (archived from SESSION_PROGRESS)

| Item | Detail |
|------|--------|
| **Light/Dark theme** | `Themes/LightTheme.xaml` + `DarkTheme.xaml`; `ThemeService.Apply()`; `AppSettings.General.ThemeMode`; `SettingsViewModel.ThemeMode`; DI + startup apply |
| **MainWindow/PatternCanvas/SettingsDialog** | All hardcoded colors → DynamicResource; Appearance GroupBox in General panel |
| **Icon resize ×1.4** | `IconBtn` FontSize 15→20; `Icon2D`/`Toggle2D` FontSize 14→19 |
| **Rounded icon buttons** | `ControlTemplate` CornerRadius=5 + theme-aware hover/pressed states |
| **CanvasBackground auto-switch** | `OnSettingsChanged` auto-updates canvas bg default when theme changes |

---

## Session 27 Changes (archived from SESSION_PROGRESS)

| Item | Detail |
|------|--------|
| **Bug — `ResizeMode="CanMinResize"`** | Invalid WPF enum value → `TypeConverterMarkupExtension` crash khi instantiate `ModelOrientationDialog`; fix: `CanMinimize` |
| **Bug — `ComputeRotation` reflection** | `Cross(front, up)` thay vì `Cross(up, front)` → right = (-1,0,0) với default → mesh bị mirror + texture biến mất; fix cross product order |
| **Bug — `Error()` outer-only message** | `ex.Message` chỉ show outer exception; fix: walk `InnerException` chain |
| **Cleanup — `BillboardTextVisual3D`** | Removed 6 axis label elements (HelixToolkit compat risk on .NET 8); replaced by TD-27-2 Canvas overlay |
| **TD-25-2 — Edge hover grid** | `BuildEdgeGrid()` + `_edgeScreenGrid`; `FindNearestEdge` O(n) → O(1); dirty on camera/mesh change |
| **TD-27-1 — Camera auto-fit** | `ZoomExtents(0)` via `Dispatcher.BeginInvoke(DispatcherPriority.Loaded)` in `ModelOrientationDialog` |
| **TD-27-2 — Axis labels overlay** | Canvas + 3 TextBlock; `Viewport3DHelper.Point3DtoPoint2D` on `CameraChanged` |
| **TD-27-3 — Parallel-axes warning** | `AxesAreParallel` + `[NotifyPropertyChangedFor]`; red TextBlock DataTrigger; OK Style trigger disabled |

---

## Session 26 Changes (archived from SESSION_PROGRESS — moved to BUGS_HISTORY in session 28)

| Item | Detail |
|------|--------|
| **TD-22-1 — Assimp material support** | `AssimpMeshLoader` reads `scene.Materials`, populates `mesh.MaterialNames`/`MaterialTexturePaths`/`SuggestedTexturePath`; uses `aMesh.MaterialIndex` → all Assimp-loaded faces now get correct `MaterialId` |
| **TD-22-2 — Multi-texture project persistence** | `ProjectState` +`MaterialTexturePaths` + `BundledMaterialTextureExts`; `ProjectSerializer` embeds/restores per-material textures as `texture_<matId>.<ext>` in `.4hu` bundles; `.pmc` relativizes paths |
| **TD-22-3 — Multi-material SVG export** | `UnfoldedFace` +`MaterialId`; `UnfoldEngine` propagates it; `IExporter` +`perMaterialTextures` param; `SvgExporter` emits per-face data URIs by material; `BuildExportLayout` passes `fd.MaterialId` |
| **TD-22-4 — Float edge-dedup fix** | `SvgExporter` + `PdfExporter`: `EdgeKey()` rounds to 3 dp and canonicalises order — replaces raw float tuple hash |
| **TD-22-5 — UV double-flip removed** | Removed `PostProcessSteps.FlipUVs` from `AssimpMeshLoader`; single `ToWpfUV` flip is correct |

---

## Session 25 Changes (archived from SESSION_PROGRESS)

| Item | Detail |
|------|--------|
| **Feature A — Model Orientation Dialog** | New `ModelOrientationDialog.xaml/.cs` + `ModelOrientationViewModel.cs` shown after every `LoadMesh`. 3D colored cube with 6 axis-labeled faces. Two ComboBoxes: Up + Front axis (each → ±X/Y/Z). `Mesh.ApplyTransform(Matrix4x4)` + `Mesh.FlipUVsVertical()` added. |
| **Feature B — 3D Edge Hover + RMB Pivot** | Context menu removed. `MouseMove` → screen-space edge proximity (8px); hover fold = red, hover cut = green; LMB = `ToggleEdge`. RMB click (non-drag) = `LookAt(hitPt, 300)` to set pivot. |
| **AppSettings** | +`EdgeHoverDetachColor`, `EdgeHoverAttachColor` in `View3DSettings` |
| **SettingsDialog** | New "3D Edge Edit Mode" GroupBox with 2 color pickers |
| **MainViewModel** | +`CurrentMesh`, +`EdgeHighlightModel`, +`HoverEdge/ClearEdgeHover`, +`BuildThinCylinder()` |

---

## Session 23 Changes (archived from SESSION_PROGRESS)

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

## All Bugs Fixed (sessions 1–17)

| Session | Severity | Bug | Fix |
|---------|----------|-----|-----|
| 4 (build) | Critical | `MainViewModel.cs` — extra spurious `}` after `CommitPreview` body → `CS1022` | Removed extra brace |
| 4 (build) | High | `App.xaml.cs` — `FourHUnfolder.Application` namespace shadowed `System.Windows.Application` → `CS0118` | Used `System.Windows.Application` explicit |
| 4 (build) | High | `PatternCanvasControl.xaml.cs` — `file static class` with `operator *` → `CS0715` | Replaced with `private Point Sc(Point p)` |
| 4 (build) | High | `MainWindow.xaml.cs` — missing `using System.Windows.Media` | Added using |
| 4 (build) | High | `MainViewModel.cs` — missing `using System.IO` → `Path`/`File` not found | Added using |
| 4 (build) | High | `MainViewModel.cs` — `Application.Current` → `CS0234` | Added `using WpfApp` alias |
| 4 (build) | Medium | `CommitPreview(_revert: true)` old parameter name → `CS1739` | Updated call site |
| 4 (build) | Medium | `PieceViewModel.FaceData` missing `EdgeIsBoundary` → `CS1061` | Added field |
| 1 | Critical | `PatternCanvasControl` drag broken — wrong mouse-capture element | `RootCanvas.CaptureMouse()` |
| 1 | Critical | Project save hardcoded `ScaleMmPerUnit = 1.0` | Use `ScaleMmPerUnit` property |
| 1 | High | `MainWindow.xaml.cs` missing `using System.Linq` | Added using |
| 1 | High | `UnfoldSetupDialog` custom-size boxes always disabled | Code-behind binding only |
| 1 | Medium | `CommitPreview` identical ternary branches | Clarified logic |
| 1 | Low | Degenerate triangles produced NaN normals → poisoned MST weights | Guard in `DualGraphBuilder` |
| 1 | Low | OBJ negative vertex indices not handled | Return -1 |
| 2 | Critical | `SettingsDialog.xaml` — 4 StackPanels as direct children of ScrollViewer | Wrapped in `<Grid>` |
| 2 | High | `SettingsViewModel.DisplayUnits` had `"mm (millimetre)"` → ComboBox blank | Changed to `["mm", "inch"]` |
| 2 | Medium | `PatternCanvasControl._vm!.GridVisible` null-forgiving could throw | Null-conditional guard |
| 2 | Low | `SvgExporter` face fill not greyed when `GrayscaleOutput = true` | CSS fill driven by setting |
| 5 | Critical | `Zoom_Changed` accessed `ZoomLabel.Text` before init — crash on startup | `if (ZoomLabel == null) return;` guard |
| 6 | High | `GlueTabGenerator` — boundary edges got unwanted glue tabs | Added `EdgeIsBoundary[i]` guard |
| 6 | Medium | `UnfoldEngine.FindSharedLocalIndices` — wrong apex on malformed topology | Throws if `n < 2`; origin fallback |
| 6 | Low | `ObjMeshLoader float.Parse()` threw on malformed tokens | Changed to `float.TryParse()` |
| 6 | Low | No feedback when mesh/texture file not found on project load | `Warnings` list in `ProjectState` |
| 8 | Critical | SVG export ignored canvas layout (position/rotation) | `BuildExportLayout()` applies per-piece transform |
| 8 | High | `PagesWide`/`PagesTall` not saved to `.pmc` | Added to `ProjectState`; saved/restored |
| 9 | Medium | `RunAutoArrange` created vertical-only pages | Fill pages horizontally; `PagesWide` grows |
| 9 | Medium | `EnsurePageForPosition` used unrotated bbox | Compute rotated bbox corners in `Canvas_MouseUp` |
| 10 | Low | SVG page label hardcoded `"FourHUnfolder Export"` | Use `Path.GetFileNameWithoutExtension(filePath)` |
| 10 | Low | `Mesh.GetOrAddEdge` overwrote `FaceB` on non-manifold topology | Guard: only assign `FaceB` when still `-1` |
| 10 | Low | O(n×faces) linear scan per 3D click | Added `_faceToGroup` dict; O(1) primary lookup |
| 12 | High | `TabData` lost `FaceId`/`LocalEdgeIdx`; export created tabs at (0,0) | Added fields to `TabData`; populated from `GlueTab` |
| 12 | Medium | Rotate ±90° and Flip-H not undoable | Capture pre-state; call `PushDragUndo` |
| 12 | Medium | `ObjMeshLoader` passed out-of-bounds vertex index → crash | Bounds check in `ParseFace`; invalid triangles skipped |
| 12 | Medium | `BuildTextureBrush` `Stretch=None` + pixel Viewport → DPI mismatch | `Viewport=(0,0,1,1)` + `Stretch=Fill` |
| 17 | High | `ObjMeshLoader` MTL filename with spaces parsed incorrectly | Use `line[7..]` to get rest-of-line |
| 17 | High | `SetMaterialTexture` didn't trigger canvas rebuild | `UpdateTextureUI()` called after every slot change |
| 17 | Medium | `RebuildMaterialSlots` index out-of-range on `MaterialTexturePaths` | Added `i < mesh.MaterialTexturePaths.Count` guard |

---

## Tech Debt — All Resolved (sessions 1–17)

| ID | Was | Resolution | Session |
|----|-----|-----------|---------|
| TD-1 | New pieces after join/split stacked at origin | Placed to right of paper boundary in 4-column grid | 7 |
| TD-2 | Shared edges drawn twice in 2D canvas | `HashSet<int>` dedup by mesh edge ID | 7 |
| TD-3 | O(n²) SAT on large meshes | AABB pre-check rejects non-overlapping pairs | 7 |
| TD-4 | Memory leak in PatternCanvasControl | Explicit subscription dict + unsubscribe on rebuild | 7 |
| TD-5 | Selection overlay rebuilt on every click | Frozen `Model3DGroup` cache per group ID | 7 |
| TD-6 | SVG fold/cut lines drawn twice | Canonical-key HashSet dedup | 7 |
| TD-7 | Triangle-grid visible; boundary indistinguishable from cut | Removed polygon stroke; boundary edges = thin dark grey | 8 |
| TD-8 | Texture not embedded in SVG | UV coords in `UnfoldedFace`; affine per-face transform in SVG | 8 |
| TD-9 | No undo/redo | `EditSnapshot`; `_undoStack`/`_redoStack`; `UndoCommand`/`RedoCommand` | 8 |
| TD-N1 | `EdgeMarker.Mark()` dead code | `EdgeMarker` now called from `UnfoldService` | 9 |
| TD-N2 | Tab depth/inset ratio hardcoded | Added to `AppSettings.PrintSettings`; exposed as sliders | 9 |
| TD-N3 | `ProjectState.Version` never validated | `ProjectSerializer.Load()` throws if `Version > CurrentVersion` | 9 |
| TD-N4 | No tests for Overlap/GlueTab/ObjLoader | `GeometryAlgorithmTests.cs` (13 tests) added | 10 |
| TD-N5 | SAT epsilon on unnormalized axis | Epsilon scaled by `axis.Length()` | 10 |
| TD-N6 | Undo/redo didn't cover piece drag moves | `_preDragPositions` captured on `MouseDown`; `PushDragUndo` on `MouseUp` | 10 |
| TD-N7 | `_currentScaleMmPerUnit` loose double field | Refactored to `private double ScaleMmPerUnit { get; set; }` property | 18 |
| TD-N8 | Epsilon values scattered across geometry files | `GeometryConstants.cs`; all geometry imports via `using static` | 10 |
| TD-R2 | Confusing `_edgeOverrides.ToDictionary(k=>k.Key, v=>v.Value)` | `new Dictionary<int,EdgeType>(_edgeOverrides)` | 12 |
| SvgExporter.AffineTransform | No unit tests for affine math | Extracted `AffineTransformHelper`; 5 tests added | 18 |
| TD-S7-1..7 | SVG layout / pages / auto-arrange issues | All fixed sessions 8–12 (see bug table above) | 8–12 |
| TD-S13-1 | `_dotRedBrush` unfrozen static SolidColorBrush | Changed to `HexBrush("#dc3232", "#dc3232")` which freezes | 19 |
| TD-S13-2 | Vertex dots shown for ALL pieces (hundreds on complex mesh) | `ShowVtxDots` now shows only selected pieces; falls back to all if none selected | 19 |
| TD-S13-3 | Rotate-by-point phase resets on any RebuildAll | `RebuildAll` saves pivot GroupId+position; restores phase=1 after ShowVtxDots if pivot dot found | 19 |
| TD-S14-1 | Lasso used AABB intersection (inaccurate for rotated pieces) | Replaced with `AnyVertexInLasso()` — checks actual canvas-space vertex positions | 19 |
| TD-S14-2 | Pan speed 1:1 pixel, no acceleration | Velocity-based acceleration: 1×–2.5× based on mouse speed per frame | 19 |
| TD-S14-3 | Paper size in Settings applied only on OK | `SettingsDialog` subscribes to `_vm.PropertyChanged`; live-applies on `DefaultPaperSizeName` change; Cancel reverts | 19 |
| UnfoldEngine `1e-6f` | Magic constant not using `GeometryConstants.DegenerateEdge` | Replaced `1e-6f` with `DegenerateEdge` in `ReconstructApex` | 19 |
| 3D texture (multi-material) | `BuildWpfModel` used single texture for all faces; multi-material OBJ showed wrong texture in 3D | Faces grouped by `MaterialId`; separate `GeometryModel3D` per material with its own `_materialBitmaps` entry; `_geoFaceIds` dict + `ResolveHitFaceId` preserves 3D pick correctness | 20 |
| `SetMaterialTexture` 3D blind | Changing slot in TextureDialog updated 2D but not 3D model | Added `MeshModel = BuildWpfModel(...)` call at end of `SetMaterialTexture` | 20 |
| Edge click too narrow | Edge Lines had 0.6–1.0 px stroke = 0.6–1.0 px hit area; required pixel-precise cursor | Added `EdgeTag` class; visual `Line` is `IsHitTestVisible=false`; second transparent `StrokeThickness=8` hit-zone Line on top carries all events | 20 |
| `RestoreProjectState` missing `RebuildMaterialSlots` | Project load re-loaded mesh but did not populate `_materialBitmaps`; 3D model built with empty/stale dict | Added `RebuildMaterialSlots(_currentMesh)` call before `BuildWpfModel` in `RestoreProjectState` | 21 |
| 3D ImageBrush UV distortion | `ImageBrush { Stretch=Fill }` without explicit Viewport caused UV distortion for multi-material groups | Set `ViewportUnits=Absolute`, `Viewport=(0,0,1,1)`, `TileMode=Tile` on every `ImageBrush` in `BuildWpfModel` | 21 |
| `SetMaterialTexture` 2D stale | `Canvas2DTexture` property change not raised after slot change → 2D canvas did not rebuild | Added `OnPropertyChanged(nameof(Canvas2DTexture))` at end of `SetMaterialTexture` | 21 |

---

## ⛔ Open Critical Bugs (unresolved as of session 22)

| ID | Severity | Description | Root Cause |
|----|----------|-------------|------------|
| CRITICAL-3D-TEX | Critical | 3D multi-material texture wrong after unfold | `CommitPreview` + `EnterPreview` call `BuildWpfModel` without `_materialBitmaps` → single texture applied to all material groups; also `_materialBitmaps[i]` may be null if MTL path missing |
