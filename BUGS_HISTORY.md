# 4H-Unfolder — Bug & Tech Debt History

> Moved from `SESSION_PROGRESS.md` at session 18.  
> This file accumulates all resolved issues from all past sessions.

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
| TD-S13-1..3 | Various low-priority canvas issues | Deferred — acceptable as-is | — |
| TD-S14-1..3 | Lasso accuracy, pan speed, paper-size live update | Deferred — acceptable as-is | — |
