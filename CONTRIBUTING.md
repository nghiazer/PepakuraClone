# Contributing to 4H-Unfolder

Thank you for your interest in contributing! This document covers how to set up
the project, the branch / PR workflow, and coding conventions.

---

## Development setup

### Prerequisites

| Tool | Version | Download |
|------|---------|---------|
| .NET 8 SDK | 8.0.x | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Windows 10 / 11 | — | WPF requires Windows |
| Git | any recent | — |

Optional for building the installer:

| Tool | Purpose |
|------|---------|
| [Inno Setup 6](https://jrsoftware.org/isinfo.php) | Compile `installer/4H-Unfolder.iss` into a Windows setup EXE |

### Clone & build

```powershell
git clone https://github.com/nghiazer/4H-Unfolder.git
cd 4H-Unfolder
dotnet restore
dotnet build            # 0 errors, 4 NuGet NU1603 compat-warnings only
dotnet run --project src/FourHUnfolder.App
```

### Run tests

```powershell
dotnet test tests/FourHUnfolder.Tests   # 56 / 56 pass
```

---

## Branching & workflow

| Branch | Purpose |
|--------|---------|
| `main` | Always-releasable; protected — no direct pushes |
| `feat/<topic>` | New features or non-trivial refactors |
| `fix/<topic>` | Bug fixes |
| `docs/<topic>` | Documentation-only changes |
| `chore/<topic>` | Build scripts, CI, tooling |

**Flow:**

```
1. Fork the repo (or create a branch in the main repo if you have write access)
2. git checkout -b feat/my-feature
3. Make changes → commit early and often
4. dotnet build && dotnet test   ← must be clean
5. Push + open a Pull Request against main
```

---

## PR checklist

Before marking your PR as ready for review, verify:

- [ ] `dotnet build` — **0 errors**, warnings ≤ existing baseline
- [ ] `dotnet test` — **all 56 tests pass** (add new tests for new behaviour)
- [ ] `dotnet run --project src/FourHUnfolder.App` — app starts, no startup crash
- [ ] No new `#pragma warning disable` without a comment explaining why
- [ ] CLAUDE.md updated if you changed a key file / line reference
- [ ] Version not bumped unless coordinated with maintainer

---

## Coding conventions

| Convention | Detail |
|------------|--------|
| **Language** | C# 12, `Nullable enable`, `ImplicitUsings enable` |
| **Naming** | PascalCase for public API; `_camelCase` for private fields |
| **MVVM** | WPF UI via `CommunityToolkit.Mvvm` — `[ObservableProperty]` / `[RelayCommand]` |
| **No circular deps** | Layer order: `Domain → Geometry → Application → Infrastructure → App` |
| **Logging** | `Debug.WriteLine` only — no `Console.WriteLine` in production paths |
| **Undo/Redo** | All user-visible mutations must go through `_undoStack.Push(new UndoItem(…))` |
| **File I/O** | New loaders implement `IMeshLoader`; new exporters implement `IExporter` |

### Layer responsibility summary

| Layer | Rule |
|-------|------|
| `Domain` | Pure models, zero external dependencies, no WPF references |
| `Geometry` | Algorithms only — no file I/O, no UI |
| `Application` | Use-case orchestration — calls Geometry, calls Infrastructure via interfaces |
| `Infrastructure` | File parsing / writing — depends on Domain + Application interfaces only |
| `App` | WPF UI + ViewModels — the only layer that may reference WPF types |

---

## Reporting bugs

Please open a GitHub Issue and include:

1. **Version** (`Help → About` or title bar)
2. **Steps to reproduce** (attach the `.pdo` / `.obj` file if possible)
3. **Expected vs actual behaviour**
4. **Windows version** and whether you're using the pre-built EXE or building from source

---

## License

By contributing you agree that your contributions will be licensed under the
[MIT License](LICENSE) that covers this project.
