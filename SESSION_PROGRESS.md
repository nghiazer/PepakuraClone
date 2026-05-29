# 4H-Unfolder — Session Progress Log

> **Last updated:** 2026-05-29 (session 43 — Dialog UX refactor × 3: ModelOrientation + UnfoldSetup + AssemblyAnimation; branch `feat/toolbar-ux`)
> **Branch:** `feat/toolbar-ux`  (base: `fix/perf-overlap-detector` @ v0.0.3.H → current: v0.0.4.C)
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
| Overlap | `OverlapDetector` | Spatial grid broad-phase + AABB pre-check + SAT |
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
| `dotnet build 4H-Unfolder.sln` | ✅ 0 errors, 5 warnings (NuGet NU1603 only) |
| `dotnet test` | ✅ 56 / 56 passed |
| `dotnet run --project src/FourHUnfolder.App` | ✅ App opens maximized; PDO files auto-unfold on load |
| Published `4H-Unfolder.exe` **v0.0.3.H** (win-x64, self-contained) | ✅ Session 40 |
| Published `4H-Unfolder.exe` **v0.0.4.A** (win-x64, self-contained) | ✅ Session 41 |
| Published `4H-Unfolder.exe` **v0.0.4.B** (win-x64, self-contained) | ✅ Session 42 |
| Published `4H-Unfolder.exe` **v0.0.4.C** (win-x64, self-contained) | ✅ Session 43 |

---

## Session 43 — Changes

| Item | Detail |
|------|--------|
| **Branch** | `feat/toolbar-ux` continuing @ v0.0.4.B → v0.0.4.C |
| **Toolbar icon fix** | `MainWindow.xaml`: Unfold button icon `⚙` → `📐` — eliminates confusion with Settings `⚙` icon |
| **ModelOrientationDialog refactor** | (session 43, committed `aed9582`) Layout overhaul: `Skip` → `Cancel` (neutral style); `OK` → `Import` (accent, Height 30→34); `AXIS REFERENCE` TextWrapping=Wrap + Height=Auto; FlipUV checkbox+tip moved into right column under Front axis (no gray box); `Don't ask again` to footer left; description + tip `TextMuted` → `TextSecondary` |
| **UnfoldSetupDialog refactor** | Unified 2-col grid (130px label / `*` control): row order Preset → Orientation → Custom Size (logical top-down flow). `CustomInput` style adds `Opacity=0.45` when `IsEnabled=False` — locked fields visually dimmed. Footer buttons Height 30→34 + `VerticalContentAlignment=Center`. |
| **AssemblyAnimationWindow refactor** | Legend overlay removed from viewport (100% clean 3D space). New Row 2: Step Timeline Slider (Minimum=0, Maximum=`StepMaxIndex`, TwoWay bind `CurrentStep`, IsSnapToTickEnabled). Controls toolbar → 3-column Grid: step description left · 5 nav buttons centre · 3-item legend right. Step description moved from separate Row 1 Border into toolbar left col. |
| **AssemblyViewModel: StepMaxIndex** | New computed property `StepMaxIndex = Math.Max(Length-1, 0)` for Slider Maximum binding |
| **AssemblyViewModel: OnCurrentStepChanged** | New `partial void OnCurrentStepChanged(int)` — Slider drag triggers `_animT=1.0` + `RefreshModel()`. Guard `_suppressStepRefresh` prevents double-render when timer internally increments `CurrentStep`. |
| **Bug fix (review)** | Timer tick sets `CurrentStep++` wrapped with `_suppressStepRefresh=true/false` — prevents `OnCurrentStepChanged` double-calling `RefreshModel()` during auto-play |
| **Publish cleanup** | `publish/v0.0.2.A–H` → `publish/v0.0.2.zip` (530 MB); `publish/v0.0.3.A–H` → `publish/v0.0.3.zip` (529 MB); 16 folders deleted |
| **Version** | `0.0.4.1 → 0.0.4.2` (v0.0.4.B → v0.0.4.C) |
| **Tests** | 56 / 56 pass |

---

## Session 42 — Changes

| Item | Detail |
|------|--------|
| **Branch** | `feat/toolbar-ux` continuing @ v0.0.4.A → v0.0.4.B |
| **GroupBox consolidation** | `SettingsDialog.xaml`: reduced from 22 GroupBoxes across 4 tabs → 8 GroupBoxes (3D: 6→2, 2D: 9→3, Print: 5→2, General: 2→1). Sub-headings use new `SubHead` TextBlock style instead of nested GroupBox. |
| **Unified grid layout** | Each GroupBox now uses a single outer Grid with fixed 3-column layout (190px label / `*` control / 64px numeric) + RowDefinitions. Eliminates per-row `<Grid.ColumnDefinitions>` repetition and the mix of 190/28/110, 190/140/40, 190/140/50 etc. widths. |
| **Slider + NumericBox two-way** | All 19 sliders: static TextBlock → `NumericBox` TextBox with `Mode=TwoWay, UpdateSourceTrigger=LostFocus`. Kéo slider → textbox cập nhật; gõ số → Tab/click ra → slider nhảy. New `NumericBox` style (BasedOn InputBox, Width=58, TextAlignment=Right). |
| **Footer buttons fix** | Height 30→34 for all 4 buttons (OK/Apply/Cancel/Reset). Added `HorizontalContentAlignment="Center" VerticalContentAlignment="Center"`. Removed space-padding hack `"  OK  "` → `"OK"`. Fixes "Applv" clipping bug. |
| **Scroll reset on tab switch** | `SettingsDialog.xaml.cs` `NavList_SelectionChanged`: added `ContentScroller.ScrollToVerticalOffset(0)`. ScrollViewer named `x:Name="ContentScroller"`. |
| **Bug fix (review)** | `ContentScroller.ScrollToTop()` không phải WPF API → fixed to `ScrollToVerticalOffset(0)`. |
| **Version** | `0.0.4.0 → 0.0.4.1` (v0.0.4.A → v0.0.4.B) |
| **Tests** | 56 / 56 pass |

---

## Remaining Tech Debt

*(No open tech debt — see [`BUGS_HISTORY.md`](BUGS_HISTORY.md) for full history)*

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

1. **Merge `feat/toolbar-ux` → `main`** — branch is stable at v0.0.4.C; all dialog UX overhauls complete
2. **Multi-page auto-layout** — allow pieces to flow across multiple pages automatically during auto-arrange
3. **UX polish** — 3D viewport navigation controls (trackpad pinch-zoom, keyboard shortcuts)
