# 4H-Unfolder — Claude Code Guide

## Build & Run

```powershell
dotnet restore
dotnet build                          # 0 errors, 4 NuGet NU1603 warnings only
dotnet run --project src/FourHUnfolder.App
dotnet test tests/FourHUnfolder.Tests # 41/41 pass
```

## Architecture

```
Domain → Geometry → Application → Infrastructure → App
```

No circular dependencies. Domain has **zero** external deps.

| Layer | Project | Key classes |
|-------|---------|-------------|
| Domain | `FourHUnfolder.Domain` | `Mesh`, `Face`, `Edge`, `UnfoldResult`, `AppSettings`, `EmbeddedTextureData` |
| Geometry | `FourHUnfolder.Geometry` | `UnfoldEngine`, `KruskalMstBuilder`, `DualGraphBuilder`, `GlueTabGenerator`, `PieceFoldTree` |
| Application | `FourHUnfolder.Application` | `MeshService`, `UnfoldService`, `ProjectSerializer`, `SettingsService` |
| Infrastructure | `FourHUnfolder.Infrastructure` | `ObjMeshLoader`, `PdoMeshLoader`, `AssimpMeshLoader`, `SvgExporter`, `PdfExporter` |
| App (WPF) | `FourHUnfolder.App` | `MainViewModel`, `PatternCanvasControl`, `AssemblyViewModel` |

## Code-Graph MCP Tools — Use These First

A local MCP server (`code-graph`) indexes this project.
**Always query it before opening source files** — it saves 5–15× tokens.

### Recommended workflow

```
1. get_project_map()                    → orient yourself at session start
2. find_definition("SymbolName")        → file + exact line, never grep manually
3. get_class_members("ClassName")       → full member list without reading the file
4. get_file_outline("FileName.cs")      → structure of one file (all symbols + lines)
5. find_usages("SymbolName")            → who references this?
6. search_code("pattern", "*.cs")       → grep with context
7. reindex()                            → only if you added/renamed files this session
```

### Token-cost comparison

| Task | Without MCP | With MCP |
|------|------------|---------|
| Find where `BuildWpfModel` is defined | Read `MainViewModel.cs` ≈ 6 000 tok | `find_definition` ≈ 30 tok |
| Understand `UnfoldResult` class | Read file ≈ 400 tok | `get_class_members` ≈ 60 tok |
| Find all usages of `IExporter` | Grep 61 files ≈ 1 500 tok | `find_usages` ≈ 80 tok |
| Session orientation | Read 5 overview files ≈ 10 000 tok | `get_project_map` ≈ 200 tok |

### When to Read actual files

Only open a source file when you need the **implementation body** — logic, algorithm,
or code you intend to edit. Use line-range reads (`:offset` + `:limit`) once you know
the exact line from `find_definition`.

---

## Key locations (quick reference)

| What | File | Approx line |
|------|------|-------------|
| Main WPF entry | `src/FourHUnfolder.App/App.xaml.cs` | — |
| Load mesh | `MainViewModel.cs` | ~120 |
| Build 3-D model | `MainViewModel.cs` → `BuildWpfModel` | ~1 459 |
| Unfold pipeline | `UnfoldEngine.cs` | ~13 |
| PDO parser | `PdoMeshLoader.cs` | ~52 |
| SVG export | `SvgExporter.cs` | — |
| Settings model | `AppSettings.cs` | — |
| Project state | `ProjectState.cs` | — |

> Line numbers shift as code is edited — always confirm with `find_definition`.

---

## Tech debt & known issues (branch `feat/pdo-import`, as of v0.0.3.B)

### Fixed (session 30–35)
| ID | Fixed in | Description |
|----|----------|-------------|
| ~~CRITICAL-3D-TEX~~ | s32 | `EnterPreview`/`CommitPreview` now pass `_materialBitmaps` to `BuildWpfModel` |
| ~~TD-PDO-1~~ | s32 | PDO `coord` 2-D paper layout extracted → `PdoLayout` / auto-unfold on load |
| ~~TD-PDO-2~~ | s31 | Multi-texture PDO: per-face material via `unk11`; `RebuildMaterialSlots` uses embedded textures by index |
| ~~BUG-PDO-1~~ | s33 | `ModelOrientationDialog` skipped for PDO with layout → prevented UV double-flip |
| ~~BUG-PDO-2~~ | s33 | `texNote` now reflects embedded textures when no file-path texture is present |
| ~~TD-PDO-3~~ | s34 | Pre-geo seek: `Seek(120, Current)` → `Seek(154+localeLen+commentLen, Begin)` — absolute formula |
| ~~TD-PDO-4~~ | s34 | `BitmapFromEmbedded` split into cached wrapper + core; `_embeddedBitmapCache` cleared on mesh load |
| ~~TD-25-1~~ | s34 | `ModelOrientationDialog` — "Don't ask again" checkbox added; persisted to `AppSettings.General` |
| ~~BUG-PDO-3~~ | s35 | `RunAutoArrange` rot=90 formula: `localX - (-minY)` → `localX + minY + hNat`; PDO pieces no longer placed off-screen |

### Open
*(no open tech debt as of s35)*

---

## Conventions

- **No tool calls** before checking code-graph MCP first
- **Targeted reads**: use `offset` + `limit` once line number is known
- Tests live in `tests/FourHUnfolder.Tests/` — run after every change
- Settings persisted to `%AppData%\4H-Unfolder\settings.json`
- Project bundle format: `.4hu` = ZIP(mesh + textures + state JSON)

## Publish & Archive — WPF native DLL rule

WPF self-contained single-file apps do **NOT** bundle native DLLs into the exe.
The following files must exist in the **same directory** as the exe or the app will crash with
`DllNotFoundException` before showing any window:

```
wpfgfx_cor3.dll           ← WPF graphics backend (required)
PresentationNative_cor3.dll
D3DCompiler_47_cor3.dll
PenImc_cor3.dll
vcruntime140_cor3.dll
assimp.dll                ← AssimpNet native (required for multi-format load)
```

**Archive command (correct):**
```bash
cp publish/4H-Unfolder.exe publish/vX.X.X.Y/
cp publish/*.dll           publish/vX.X.X.Y/
```

Never copy only the exe — the archive folder will appear to "suspend" immediately when launched.

**Symptom of missing DLLs:** process appears in Task Manager → Background processes → Suspended,
no window ever shown. Event Log shows `DllNotFoundException` in `HwndSubclass.SubclassWndProc`.
