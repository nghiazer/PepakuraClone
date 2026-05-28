# 4H-Unfolder — Session Progress Log

> **Last updated:** 2026-05-28 (session 38 — No-overlap auto-arrange, state reset on load, empty page trim; branch `fix/theme-system`)
> **Branch:** `fix/theme-system`  (base: `fix/ui-ux-polish` @ v0.0.3.D → current: v0.0.3.F)
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
| PDO load | `PdoMeshLoader` | Pepakura Designer v3/PD6: header, cipher, vertices, UV, shapes, zlib texture |
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
- **Parts alignment** — 6 toolbar buttons: Align L `◧` / R `◨` / T `⊤` / B `⊥` / Center-H `↔` / Center-V `◫`; undoable
- **Edge ID labels + glue arrows** on cut edges (pair numbers 1,2,3…); color in Settings
- **Multi-texture** — per-material texture slots; TextureDialog with material list; per-face texture in 2D
- **Save/Load `.4hu`** — self-contained ZIP bundle (mesh + texture + state)
- **Unsaved changes warning** on Load/Open/Close
- **Strip-packing auto-arrange** — FFD, try 90° rotation; guaranteed no bounding-box overlap (bug fixes: cap removal, rotation guard, page-advance guard)
- **State reset on model load** — zoom, page count, canvas scroll reset to default when loading a new mesh
- **Empty page trim after drag** — empty page columns/rows collapse automatically after piece movement
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
| `dotnet test` | ✅ 56 / 56 passed |
| `dotnet run --project src/FourHUnfolder.App` | ✅ App opens maximized; PDO files auto-unfold on load |
| Published `4H-Unfolder.exe` **v0.0.3.E** (win-x64, self-contained) | ✅ Session 37 |
| Published `4H-Unfolder.exe` **v0.0.3.F** (win-x64, self-contained) | ✅ Session 38 |

---

## Session 38 — Changes

| Item | Detail |
|------|--------|
| **Branch** | `fix/theme-system` continuing @ v0.0.3.E → v0.0.3.F |
| **No-overlap auto-arrange** | `RunAutoArrange` (MainViewModel.cs): (1) removed `pw/ph = Math.Min(…,usable*)` caps — allocated space now matches rendered size; (2) rotation condition adds `&& wNat <= usableH` guard — prevents over-tall rotated pieces; (3) page-advance check adds `localY > gap` guard — prevents infinite advance for oversized first-row pieces |
| **State reset on new model load** | `LoadMesh()`: resets `PagesWide=1`, `PagesTall=1`, `PixelsPerMm=DefaultPixelsPerMm` after `Pieces.Clear()`; fires `ViewResetRequested` event so canvas scrolls to origin |
| **`ViewResetRequested` event** | New `Action?` event on `MainViewModel`; `PatternCanvasControl.OnDataContextChanged` subscribes/unsubscribes; handler scrolls `Scroller` to top-left |
| **Empty page trim after drag** | New `GetCanvasBounds()` on `PieceViewModel` (rotated AABB in canvas mm); new `TrimEmptyPages()` on `MainViewModel` — detects empty page columns/rows, shifts piece positions, decrements `PagesWide`/`PagesTall`; called from `Canvas_MouseUp` after each drag |
| **Version** | `0.0.3.5 → 0.0.3.6` (v0.0.3.E → v0.0.3.F) |
| **Tests** | 56 / 56 pass |

---

## Session 37 — Changes

| Item | Detail |
|------|--------|
| **Branch** | `fix/theme-system` — branched from `fix/ui-ux-polish` @ v0.0.3.D |
| **Theme root cause** | `ThemeService.Apply()` used `merged.Insert(0, newDict)` → theme inserted at index 0 (lowest priority); App.xaml's static `LightTheme.xaml` at higher index always won every `DynamicResource` lookup → dark theme never applied to any binding except code-set values |
| **ThemeService core fix** | Changed `merged.Insert(0,…)` → `merged.Add(…)` — last entry wins in WPF `MergedDictionaries`; now the applied theme always overrides App.xaml's static baseline |
| **4 new theme keys** | Added `WarningTextFg`, `ColorSwatchBorderBrush`, `TransparencyCheckerA/B` to both `DarkTheme.xaml` and `LightTheme.xaml` |
| **WarningTextFg** | `ModelOrientationDialog.xaml`: hardcoded `#ff6666` → `{DynamicResource WarningTextFg}` — warning text now adjusts shade per theme |
| **ColorSwatchBorderBrush** | `SettingsDialog.xaml`: 17× `Stroke="#666"` → `{DynamicResource ColorSwatchBorderBrush}` — swatch borders visible in both themes |
| **CheckerBrush — freeze workaround** | `TextureDialog.xaml.cs`: `ApplyCheckerBrush()` builds `DrawingBrush` in code-behind using `TryFindResource("TransparencyCheckerA/B")`, stores in `Window.Resources["CheckerBrush"]`; XAML uses `{DynamicResource CheckerBrush}`; `OnActivated` override re-builds brush so live theme switch is picked up when dialog is re-focused |
| **ViewportBorderBrush** | `MainViewModel.cs`: getter now uses `TryFindResource("SplitterBg")` instead of hardcoded `#3d3d5c`; `OnSettingsChanged` notifies binding after `Apply()` |
| **Code review (simplify)** | `/simplify` audit of all changes: 3 confirmed findings: (1) CheckerBrush not refreshed on live switch — fixed by `OnActivated`; (2) `↕` icon was semantically reversed for CenterH — fixed to `↔`; (3) duplicate LightTheme entry in MergedDictionaries at startup — noted, harmless, deferred |
| **Version** | `0.0.3.4 → 0.0.3.5` (v0.0.3.D → v0.0.3.E); window title updated |
| **Tests** | 56 / 56 pass |

---

## Remaining Tech Debt

| ID | Priority | Status | Fixed in | Description |
|----|----------|--------|----------|-------------|
| **Performance** | 🟢 Low | 🔲 open | — | O(n²) overlap check (AABB + SAT); spatial grid needed for meshes > 2000 faces |

*(All other items resolved — see [`BUGS_HISTORY.md`](BUGS_HISTORY.md) for full history)*

---

## PDO Format Reference (PD6)

```
abs 0-9    : "version 3\n"  (ASCII, raw)
abs 10-13  : uint32 locked=6
abs 14-17  : uint32 unk1
abs 18-21  : uint32 version
abs 22-25  : uint32 localeLen (BYTES, not chars)
abs 26-..  : localeLen bytes locale UTF-16LE (RAW, no cipher)
abs 66-69  : uint32 cipher_key  (subtraction: decoded=(raw-key+256)%256)
abs 70-73  : uint32 commentLen (bytes)
abs 74-..  : commentLen bytes cipher-encoded comment (skip)
abs 380-499: 120 bytes pre-geometry settings (skip)
abs 500-.. : geometry + texture section

── Geometry ─────────────────────────────────────────────────
uint32 geoCount
  per geo:
    wstr  name        (uint32 byteLen + cipher UTF-16LE)
    bool  unk8
    uint32 vtxCount
    vtxCount × (double x, double y, double z)  ← RAW, no cipher
    uint32 shapeCount
      per shape:
        int32  unk11
        uint32 part    (2D paper part index)
        4 × double     (unk12)
        uint32 ptCount
          per point (85 bytes):
            uint32 vtxIdx      [+0]
            2×double coord     [+4]   ← 2D paper layout mm, NOT UV
            2×double unk13     [+20]  ← texture UV [0..1] (may tile outside)
            bool   unk14       [+36]
            3×double unk15     [+37]
            3×uint32 unk16a    [+61]
            3×float  unk16b    [+73]
    uint32 edgeCount
    edgeCount × 22 bytes  (unk17, skip)

── Texture section ──────────────────────────────────────────
uint32 texCount
  per texture:
    wstr  name
    80 bytes (5 × 4 floats, settings)
    bool  hasImage
    if hasImage:
      uint32 w, uint32 h
      uint32 csize
      csize bytes → zlib(RFC 1950) decompress → w×h×3 bytes RGB24 top-to-bottom
```

---

## File Inventory (~73 source files, ~6 400 lines)

```
Domain/Models/          Vertex Edge EdgeType Face Mesh PaperSizeModel ModelScale
                        EmbeddedTextureData
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
                        PdoMeshLoader
                        SvgExporter PdfExporter AffineTransformHelper

App/ViewModels/         MainViewModel PieceViewModel SettingsViewModel
                        MaterialTextureViewModel AssemblyViewModel ModelOrientationViewModel
App/Controls/           PatternCanvasControl
App/Dialogs/            UnfoldSetupDialog SettingsDialog TextureDialog
                        AssemblyAnimationWindow ModelOrientationDialog
App/Converters/         HexColorBrushConverter
App/Services/           ThemeService
App/Themes/             LightTheme.xaml DarkTheme.xaml
App/                    MainWindow App

Tests/                  MstAlgorithmTests (6)  UnfoldEngineTests (9)
                        GeometryAlgorithmTests (13)  SvgExporterTests (5)
                        PdoMeshLoaderTests (7)
App/Assets/             app.ico (6 sizes) logo.png
```

---

## Recommended Next Steps

1. **Performance** — Spatial grid / bucket partition for `OverlapDetector`; current O(n²) AABB loop becomes a bottleneck on meshes > 2000 faces
2. **Merge `fix/theme-system` → `main`** — branch is stable at v0.0.3.F; theme fix + UX bug fixes applied
