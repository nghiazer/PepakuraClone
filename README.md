# PepakuraClone

A Pepakura-style paper model unfolder built with **WPF / .NET 8**.  
Load a 3-D OBJ mesh, unfold it into a printable 2-D pattern, customise the layout, and export to SVG.

---

## Prerequisites

| Requirement | Download |
|-------------|---------|
| .NET 8 **SDK** | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Windows 10/11 (WPF) | — |

> The machine may already have the .NET 8 *runtime*; the **SDK** is also needed to compile.

---

## Build & Run

```bash
dotnet restore
dotnet build          # 0 errors, 0 warnings
dotnet run --project src/PepakuraClone.App
```

### Tests

```bash
dotnet test tests/PepakuraClone.Tests   # 16 / 16 pass
```

---

## Features

### Load & display
- Import **Wavefront OBJ** files (v, vt, f with v/vt/vn tokens, n-gon fan-triangulation)
- Auto-loads associated **texture** from the companion `.mtl` file (`map_Kd`)
- Interactive **HelixToolkit 3-D viewport** — LMB orbit, MMB pan, scroll zoom

### Texture management
- Load / replace / remove texture with **live preview** before committing
- Orange border + badge indicates active preview mode; **Apply / Cancel** to confirm

### Unfold
- Click **Unfold** → **setup dialog** appears first:
  - Real-world target size (axis + value + unit: mm/cm/inch)
  - Paper size (A4 / A3 / A2 / A1 / Letter / Legal / Custom, Portrait or Landscape)
- Algorithm: dual-graph MST (Kruskal + Union-Find) → BFS triangle flattening
- MST edges = **Fold** (dashed blue), non-MST = **Cut** (solid red)
- Trapezoidal **glue tabs** generated on every cut edge

### Interactive 2-D layout canvas
| Action | Result |
|--------|--------|
| Drag piece | Moves the piece on the paper |
| Right-click edge | **Join pieces** (Cut→Fold) or **Split piece** (Fold→Cut) |
| Select piece | Highlights the corresponding faces in the 3-D viewport |
| Toolbar: Rotate ±90° / Flip H | Rotates or mirrors selected piece |
| Toolbar: ⊞ Grid | Toggle grid show/hide immediately (no rebuild) |
| Toolbar: ⊟ Snap | Toggle snap-to-grid when dragging |
| Toolbar: Auto-arrange | Row-packs all pieces onto the paper using the configured gap |
| Toolbar: ↩ Undo / ↪ Redo | Undo/redo edge changes (Ctrl+Z / Ctrl+Y) |

Edge type visual key in the 2-D canvas:

| Style | Meaning |
|-------|---------|
| Dashed blue | Fold edge — paper bends here |
| Solid red | Cut edge — separate pieces, needs glue tab |
| Thin dark grey | Boundary edge — outer mesh boundary |

### 3-D face selection + Detach / Attach
| Action | Result |
|--------|--------|
| Left-click face (3-D) | Selects face; highlights piece (yellow overlay) + matching 2-D piece |
| Right-click face (3-D) | **Detach this face** / **Detach entire piece** / **Attach to face N** |
| Click piece (2-D) | Updates 3-D selection overlay — bidirectional sync |

### Settings (`⚙ Settings` button)
Four sections, all persisted to `%AppData%\PepakuraClone\settings.json`:

| Section | Notable options |
|---------|----------------|
| **3D View** | Background color · Display mode (Solid / SolidEdges / Wireframe) · Face & back-face color · Face opacity · Edge overlay · Lighting · **Camera FOV + near/far clip planes** |
| **2D View** | Canvas & paper color · **Grid show/size/color** · Face fill · Fold/cut line color+width+dash · Glue tabs · Face numbers · **Piece gap (mm)** · **Snap to grid** · Default zoom |
| **Print/Export** | Page margin · Bleed · SVG scale · Fold/cut line colors & widths · Grayscale output |
| **General** | **Display unit** (mm / inch) — affects all dimension labels in the UI |

### Save / Load project (`.pmc`)
- Saves: mesh path, texture path, real-world scale, paper size, edge overrides, piece layouts
- On load: re-runs unfold with saved overrides, then restores piece layout

### Export SVG
- Produces a standalone `.svg` with face fills, fold/cut/boundary lines, glue tabs
- **UV-mapped texture** embedded as a base-64 data URI with per-face affine transform (when the mesh has UV data and a texture is loaded)
- All settings driven by the **Print** settings section

---

## Architecture

```
PepakuraClone.sln
├── src/
│   ├── PepakuraClone.Domain          # Pure models — no external deps
│   │   ├── Models/        Vertex, Edge (EdgeType), Face, Mesh
│   │   │                  PaperSizeModel, ModelScale
│   │   ├── DualGraph/     DualGraph, GraphNode, GraphEdge
│   │   ├── Results/       UnfoldedFace, GlueTab, UnfoldResult
│   │   ├── Settings/      AppSettings (View3D + View2D + Print + General)
│   │   └── Persistence/   ProjectState (JSON DTO)
│   │
│   ├── PepakuraClone.Geometry        # Algorithms (→ Domain)
│   │   └── Algorithms/    DualGraphBuilder, KruskalMstBuilder, EdgeMarker,
│   │                       UnfoldEngine, OverlapDetector (AABB + SAT),
│   │                       GlueTabGenerator, PieceComputer
│   │
│   ├── PepakuraClone.Application     # Use-case services (→ Domain, Geometry)
│   │   ├── Interfaces/    IMeshLoader, IExporter
│   │   └── Services/      MeshService, UnfoldService,
│   │                       ProjectSerializer, SettingsService
│   │
│   ├── PepakuraClone.Infrastructure  # I/O (→ Domain, Application)
│   │   ├── Loaders/       ObjMeshLoader
│   │   └── Exporters/     SvgExporter
│   │
│   └── PepakuraClone.App             # WPF UI (→ Application, Infrastructure)
│       ├── ViewModels/    MainViewModel, PieceViewModel, SettingsViewModel
│       ├── Controls/      PatternCanvasControl
│       ├── Dialogs/       UnfoldSetupDialog, SettingsDialog (4-panel)
│       ├── Converters/    HexColorBrushConverter
│       └── MainWindow.xaml
│
└── tests/
    └── PepakuraClone.Tests  # xunit + FluentAssertions — 15 tests
```

### Dependency graph

```
Domain ─→ Geometry ─→ Application ─→ Infrastructure ─→ App
                                                        ↑
                                            HelixToolkit.WPF
                                            CommunityToolkit.Mvvm
                                            Microsoft.Extensions.DependencyInjection
```

---

## Unfold pipeline

| Step | Class | What it does |
|------|-------|-------------|
| 1 | `ObjMeshLoader` | Parse `.obj`, build `Mesh` with canonical edge-adjacency, read MTL |
| 2 | `DualGraphBuilder` | One node per face; edges weighted by dihedral angle; degenerate-face guard |
| 3 | `KruskalMstBuilder` | Kruskal + path-compressed Union-Find → MST |
| 4 | `EdgeMarker` | MST → Fold; non-MST interior → Cut; boundary → Boundary |
| 5 | `UnfoldEngine` | BFS flattening; circle-circle apex; populates `EdgeIsBoundary` and `UVCoords` per face |
| 6 | `OverlapDetector` | AABB pre-check + SAT; sets `UnfoldResult.HasOverlaps` |
| 7 | `GlueTabGenerator` | Trapezoidal tabs on cut edges (tagged with FaceId) |
| 8 | `PieceComputer` | Union-Find on fold graph → connected components |
| 9 | `SvgExporter` | Edge-deduplicated SVG; boundary/fold/cut styling; UV-mapped texture via affine transform |

---

## NuGet packages

| Package | Version | Purpose |
|---------|---------|---------|
| `HelixToolkit.WPF` | 2.25.0 | 3-D viewport with orbit/pan/zoom |
| `CommunityToolkit.Mvvm` | 8.3.2 | `[ObservableProperty]` / `[RelayCommand]` source generators |
| `Microsoft.Extensions.DependencyInjection` | 8.0.1 | Constructor injection |

---

## File formats

| Format | Role |
|--------|------|
| `.obj` | Input mesh |
| `.mtl` | Optional — diffuse texture path (`map_Kd`) |
| `.png/.jpg/.bmp` | Texture images |
| `.pmc` | PepakuraClone project — JSON session snapshot |
| `.svg` | Export — printable 2-D pattern |

---

## Quick test — tetrahedron

Save as `tetrahedron.obj` and open with **Load Mesh**:

```
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

- Overlap detection is O(n²) after AABB rejection — still slow on meshes > ~2 000 faces
- Undo/redo only covers edge changes (join/split/detach) — piece positions are partially restored on undo but may drift for complex sequences
- SVG texture embedding requires a loaded texture and UV-mapped mesh; plain OBJ without `vt` coordinates exports without texture
