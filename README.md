# 4H-Unfolder

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blueviolet)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue)]()
[![Tests](https://img.shields.io/badge/Tests-56%2F56%20pass-brightgreen)]()

A Pepakura-style paper model unfolder built with **WPF / .NET 8**.  
Load a 3-D mesh, unfold it into a printable 2-D pattern, customise the layout, and export to SVG or PDF.

> Current version: **v0.0.3.E** (win-x64 self-contained EXE) — Dark/Light theme fix + UI/UX polish

---

## Prerequisites

| Requirement | Download |
|-------------|---------|
| .NET 8 **SDK** | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Windows 10 / 11 (WPF) | — |

---

## Build & Run

```bash
cd D:\CODING\UNFOLD
dotnet restore
dotnet build          # 0 errors, 4 NuGet warnings only
dotnet run --project src/FourHUnfolder.App
```

### Tests

```bash
dotnet test tests/FourHUnfolder.Tests   # 56 / 56 pass
```

---

## Supported input formats

| Format | Notes |
|--------|-------|
| **Wavefront OBJ** | v / vt / f, fan-triangulates n-gons; reads companion `.mtl` for textures |
| **Pepakura PDO** | PD6 / v3: 3-D geometry + UV + embedded texture (zlib RGB24) |
| **FBX / COLLADA / 3DS / STL / DXF / LWO / PLY** | Via AssimpNet 5 |

---

## Features

### Load & 3-D viewport
- Import any supported mesh format via **Load Mesh** (📂)
- **Model Orientation dialog** on every load: choose Up / Front axes (±X/Y/Z), optional UV-flip
- Interactive HelixToolkit 3-D viewport — LMB orbit, MMB pan, scroll zoom, RMB pivot
- Per-material **multi-texture** display in 3-D (each material group rendered separately)
- **Light / Dark theme** — toggle in Settings → General

### Texture management
- Load / replace / remove texture with **live preview** before committing
- Orange border + badge indicates active preview mode; Apply / Cancel to confirm
- **Texture Dialog** — per-material texture slots; browse or clear each slot independently

### Unfold
- Click **Unfold** → **setup dialog**:
  - Real-world target size (axis + value + unit: mm / cm / inch)
  - Paper size (A4 / A3 / A2 / A1 / Letter / Legal / Custom + Portrait / Landscape)
- Algorithm: dual-graph MST (Kruskal + Union-Find) → BFS triangle flattening
- MST edges = **Fold** (dashed blue), non-MST interior = **Cut** (solid red)
- Trapezoidal / rectangular / triangular **glue tabs** on every cut edge (shape, angle, depth configurable)

### Interactive 2-D layout canvas

| Action | Result |
|--------|--------|
| Drag piece | Moves piece on the paper |
| Double-click edge | **Auto-align** to nearest 90° — undoable |
| Lasso drag | Multi-select pieces |
| Toolbar Rotate ±90° / Flip H | Rotate or mirror selected piece — undoable |
| Toolbar Align L/R/T/B/Center-H/V | Align selected pieces — undoable |
| Auto-arrange | Strip-pack all pieces (sort by area, try 90° rotation) |
| Ctrl+Z / Ctrl+Y | Undo / Redo all edits |

Edge visual key:

| Style | Meaning |
|-------|---------|
| Dashed blue | Fold edge |
| Solid red | Cut edge |
| Thin dark grey | Boundary |

### Edge-Edit mode (✏)
- Hover any edge in the 3-D viewport → coloured tube highlight (attach = green, detach = red)
- LMB = toggle Fold ↔ Cut; undoable
- Edge colours configurable in Settings → 3D View

### Rotate-by-Point mode (⊙)
- Click pivot on canvas → drag handle to rotate piece to any angle — undoable

### Assembly animation (🎬)
- Step-by-step fold guide showing how to assemble the model
- Phase 1: true paper-fold — faces rotate around shared fold edges via accumulated Matrix4x4
- Phase 2: fly-in — folded shape translates to its final 3-D position
- Per-material texture on each piece; amber highlight on current step
- Play / Pause auto-animation; step controls ⏮ ◀ ▶ ⏭

### 3-D face selection
| Action | Result |
|--------|--------|
| Left-click face (3-D) | Selects face; yellow overlay + sync to 2-D |
| Right-click face (3-D) | Context menu: Detach / Attach |
| Click piece (2-D) | Bidirectional sync to 3-D selection |

### Settings dialog (⚙)
Four panels — all persisted to `%AppData%\4H-Unfolder\settings.json`:

| Panel | Notable options |
|-------|----------------|
| **3D View** | Background · Display mode (Solid / SolidEdges / Wireframe) · Face/back-face color · Opacity · Edge overlay · Lighting · Camera FOV/clip planes · **Edge-Edit hover colors** |
| **2D View** | Canvas & paper color · Grid (show/size/color) · Fold/cut line style · Glue tabs · Face numbers · Edge ID labels & arrows · Piece gap · Snap-to-grid |
| **Print** | Margin · Bleed · SVG scale · Tab shape/angle/depth · Alternate flaps · Grayscale |
| **General** | Display unit (mm / inch) · **Light / Dark theme** |

### Save / Load project (`.4hu`)
- Self-contained ZIP bundle: mesh file + textures + full session state
- Legacy `.pmc` (JSON) also supported for loading
- **Unsaved-changes warning** on Load / Open / Close

### Export
- **SVG** — UV-mapped texture per face (base64 data URI + affine transform); fold/cut/tab lines; edge dedup
- **PDF** — multi-page; fold/cut/tab lines; page labels

---

## Architecture

```
4H-Unfolder.sln
├── src/
│   ├── FourHUnfolder.Domain          # Pure models — zero external deps
│   │   ├── Models/        Vertex, Edge, Face, Mesh, EmbeddedTextureData
│   │   │                  PaperSizeModel, ModelScale
│   │   ├── DualGraph/     DualGraph, GraphNode, GraphEdge
│   │   ├── Results/       UnfoldedFace, GlueTab, UnfoldResult, AssemblyStep
│   │   ├── Settings/      AppSettings (View3D + View2D + Print + General)
│   │   └── Persistence/   ProjectState
│   │
│   ├── FourHUnfolder.Geometry        # Algorithms (→ Domain)
│   │   └── Algorithms/    DualGraphBuilder, KruskalMstBuilder, EdgeMarker,
│   │                       UnfoldEngine, OverlapDetector (AABB + SAT),
│   │                       GlueTabGenerator, PieceComputer,
│   │                       AssemblyPlanner, PieceFoldTree
│   │
│   ├── FourHUnfolder.Application     # Use-case services (→ Domain, Geometry)
│   │   ├── Interfaces/    IMeshLoader, IExporter
│   │   └── Services/      MeshService, UnfoldService,
│   │                       ProjectSerializer, SettingsService
│   │
│   ├── FourHUnfolder.Infrastructure  # I/O (→ Domain, Application)
│   │   ├── Loaders/       ObjMeshLoader, PdoMeshLoader, AssimpMeshLoader,
│   │   │                   MultiFormatMeshLoader
│   │   └── Exporters/     SvgExporter, PdfExporter, AffineTransformHelper
│   │
│   └── FourHUnfolder.App             # WPF UI (→ Application, Infrastructure)
│       ├── ViewModels/    MainViewModel, PieceViewModel, SettingsViewModel,
│       │                   MaterialTextureViewModel, AssemblyViewModel,
│       │                   ModelOrientationViewModel
│       ├── Controls/      PatternCanvasControl
│       ├── Dialogs/       UnfoldSetupDialog, SettingsDialog, TextureDialog,
│       │                   AssemblyAnimationWindow, ModelOrientationDialog
│       ├── Services/      ThemeService
│       ├── Themes/        LightTheme.xaml, DarkTheme.xaml
│       └── MainWindow.xaml
│
└── tests/
    └── FourHUnfolder.Tests   # xUnit + FluentAssertions — 41 tests
        MstAlgorithmTests (6), UnfoldEngineTests (9),
        GeometryAlgorithmTests (13), SvgExporterTests (5),
        PdoMeshLoaderTests (7) + AffineTransform (1)
```

### Dependency graph

```
Domain ──→ Geometry ──→ Application ──→ Infrastructure ──→ App
                                                            ↑
                                              HelixToolkit.WPF
                                              CommunityToolkit.Mvvm
                                              Microsoft.Extensions.DI
                                              AssimpNet 5
                                              PdfSharp.Standard
```

---

## Unfold pipeline

| Step | Class | What it does |
|------|-------|-------------|
| 1 | `MultiFormatMeshLoader` | Routes by extension → OBJ / PDO / Assimp |
| 2 | `DualGraphBuilder` | One node per face; dihedral-angle edge weights |
| 3 | `KruskalMstBuilder` | MST via Kruskal + path-compressed Union-Find |
| 4 | `EdgeMarker` | MST → Fold; non-MST interior → Cut; boundary → Boundary |
| 5 | `UnfoldEngine` | BFS flattening; circle-circle apex reconstruction |
| 6 | `OverlapDetector` | AABB pre-check + SAT |
| 7 | `GlueTabGenerator` | Configurable tabs on all cut edges |
| 8 | `PieceComputer` | Union-Find on fold graph → connected components |
| 9 | `SvgExporter` / `PdfExporter` | Edge-dedup; UV-mapped texture; multi-page PDF |

---

## NuGet packages

| Package | Version | Purpose |
|---------|---------|---------|
| `HelixToolkit.WPF` | 2.25.0 | 3-D viewport |
| `CommunityToolkit.Mvvm` | 8.3.2 | `[ObservableProperty]` / `[RelayCommand]` |
| `Microsoft.Extensions.DependencyInjection` | 8.0.1 | Constructor injection |
| `AssimpNet` | 5.0.0-beta1 | FBX / COLLADA / 3DS / STL / DXF / LWO / PLY import |
| `PdfSharp.Standard` | 1.51.8 | PDF export |

---

## File formats

| Format | Role |
|--------|------|
| `.obj` + `.mtl` | Input mesh (OBJ + material / texture reference) |
| `.pdo` | Input mesh (Pepakura Designer v3 / PD6, with embedded texture) |
| `.fbx`, `.dae`, `.3ds`, `.stl`, `.dxf`, `.lwo`, `.ply` | Input mesh (via Assimp) |
| `.png` / `.jpg` / `.bmp` / `.tiff` | Texture images |
| `.4hu` | 4H-Unfolder project bundle (ZIP: mesh + textures + state JSON) |
| `.pmc` | Legacy project format (JSON, load-only) |
| `.svg` | Export — printable 2-D pattern with embedded texture |
| `.pdf` | Export — multi-page printable pattern |

---

## Quick test — tetrahedron

Save as `tetrahedron.obj` and open with **Load Mesh**:

```obj
# Simple tetrahedron
v  0.0  0.0  0.0
v  1.0  0.0  0.0
v  0.5  1.0  0.0
v  0.5  0.5  1.0
f 1 2 3
f 1 2 4
f 2 3 4
f 1 3 4
```

Expected after **Unfold** (A4, 200 mm longest axis):  
4 triangular faces flat, 3 dashed fold lines, 3 solid cut lines, 3 glue tabs.

---

## Known limitations

- Overlap detection is O(n²) after AABB rejection — slow on meshes > ~2 000 faces
- Undo/redo covers edge edits and piece transforms; complex multi-step sequences may have minor position drift
- SVG texture requires UV-mapped mesh; plain geometry without UV coordinates exports without texture

---

## Contributing

Contributions, bug reports and feature requests are welcome!  
See [CONTRIBUTING.md](CONTRIBUTING.md) for setup instructions, branch workflow and coding conventions.

---

## License

[MIT](LICENSE) © 2026 NghiaZer
