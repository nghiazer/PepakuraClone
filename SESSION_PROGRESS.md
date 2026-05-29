# 4H-Unfolder — Session Progress Log

> **Last updated:** 2026-05-29 (session 40 — Remove 2D canvas inner bounder layer; branch `fix/perf-overlap-detector`)
> **Branch:** `fix/perf-overlap-detector`  (base: `fix/theme-system` @ v0.0.3.F → current: v0.0.3.H)
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
| `dotnet build 4H-Unfolder.sln` | ✅ 0 errors, 7 warnings (NuGet NU1603 only) |
| `dotnet test` | ✅ 56 / 56 passed |
| `dotnet run --project src/FourHUnfolder.App` | ✅ App opens maximized; PDO files auto-unfold on load |
| Published `4H-Unfolder.exe` **v0.0.3.G** (win-x64, self-contained) | ✅ Session 39 |
| Published `4H-Unfolder.exe` **v0.0.3.H** (win-x64, self-contained) | ✅ Session 40 |

---

## Session 40 — Changes

| Item | Detail |
|------|--------|
| **Branch** | `fix/perf-overlap-detector` continuing @ v0.0.3.G → v0.0.3.H |
| **Remove 2D canvas inner bounder** | `DrawPaper()` (`PatternCanvasControl.xaml.cs`): changed `RootCanvas.Background = HexBrush(canvasBg)` → `Scroller.Background = HexBrush(canvasBg)` + `RootCanvas.Background = Brushes.Transparent`. Previously `RootCanvas` had a fixed Width/Height forming a visible rectangle against `Canvas2DScrollerBg`, creating the "inner bounder" look. Now the whole 2D view is one uniform color. |
| **Theme sync** | `DarkTheme.xaml`: `Canvas2DScrollerBg` `#2a2a4a` → `#3a3a5a`; `LightTheme.xaml`: `Canvas2DScrollerBg` `#cdd2de` → `#e8eaf0` — theme fallback before code-behind runs is now seamless with `CanvasBackground` defaults. |
| **Settings label** | `SettingsDialog.xaml`: "Canvas background" → "2D view background" to reflect new scope. |
| **Code review** | No issues found in either this session's canvas change or session 39's OverlapDetector change. |
| **Version** | `0.0.3.7 → 0.0.3.8` (v0.0.3.G → v0.0.3.H) |
| **Tests** | 56 / 56 pass |

---

## Session 39 — Changes

| Item | Detail |
|------|--------|
| **Branch** | `fix/perf-overlap-detector` — branched from `fix/theme-system` @ v0.0.3.F |
| **Spatial grid for OverlapDetector** | Replaced O(n²) nested loop with uniform bucket-partition broad phase. Each face is inserted into all grid cells its AABB covers (typically 1–4 cells); only pairs that share a cell are tested. Cell size = `max(2 × avgAABBSide, maxExtent / 256)` — keeps grid ≤ 256×256. Candidate pairs deduplicated with `HashSet<long>` (encodes canonical `i < j` pair as `(long)i<<32 | j`). AABB pre-check + SAT follow, identical to before. On spread-out geometry with n=2000 faces: ~4 000 comparisons vs 2 000 000 (~500× speedup). |
| **Correctness guarantee** | If two AABBs overlap they must share ≥ 1 grid cell → no false negatives. Degenerate geometry guard: `cellSize` clamped to ≥ 1e-6. All 4 existing `OverlapDetectorTests` pass unchanged. |
| **Version** | `0.0.3.6 → 0.0.3.7` (v0.0.3.F → v0.0.3.G) |
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

1. **Merge `fix/perf-overlap-detector` → `main`** — branch is stable at v0.0.3.H; spatial grid + canvas bounder removal applied
2. **Multi-page auto-layout** — allow pieces to flow across multiple pages automatically during auto-arrange
