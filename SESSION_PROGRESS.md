# 4H-Unfolder — Session Progress Log

> **Last updated:** 2026-05-25 (session 35 — BUG-PDO-3: PDO piece disappears on first click; branch `feat/pdo-import`)
> **Branch:** `feat/pdo-import`  (base: `main` @ v0.0.2.H → current: v0.0.3.B)
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
| `dotnet test` | ✅ 56 / 56 passed |
| `dotnet run --project src/FourHUnfolder.App` | ✅ App opens, PDO files auto-unfold on load |
| Published `4H-Unfolder.exe` **v0.0.3.B** (win-x64, self-contained) | ✅ Session 34 |
| BUG-PDO-3 fix | ✅ Session 35 — PDO piece no longer disappears on first click |
| Published `4H-Unfolder.exe` **v0.0.3.C** (win-x64, self-contained) | ✅ Session 35 |

---

## Session 35 — Changes

| Item | Detail |
|------|--------|
| **BUG-PDO-3 root cause** | `RunAutoArrange` rot=90 branch: `PositionX = localX - (-minY)` → double-negation error; evaluates to `localX + minY`. For centered pieces `minY < 0`, placing most of the rotated piece at negative canvas_X (off-screen left). Users saw pieces "disappear" when clicking PDO pieces. |
| **BUG-PDO-3 fix** | `MainViewModel.cs` `RunAutoArrange`: changed to `localX + minY + hNat` (`= localX + maxY`). After a 90° CW `RotateTransform`, canvas_X_min occurs at `ly = maxY`, so `PositionX` must be ≥ `maxY` to keep the piece on-screen. |
| **Defensive guard** | `PatternCanvasControl.xaml.cs`: added `ScrollToShowPiece(piece)` call in `IsSelected` PropertyChanged handler; added `ScrollToShowPiece` method that centers the viewport on a piece if it's outside the visible scroll area. |
| **Why PDO-specific** | PDO paper-model strips are wide (wNat > hNat), frequently hitting the `rot=90` branch. OBJ/FBX mesh pieces are more equidimensional and rarely trigger it. |
| **Tests** | 56 / 56 pass (no new tests needed — bug is in UI layout arithmetic, no testable pure-logic unit) |

---

## Session 34 — Changes

| Item | Detail |
|------|--------|
| **Verified s30–33 fixes** | Confirmed TD-PDO-1, TD-PDO-2, CRITICAL-3D-TEX, BUG-PDO-1, BUG-PDO-2 all correctly implemented via code-graph exploration |
| **TD-PDO-4** | Added `_embeddedBitmapCache` (Dictionary<int, BitmapImage?>) to `MainViewModel`; `BitmapFromEmbedded` split into cached wrapper + `BitmapFromEmbeddedCore`; cache cleared on new mesh load — eliminates repeated PNG encode for 2048² textures |
| **TD-25-1** | "Don't ask again" checkbox in `ModelOrientationDialog`; new `SkipModelOrientationDialog` bool in `AppSettings.GeneralSettings`; `MainViewModel.LoadMesh` honours the setting + persists on check |
| **TD-PDO-3** | Pre-geo seek replaced: `Seek(120, Current)` → `Seek(154 + localeLen + commentLen, Begin)` — absolute formula derived from header byte-trace; correct for any localeLen (not just standard PD6 = 40) |
| **Tests** | All 56 pass |

### Tech debt summary (v0.0.3.A → v0.0.3.B)

| ID | Priority | Status | Description |
|----|----------|--------|-------------|
| ~~TD-PDO-3~~ | 🟢 | ✅ s34 | 120-byte pre-geo skip replaced by absolute offset formula |
| ~~TD-PDO-4~~ | 🟢 | ✅ s34 | `BitmapFromEmbedded` now cached; no repeated PNG encode |
| ~~TD-25-1~~  | 🟢 | ✅ s34 | `ModelOrientationDialog` — "Don't ask again" checkbox added |

---

## Session 30 — Changes

| Item | Detail |
|------|--------|
| **PDO import Phase 1 — 3D geometry** | `PdoMeshLoader.cs` (new): parses PD6 header (sig, localeLen, cipher key, commentLen), skips 120-byte pre-geo settings, reads geo_count + per-geo: cipher-decoded wstr name, raw vertices (3×double), polygon shapes fan-triangulated into triangles, skips unk17 edge data |
| **PDO cipher** | Subtraction cipher `decoded = (raw−key+256)%256`; applies only to `wstr` fields; all int/double/bool fields are raw LE |
| **PDO wstr format** | `uint32` byteLen (raw, NOT char count) + byteLen bytes of cipher-encoded UTF-16LE; trailing null stripped |
| **PDO pre-geo skip** | geo_count always at abs `74+commentLen+120` = abs 500 for PD6; verified on all 3 sample files |
| **PDO import Phase 2 — UVs + embedded texture** | Per-point: read `unk13` (offsets 20-35 after vtxIdx) as texture UV [0,1]; per-shape: populate `mesh.UVs` + `mesh.FaceUVs`; fan-triangulate with UV indices |
| **PDO texture decompression** | After all geos: read texture section (wstr name + 80 bytes settings + bool hasImage + w/h/csize + zlib-compressed RGB24); `ZLibStream` decompress → `mesh.EmbeddedTextures` |
| **`EmbeddedTextureData` record** | New `Domain/Models/EmbeddedTextureData.cs`: `(Name, Width, Height, Rgb24Bytes)` |
| **`Mesh.EmbeddedTextures`** | New `List<EmbeddedTextureData>` property on Mesh |
| **`MainViewModel.BitmapFromEmbedded`** | New helper: `BitmapSource.Create(Rgb24)` → `PngBitmapEncoder` → in-memory `BitmapImage`; frozen for cross-thread use |
| **`RebuildMaterialSlots` PDO fallback** | When `MaterialNames.Count == 0` and `SuggestedTexturePath == null`: uses `BitmapFromEmbedded(mesh.EmbeddedTextures[0])` → stores at `_materialBitmaps[-1]` → picked up by `BuildWpfModel` for 3D texture display |
| **File dialog + tooltip** | `MainViewModel`: added `*.pdo` to Open Mesh filter; `MainWindow.xaml`: tooltip updated |
| **`MultiFormatMeshLoader`** | Routes `.pdo` → `PdoMeshLoader` |
| **Tests** | 7 new `PdoMeshLoaderTests`: geometry valid, vertex count exact, UVs finite in-bounds, embedded textures present with correct w/h/byte-count, invalid-signature guard; 41/41 suite green |
| **Empirical recon** | Verified on 3 sample files: SoundEmitter (132 KB, 5 geos, 128×128 tex), waluigiblimp (6.1 MB, 2 geos, 2048×2048 tex), Pillar (18.4 MB, 1 geo, 2×textures 2048² + 1440×2880) |

---

## Session 29 — Changes (archived → BUGS_HISTORY.md)

| Item | Detail |
|------|--------|
| **TD-28-4 / TD-28-1 / TD-28-3** | Theme-aware: 3D bg auto-switch; SettingsDialog footer; 4 dialogs (Assembly/Texture/UnfoldSetup/ModelOrientation) |
| **TD-24-1** | PieceFoldTree fold direction fix (`EdgeDir3D` + `signCorr`) |
| **Build/Test** | ✅ 0 errors / 34 tests / app opens clean; published v0.0.2.H |

---

## Remaining Tech Debt

| ID | Priority | Status | Fixed in | Description |
|----|----------|--------|----------|-------------|
| ~~TD-PDO-1~~ | 🟡 Med | ✅ fixed | s32 | `coord` doubles per point extracted → `PdoLayout`; auto-unfold on load |
| ~~TD-PDO-2~~ | 🟡 Med | ✅ fixed | s31 | Multi-texture PDO: per-face `MaterialId` from `unk11`; `MaterialNames` from `EmbeddedTextures` |
| ~~TD-PDO-3~~ | 🟢 Low | ✅ fixed | s34 | Pre-geo seek: `Seek(120, Current)` → `Seek(154+localeLen+commentLen, Begin)` |
| ~~TD-PDO-4~~ | 🟢 Low | ✅ fixed | s34 | `BitmapFromEmbedded` cached via `_embeddedBitmapCache`; core only called once per texture per mesh |
| ~~CRITICAL-3D-TEX~~ | 🔴 Critical | ✅ fixed | s32 | `EnterPreview`/`CommitPreview` now pass `_materialBitmaps` to `BuildWpfModel` |
| ~~TD-25-1~~ | 🟢 Low | ✅ fixed | s34 | "Don't ask again" checkbox in `ModelOrientationDialog`; persisted to `AppSettings.General` |
| ~~BUG-PDO-3~~ | 🟡 Med | ✅ fixed | s35 | `RunAutoArrange` rot=90 formula error → PDO pieces placed off-screen, appear to "disappear" on first click |
| **Performance** | 🟢 Low | 🔲 open | — | O(n²) overlap check (AABB + SAT); spatial grid needed for meshes > 2000 faces |

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
                        EmbeddedTextureData                                  ← NEW
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
                        PdoMeshLoader                                        ← NEW
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
                        PdoMeshLoaderTests (7)                               ← NEW
App/Assets/             app.ico (6 sizes) logo.png
```

---

## Recommended Next Steps

1. **Performance** — Spatial grid / bucket partition for `OverlapDetector`; current O(n²) AABB loop becomes a bottleneck on meshes > 2000 faces
2. **Merge `feat/pdo-import` → `main`** — branch is stable at v0.0.3.B; all tech debt cleared
