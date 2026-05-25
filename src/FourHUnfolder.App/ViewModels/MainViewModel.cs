using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using FourHUnfolder.App.Dialogs;
using FourHUnfolder.App.Services;
using FourHUnfolder.Application.Interfaces;
using FourHUnfolder.Application.Services;
using FourHUnfolder.Domain.Models;
using FourHUnfolder.Domain.Persistence;
using FourHUnfolder.Domain.Results;
using FourHUnfolder.Domain.Settings;
using FourHUnfolder.Infrastructure.Exporters;
// Alias to avoid ambiguity with FourHUnfolder.Application namespace
using WpfApp = System.Windows.Application;

namespace FourHUnfolder.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly MeshService       _meshService;
    private readonly UnfoldService     _unfoldService;
    private readonly IExporter         _exporter;
    private readonly PdfExporter       _pdfExporter;
    private readonly ProjectSerializer _serializer;
    private readonly SettingsService   _settingsService;
    private readonly ThemeService      _themeService;

    // ── core state ────────────────────────────────────────────────────────────
    private Mesh?   _currentMesh;
    private string? _currentMeshPath;
    private string? _committedTexturePath;
    private string? _tempBundleDir; // temp extract dir for .4hu bundles; deleted on next load or Dispose
    private string? _pendingTexturePath;
    private bool    _previewActive;
    private double ScaleMmPerUnit { get; set; } = 1.0;

    // dirty tracking — true when there are unsaved changes
    private bool _isDirty;
    public bool IsDirty => _isDirty;
    private void MarkDirty()  { _isDirty = true; }
    private void MarkClean()  { _isDirty = false; }

    // edge overrides: mesh edge ID → EdgeType (user's join/split operations)
    private readonly Dictionary<int, EdgeType> _edgeOverrides = new();

    // cached result from last unfold — used by SVG export to retrieve UV coords
    private UnfoldResult? _lastUnfoldResult;

    // suppress canvas CollectionChanged rebuilds during batch Pieces.Add() in RebuildPieces
    internal bool BatchingPieces;

    // O(1) face→piece lookup built in RebuildPieces (faceId → GroupId)
    private readonly Dictionary<int, int> _faceToGroup = new();

    // bumped after each RebuildPieces() to trigger a single canvas rebuild
    [ObservableProperty] private int _piecesVersion;

    // TD-5: selection overlay cache — avoid rebuilding geometry on every click
    private int?          _cachedOverlayGroupId;
    private Model3DGroup? _cachedOverlayModel;

    // Multi-material 3D hit-test map: geometry → list of global face IDs in draw order
    private readonly Dictionary<MeshGeometry3D, List<int>> _geoFaceIds = new();

    // TD-9: undo/redo — snapshots of (_edgeOverrides, piece layouts)
    private readonly record struct EditSnapshot(
        IReadOnlyDictionary<int, EdgeType> EdgeOverrides,
        IReadOnlyDictionary<int, (double X, double Y, double Rot)> PieceLayouts);

    private readonly Stack<EditSnapshot> _undoStack = new();
    private readonly Stack<EditSnapshot> _redoStack = new();

    // ── observable properties ─────────────────────────────────────────────────
    [ObservableProperty] private Model3DGroup? _meshModel;
    [ObservableProperty] private Model3DGroup? _selectionOverlayModel;  // 3D highlight
    [ObservableProperty] private Model3DGroup? _edgeHighlightModel;     // Feature B: 3D edge hover
    [ObservableProperty] private string        _statusText   = "Ready — load a mesh to begin.";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(UnfoldCommand))]  private bool _canUnfold;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportSvgCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenAssemblyAnimationCommand))]
    private bool _canExport;
    [ObservableProperty] private bool          _isUnfolded;
    [ObservableProperty] private int           _selectedFaceId = -1;

    // texture — single (legacy / default)
    [ObservableProperty] private ImageSource?  _activeTextureThumbnail;
    [ObservableProperty] private BitmapImage?  _canvas2DTexture;
    [ObservableProperty] private string        _textureStatusText = "No texture";
    [ObservableProperty] private string        _previewLabelText  = string.Empty;
    [ObservableProperty] private bool          _hasTexture;

    // multi-material texture slots (for TextureDialog)
    public ObservableCollection<MaterialTextureViewModel> MaterialTextureSlots { get; } = new();

    // cached loaded bitmaps per materialId (rebuilt when slots change)
    private readonly Dictionary<int, BitmapImage?> _materialBitmaps = new();

    // TD-PDO-4: cache decoded embedded bitmaps; keyed by EmbeddedTextures index.
    // Cleared when a new mesh is loaded to avoid holding stale bitmap data.
    private readonly Dictionary<int, BitmapImage?> _embeddedBitmapCache = new();

    // ── Feature B: expose mesh for edge hover in code-behind ─────────────────
    /// Read-only access to the current loaded mesh (used by MainWindow for screen-space edge hover).
    public Mesh? CurrentMesh => _currentMesh;

    /// Returns the bitmap for the given materialId (from multi-material slots), or
    /// falls back to Canvas2DTexture if no per-material texture is defined.
    public BitmapImage? GetCanvas2DTexture(int materialId)
    {
        if (materialId >= 0 && _materialBitmaps.TryGetValue(materialId, out var bmp))
            return bmp;
        return Canvas2DTexture;
    }

    /// Called by TextureDialog when user loads/removes a texture for a specific material slot.
    public void SetMaterialTexture(int materialId, string? path)
    {
        var slot = MaterialTextureSlots.FirstOrDefault(s => s.MaterialId == materialId);
        if (slot == null) return;
        slot.TexturePath = path;
        var bmp = LoadBitmapImage(path);
        slot.Thumbnail = bmp;
        _materialBitmaps[materialId] = bmp;
        MarkDirty();

        // Sync legacy single-texture path if this is the primary slot
        // Always pick a representative texture for the 3D view / single-texture fallback
        var primaryId  = GetPrimaryMaterialId();
        var primaryBmp = _materialBitmaps.GetValueOrDefault(primaryId)
                         ?? _materialBitmaps.Values.FirstOrDefault(b => b != null);
        _committedTexturePath = MaterialTextureSlots.FirstOrDefault(s => s.HasTexture)?.TexturePath;
        UpdateTextureUI(primaryBmp, _committedTexturePath, isPreview: false);
        OnPropertyChanged(nameof(Canvas2DTexture)); // Force 2D canvas to rebuild

        // Rebuild 3D model with updated per-material textures
        if (_currentMesh != null)
            MeshModel = BuildWpfModel(_currentMesh, primaryBmp, _materialBitmaps);
    }

    private int GetPrimaryMaterialId() =>
        MaterialTextureSlots.FirstOrDefault()?.MaterialId ?? -1;

    // paper canvas
    [ObservableProperty] private PaperSizeModel _paperSizeModel = PaperSizeModel.A4;

    /// Standard paper sizes shown in the toolbar ComboBox.
    public static IReadOnlyList<PaperSizeModel> AvailablePaperSizes { get; } =
        PaperSizeModel.Presets
            .SelectMany(p => new[] { p.Portrait(), p.Landscape() })
            .ToArray();
    [ObservableProperty] private double          _pixelsPerMm    = 3.0;
    [ObservableProperty] private int             _pagesWide      = 1;
    [ObservableProperty] private int             _pagesTall      = 1;

    /// Visual gap between adjacent pages on the canvas (mm).
    public const double PageSepMm = 20.0;

    public ObservableCollection<PieceViewModel> Pieces { get; } = new();

    // ── grid / snap (fast-path toggles, kept in sync with settings) ───────────
    [ObservableProperty] private bool _gridVisible = true;
    [ObservableProperty] private bool _snapToGrid  = false;

    // ── settings-derived 3D viewport bindings ─────────────────────────────────
    public Brush  Viewport3DBackground       => ParseBrush(S.View3D.BackgroundColor, "#0d0d1a");
    public bool   Show3DCoordinateSystem     => S.View3D.ShowCoordinateSystem;
    public bool   Show3DViewCube             => S.View3D.ShowViewCube;

    // Camera settings (read by MainWindow code-behind)
    public double CameraFOV        => S.View3D.CameraFOV;
    public double CameraNearPlane  => S.View3D.CameraNearPlane;
    public double CameraFarPlane   => S.View3D.CameraFarPlane;
    // Bumped by OnSettingsChanged so MainWindow knows to reapply camera
    public int    CameraSettingsVersion { get; private set; }

    // Expose current settings for PatternCanvasControl
    public AppSettings.View2DSettings View2DSettings => S.View2D;

    // ── unit display helpers ──────────────────────────────────────────────────
    private bool   IsInches   => S.General?.DisplayUnit == "inch";
    public  string UnitLabel  => IsInches ? "in" : "mm";
    public string FormatMm(double mm) =>
        IsInches ? $"{mm / 25.4:F3} {UnitLabel}" : $"{mm:F1} {UnitLabel}";

    // ── derived visibility ────────────────────────────────────────────────────
    public Visibility TexturePanelVisible   => _currentMesh != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PreviewBadgeVisible   => _previewActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoTexturePlaceholderVisible => ActiveTextureThumbnail == null ? Visibility.Visible : Visibility.Collapsed;

    public Brush ViewportBorderBrush =>
        _previewActive
            ? new SolidColorBrush(Colors.Orange)
            : new SolidColorBrush(Color.FromRgb(61, 61, 92));

    public bool CanRemoveTexture => HasTexture && !_previewActive;

    // ── column widths for split-view (show 2D panel only after unfolding) ─────
    public GridLength RightColumnWidth =>
        IsUnfolded ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    public GridLength SplitterColumnWidth =>
        IsUnfolded ? new GridLength(5) : new GridLength(0);
    public Visibility RightPanelVisible =>
        IsUnfolded ? Visibility.Visible : Visibility.Collapsed;

    // ── constructor ───────────────────────────────────────────────────────────
    public MainViewModel(MeshService meshService, UnfoldService unfoldService,
                         IExporter exporter, PdfExporter pdfExporter,
                         ProjectSerializer serializer, SettingsService settingsService,
                         ThemeService themeService)
    {
        _meshService     = meshService;
        _unfoldService   = unfoldService;
        _exporter        = exporter;
        _pdfExporter     = pdfExporter;
        _serializer      = serializer;
        _settingsService = settingsService;
        _themeService    = themeService;

        // Apply initial values from settings
        _pixelsPerMm   = settingsService.Current.View2D.DefaultPixelsPerMm;
        _gridVisible   = settingsService.Current.View2D.ShowGrid;
        _snapToGrid    = settingsService.Current.View2D.SnapToGrid;
        _lastThemeMode = settingsService.Current.General.ThemeMode;

        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    // ── settings shortcut ─────────────────────────────────────────────────────
    private AppSettings S => _settingsService.Current;

    // Hash of View3D fields that require a 3D model rebuild when changed
    private string _lastView3DHash = string.Empty;

    // Track theme to detect switches and auto-update theme-linked background colors
    private string _lastThemeMode = "Light";

    // Canonical default canvas backgrounds per theme (auto-swapped on theme change)
    private const string LightCanvasBg  = "#e8eaf0";
    private const string DarkCanvasBg   = "#3a3a5a";
    private const string LightView3DBg  = "#e8ecf4";
    private const string DarkView3DBg   = "#0d0d1a";

    private static string View3DHash(AppSettings.View3DSettings v) =>
        $"{v.FaceColor}|{v.BackFaceColor}|{v.FaceOpacity:F4}|{v.DisplayMode}|{v.EdgeOverlayColor}|{v.EdgeOverlayThickness:F4}";

    // ── settings changed handler ──────────────────────────────────────────────
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // Apply theme immediately when changed in settings
        _themeService.Apply(S.General.ThemeMode);

        // When theme changes, auto-update canvas/3D-viewport backgrounds
        // if they're still at the previous theme's default (respects user customisation).
        var newTheme = S.General.ThemeMode;
        if (!string.Equals(newTheme, _lastThemeMode, StringComparison.OrdinalIgnoreCase))
        {
            bool goingLight = newTheme.Equals("Light", StringComparison.OrdinalIgnoreCase);
            var oldCanvasBg = goingLight ? DarkCanvasBg : LightCanvasBg;
            var newCanvasBg = goingLight ? LightCanvasBg : DarkCanvasBg;

            if (string.Equals(S.View2D.CanvasBackground, oldCanvasBg, StringComparison.OrdinalIgnoreCase))
                PatchSettings(s => s.View2D.CanvasBackground = newCanvasBg);

            // Auto-switch 3D viewport background if still at previous theme's default
            var oldView3DBg = goingLight ? DarkView3DBg : LightView3DBg;
            var newView3DBg = goingLight ? LightView3DBg : DarkView3DBg;
            if (string.Equals(S.View3D.BackgroundColor, oldView3DBg, StringComparison.OrdinalIgnoreCase))
                PatchSettings(s => s.View3D.BackgroundColor = newView3DBg);

            _lastThemeMode = newTheme;
        }

        // Sync fast-path toggles from new settings
        GridVisible = S.View2D.ShowGrid;
        SnapToGrid  = S.View2D.SnapToGrid;

        // Update paper size if the default changed
        var matchedPaper = PaperSizeModel.Presets
            .FirstOrDefault(p => p.Name == S.View2D.DefaultPaperSizeName);
        if (matchedPaper != null) PaperSizeModel = matchedPaper;

        OnPropertyChanged(nameof(Viewport3DBackground));
        OnPropertyChanged(nameof(Show3DCoordinateSystem));
        OnPropertyChanged(nameof(Show3DViewCube));
        OnPropertyChanged(nameof(View2DSettings));
        OnPropertyChanged(nameof(UnitLabel));
        OnPropertyChanged(nameof(CameraFOV));
        OnPropertyChanged(nameof(CameraNearPlane));
        OnPropertyChanged(nameof(CameraFarPlane));
        CameraSettingsVersion++;
        OnPropertyChanged(nameof(CameraSettingsVersion));

        // Only rebuild expensive 3D model when View3D appearance settings actually changed
        var newHash = View3DHash(S.View3D);
        if (_currentMesh != null && newHash != _lastView3DHash)
        {
            _lastView3DHash = newHash;
            var tex = LoadBitmapImage(_committedTexturePath);
            MeshModel = BuildWpfModel(_currentMesh, tex, _materialBitmaps);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SETTINGS + GRID/SNAP COMMANDS
    // ══════════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void ToggleGrid()
    {
        GridVisible = !GridVisible;
        // Persist to settings without triggering full canvas rebuild —
        // PatternCanvasControl listens to GridVisible separately for a fast path.
        PatchSettings(s => s.View2D.ShowGrid = GridVisible);
    }

    [RelayCommand]
    private void ToggleSnap()
    {
        SnapToGrid = !SnapToGrid;
        PatchSettings(s => s.View2D.SnapToGrid = SnapToGrid);
    }

    // Applies a single settings mutation without going through SettingsService.Apply
    // (which would fire SettingsChanged and trigger a full 3D+2D rebuild).
    private void PatchSettings(Action<AppSettings> mutate)
    {
        mutate(_settingsService.Current);
        _settingsService.SaveCurrent();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var dlg = new SettingsDialog(_settingsService)
        {
            Owner = WpfApp.Current.MainWindow
        };
        dlg.ShowDialog();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  UNDO / REDO  (TD-9)
    // ══════════════════════════════════════════════════════════════════════════

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(TakeSnapshot());
        RestoreSnapshot(_undoStack.Pop());
        NotifyUndoRedo();
        StatusText = $"Undo — {_undoStack.Count} step(s) remaining.";
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(TakeSnapshot());
        RestoreSnapshot(_redoStack.Pop());
        NotifyUndoRedo();
        StatusText = $"Redo — {_redoStack.Count} step(s) remaining.";
    }

    /// Call before any operation that modifies edge overrides.
    private void PushUndoState()
    {
        _undoStack.Push(TakeSnapshot());
        _redoStack.Clear();
        NotifyUndoRedo();
    }

    private EditSnapshot TakeSnapshot() => new(
        new Dictionary<int, EdgeType>(_edgeOverrides),
        Pieces.ToDictionary(p => p.GroupId,
                            p => (p.PositionX, p.PositionY, p.Rotation)));

    private void RestoreSnapshot(EditSnapshot snap)
    {
        _edgeOverrides.Clear();
        foreach (var (id, t) in snap.EdgeOverrides) _edgeOverrides[id] = t;
        RerunUnfold(preservePositions: false);
        // Restore saved piece positions (best-effort: group IDs may differ after re-run)
        foreach (var piece in Pieces)
            if (snap.PieceLayouts.TryGetValue(piece.GroupId, out var pos))
            { piece.PositionX = pos.X; piece.PositionY = pos.Y; piece.Rotation = pos.Rot; }
    }

    private void NotifyUndoRedo()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  MESH COMMANDS
    // ══════════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void LoadMesh()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Open 3D Mesh",
            Filter = "All supported meshes (*.obj;*.pdo;*.3ds;*.stl;*.dxf;*.lwo;*.lws;*.fbx;*.dae;*.ply)|*.obj;*.pdo;*.3ds;*.stl;*.dxf;*.lwo;*.lws;*.fbx;*.dae;*.ply" +
                     "|Wavefront OBJ (*.obj)|*.obj" +
                     "|Pepakura Designer (*.pdo)|*.pdo" +
                     "|3D Studio (*.3ds)|*.3ds" +
                     "|STL (*.stl)|*.stl" +
                     "|AutoCAD DXF (*.dxf)|*.dxf" +
                     "|LightWave (*.lwo;*.lws)|*.lwo;*.lws" +
                     "|FBX (*.fbx)|*.fbx" +
                     "|COLLADA (*.dae)|*.dae" +
                     "|PLY (*.ply)|*.ply" +
                     "|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        if (!ConfirmDiscardIfDirty("load a new mesh")) return;

        try
        {
            CleanupTempBundle();
            StatusText = $"Loading {Path.GetFileName(dlg.FileName)} …";
            _currentMeshPath   = dlg.FileName;
            _currentMesh       = _meshService.LoadFromFile(dlg.FileName);
            _edgeOverrides.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            _embeddedBitmapCache.Clear(); // TD-PDO-4: stale bitmaps from previous mesh
            NotifyUndoRedo();
            InvalidateOverlayCache();

            CancelPreviewSilently();

            // Feature A: Model orientation dialog.
            // Skip for PDO files with a pre-computed 2-D layout: the layout coords are
            // paper-space (mm) and are independent of the 3-D transform, BUT FlipUV would
            // double-flip the UVs that the PDO loader already inverted (BUG-PDO-1).
            // TD-25-1: also skip when the user previously chose "Don't ask again".
            if (_currentMesh.PdoLayout == null &&
                !_settingsService.Current.General.SkipModelOrientationDialog)
            {
                var orientDlg = new Dialogs.ModelOrientationDialog(_currentMesh)
                {
                    Owner = WpfApp.Current.MainWindow
                };
                orientDlg.ShowDialog();

                // TD-25-1: persist "don't ask again" immediately so next load skips the dialog
                if (orientDlg.DontAskAgain)
                {
                    _settingsService.Current.General.SkipModelOrientationDialog = true;
                    _settingsService.SaveCurrent();
                }

                if (orientDlg.Applied)
                {
                    var rot = orientDlg.Result.ComputeRotation();
                    if (rot != System.Numerics.Matrix4x4.Identity)
                        _currentMesh.ApplyTransform(rot);
                    if (orientDlg.Result.FlipUV)
                        _currentMesh.FlipUVsVertical();
                }
            }

            _committedTexturePath = _currentMesh.SuggestedTexturePath;
            RebuildMaterialSlots(_currentMesh);

            var tex    = LoadBitmapImage(_committedTexturePath);
            _lastView3DHash = View3DHash(S.View3D);   // sync hash so OnSettingsChanged skips rebuild
            MeshModel  = BuildWpfModel(_currentMesh, tex, _materialBitmaps);
            CanUnfold  = true;
            CanExport  = false;
            IsUnfolded = false;
            Pieces.Clear();

            UpdateTextureUI(tex, _committedTexturePath, isPreview: false);
            RefreshDerivedVisibility();

            // BUG-PDO-2: embedded-texture PDOs have no file path, so check both sources
            var texNote = _committedTexturePath != null
                ? " · texture from MTL"
                : _currentMesh.EmbeddedTextures.Count > 0
                    ? $" · {_currentMesh.EmbeddedTextures.Count} embedded texture(s)"
                    : string.Empty;

            // ── Auto-unfold PDO files that carry a pre-computed 2-D layout ────
            if (_currentMesh.PdoLayout != null)
            {
                try
                {
                    StatusText = "Restoring PDO layout …";
                    var pdoResult = _unfoldService.TryBuildFromPdoLayout(
                        _currentMesh, _settingsService.Current.Print);

                    if (pdoResult != null)
                    {
                        ScaleMmPerUnit = 1.0;  // PDO coords are already in mm
                        var pieces = _unfoldService.ComputePieces(_currentMesh);
                        RebuildPieces(pdoResult, pieces, 1.0);
                        RunAutoArrange();

                        CanExport  = true;
                        IsUnfolded = true;
                        RefreshColumnBindings();

                        var parts   = _currentMesh.PdoLayout.PartIndices.Count;
                        StatusText = $"Loaded (PDO) — {_currentMesh.Faces.Count:N0} faces, " +
                                     $"{parts} piece(s){texNote}.";
                        MarkClean();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Non-fatal: fall through to normal status text
                    StatusText = $"PDO layout restore failed: {ex.Message}";
                }
            }

            StatusText = $"Loaded — {_currentMesh.Faces.Count:N0} faces, " +
                         $"{_currentMesh.Vertices.Count:N0} vertices{texNote}.";
            MarkClean(); // fresh mesh = clean state
        }
        catch (Exception ex) { Error("Load failed", ex); }
    }

    // ── UNFOLD (shows setup dialog first) ─────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanUnfold))]
    private void Unfold()
    {
        if (_currentMesh == null) return;

        var dlg = new UnfoldSetupDialog(UnfoldService.BoundingBoxInfo(_currentMesh))
        {
            Owner = WpfApp.Current.MainWindow
        };
        if (dlg.ShowDialog() != true) return;

        var setup = dlg.Result!;
        PaperSizeModel = setup.Paper;
        OnPropertyChanged(nameof(RightPanelVisible));

        try
        {
            StatusText = "Unfolding …";

            double scale        = UnfoldService.ComputeScale(_currentMesh, setup.Scale);
            ScaleMmPerUnit = scale;
            var    unfoldResult = _unfoldService.Unfold(_currentMesh, _edgeOverrides, _settingsService.Current.Print);
            var    pieces       = _unfoldService.ComputePieces(_currentMesh);

            RebuildPieces(unfoldResult, pieces, scale);
            RunAutoArrange();   // place pieces without overlap, set PagesWide/PagesTall

            CanExport  = true;
            IsUnfolded = true;
            RefreshColumnBindings();

            var overlap = unfoldResult.HasOverlaps ? "  ⚠ overlaps" : string.Empty;
            StatusText = $"Unfolded — {Pieces.Count} pieces on {PagesWide * PagesTall} page(s), " +
                         $"{unfoldResult.GlueTabs.Count} glue tabs.{overlap}";
        }
        catch (Exception ex) { Error("Unfold failed", ex); }
    }

    // ── ASSEMBLY ANIMATION ────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanExport))]
    private void OpenAssemblyAnimation()
    {
        if (_currentMesh == null || _lastUnfoldResult == null) return;

        var vm  = new AssemblyViewModel(_currentMesh, _lastUnfoldResult, Pieces, ScaleMmPerUnit, _materialBitmaps);
        var win = new Dialogs.AssemblyAnimationWindow(vm)
        {
            Owner = WpfApp.Current.MainWindow
        };
        win.Show();
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportSvg()
    {
        if (_currentMesh == null) return;

        var dlg = new SaveFileDialog
        {
            Title      = "Export SVG Pattern",
            Filter     = "SVG files (*.svg)|*.svg",
            DefaultExt = "svg",
            FileName   = "unfolded_pattern"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            // Use current canvas layout (positions/rotations applied) instead of re-running unfold
            var result   = BuildExportLayout();
            // TD-22-3: pass per-material texture paths for multi-texture SVG export
            var matPaths = GetMaterialTexturePaths();
            _exporter.Export(result, dlg.FileName, _committedTexturePath, matPaths);
            StatusText = $"Exported to {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { Error("Export SVG failed", ex); }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportPdf()
    {
        if (_currentMesh == null) return;
        var dlg = new SaveFileDialog
        {
            Title      = "Export PDF Pattern",
            Filter     = "PDF files (*.pdf)|*.pdf",
            DefaultExt = "pdf",
            FileName   = "unfolded_pattern"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var result = BuildExportLayout();
            _pdfExporter.Export(result, dlg.FileName,
                PaperSizeModel.WidthMm, PaperSizeModel.HeightMm,
                PagesWide, PagesTall, PageSepMm);
            StatusText = $"PDF exported to {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { Error("Export PDF failed", ex); }
    }

    // ── SAVE / LOAD PROJECT ───────────────────────────────────────────────────
    [RelayCommand]
    private void SaveProject()
    {
        if (_currentMesh == null || string.IsNullOrEmpty(_currentMeshPath))
        { StatusText = "Nothing to save."; return; }

        var dlg = new SaveFileDialog
        {
            Title      = "Save Project",
            Filter     = "4H-Unfolder bundle (*.4hu)|*.4hu",
            DefaultExt = "4hu",
            FileName   = Path.GetFileNameWithoutExtension(_currentMeshPath)
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var state    = BuildProjectState();
            // TD-22-2: persist per-material textures in the bundle
            var matPaths = GetMaterialTexturePaths();
            _serializer.SaveBundle(state, _currentMeshPath!, _committedTexturePath, dlg.FileName, matPaths);
            StatusText = $"Project saved: {Path.GetFileName(dlg.FileName)}";
            MarkClean();
        }
        catch (Exception ex) { Error("Save failed", ex); }
    }

    [RelayCommand]
    private void LoadProject()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Open Project",
            Filter = "4H-Unfolder bundle (*.4hu)|*.4hu|Legacy project (*.pmc)|*.pmc|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        if (!ConfirmDiscardIfDirty("open a project")) return;

        try
        {
            CleanupTempBundle();
            ProjectState state;
            if (dlg.FileName.EndsWith(".4hu", StringComparison.OrdinalIgnoreCase))
                state = _serializer.LoadBundle(dlg.FileName, out _tempBundleDir);
            else
                state = _serializer.Load(dlg.FileName);
            RestoreProjectState(state);
        }
        catch (Exception ex) { Error("Load project failed", ex); }
    }

    /// Returns true if safe to proceed (no unsaved changes, or user confirmed discard).
    public bool ConfirmDiscardIfDirty(string action = "proceed")
    {
        if (!_isDirty) return true;
        var result = MessageBox.Show(
            $"You have unsaved changes. Discard them and {action}?",
            "Unsaved Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private void CleanupTempBundle()
    {
        if (_tempBundleDir == null) return;
        try { Directory.Delete(_tempBundleDir, recursive: true); } catch { /* best-effort */ }
        _tempBundleDir = null;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  TEXTURE COMMANDS
    // ══════════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void LoadTexture()
    {
        if (_currentMesh == null) return;
        var dlg = new OpenFileDialog
        {
            Title  = "Load Texture",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.tiff|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        EnterPreview(dlg.FileName);
    }

    [RelayCommand] private void RemoveTexture()  { if (_currentMesh != null) EnterPreview(null); }
    [RelayCommand] private void ApplyPreview()   { if (!_previewActive) return; _committedTexturePath = _pendingTexturePath; CommitPreview(); MarkDirty(); }
    [RelayCommand] private void CancelPreview()  { if (!_previewActive) return; CommitPreview(revert: true); }

    // ══════════════════════════════════════════════════════════════════════════
    //  EDGE TOGGLE (join / split)  — called from PatternCanvasControl
    // ══════════════════════════════════════════════════════════════════════════

    public bool IsEdgeFold(int meshEdgeId)
    {
        if (_currentMesh == null || meshEdgeId >= _currentMesh.Edges.Count) return false;
        return _currentMesh.Edges[meshEdgeId].Type == EdgeType.Fold;
    }

    public void ToggleEdge(int meshEdgeId)
    {
        if (_currentMesh == null) return;
        PushUndoState();
        var current = IsEdgeFold(meshEdgeId) ? EdgeType.Fold : EdgeType.Cut;
        _edgeOverrides[meshEdgeId] = current == EdgeType.Fold ? EdgeType.Cut : EdgeType.Fold;
        MarkDirty();
        try
        {
            RerunUnfold();
            StatusText = IsEdgeFold(meshEdgeId)
                ? $"Joined — edge {meshEdgeId} is now a fold."
                : $"Split   — edge {meshEdgeId} is now a cut.";
        }
        catch (Exception ex) { Error("Edge toggle failed", ex); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  3-D SELECTION AND DETACH / ATTACH
    // ══════════════════════════════════════════════════════════════════════════

    /// Called from MainWindow code-behind on left-click in the 3D viewport.
    public void SelectFace3D(int faceId)
    {
        if (_currentMesh == null || faceId < 0 || faceId >= _currentMesh.Faces.Count) return;
        SelectedFaceId = faceId;

        // O(1) lookup via pre-built dict; fall back to linear scan if dict is stale
        PieceViewModel? piece = null;
        if (_faceToGroup.TryGetValue(faceId, out var gid))
            piece = Pieces.FirstOrDefault(p => p.GroupId == gid);
        piece ??= Pieces.FirstOrDefault(p => p.Faces.Any(f => f.FaceId == faceId));

        foreach (var p in Pieces) p.IsSelected = (p == piece);
        BuildSelectionOverlay(piece);

        StatusText = piece != null
            ? $"Selected face {faceId}  ·  piece #{piece.GroupId}  ·  {piece.Faces.Length} triangles"
            : $"Selected face {faceId}  (no 2D piece — unfold first)";
    }

    /// Called when empty space is clicked (deselect all).
    public void ClearSelection()
    {
        foreach (var p in Pieces) p.IsSelected = false;
        SelectionOverlayModel = null;
        _cachedOverlayGroupId = null;
        _cachedOverlayModel   = null;
        SelectedFaceId        = -1;
    }

    /// Called from 2D canvas when a piece is clicked.
    public void SelectPiece2D(int groupId)
    {
        var piece = Pieces.FirstOrDefault(p => p.GroupId == groupId);
        if (piece == null) return;
        SelectedFaceId = piece.Faces.FirstOrDefault()?.FaceId ?? -1;
        BuildSelectionOverlay(piece);
        StatusText = $"Selected piece #{groupId}  ·  {piece.Faces.Length} triangles";
    }

    /// Cuts all fold edges connecting faceId to its piece → face becomes its own piece.
    public void DetachFace(int faceId)
    {
        if (_currentMesh == null || faceId < 0) return;
        var face = _currentMesh.Faces[faceId];
        bool changed = false;
        foreach (var eid in face.EdgeIds)
        {
            var e = _currentMesh.Edges[eid];
            if (e.Type == EdgeType.Fold && e.ConnectsFaces)
            { changed = true; break; }
        }
        if (!changed) { StatusText = "Face is already detached."; return; }
        PushUndoState();
        foreach (var eid in face.EdgeIds)
        {
            var e = _currentMesh.Edges[eid];
            if (e.Type == EdgeType.Fold && e.ConnectsFaces) _edgeOverrides[eid] = EdgeType.Cut;
        }
        try { RerunUnfold(); StatusText = $"Detached face {faceId}."; }
        catch (Exception ex) { Error("Detach failed", ex); }
    }

    /// Cuts all edges of every face in the same piece as faceId.
    public void DetachPiece(int faceId)
    {
        if (_currentMesh == null) return;
        var piece = Pieces.FirstOrDefault(p => p.Faces.Any(f => f.FaceId == faceId));
        if (piece == null) { StatusText = "No piece found for that face."; return; }

        PushUndoState();
        foreach (var fd in piece.Faces)
        {
            var mf = _currentMesh.Faces[fd.FaceId];
            foreach (var eid in mf.EdgeIds)
            {
                var e = _currentMesh.Edges[eid];
                if (e.Type == EdgeType.Fold && e.ConnectsFaces)
                    _edgeOverrides[eid] = EdgeType.Cut;
            }
        }
        try { RerunUnfold(); StatusText = $"Detached piece #{piece.GroupId}."; }
        catch (Exception ex) { Error("Detach piece failed", ex); }
    }

    /// Joins faceA and faceB via their shared edge (Cut → Fold).
    public void AttachFaces(int faceIdA, int faceIdB)
    {
        if (_currentMesh == null) return;
        var faceA = _currentMesh.Faces[faceIdA];
        foreach (var eid in faceA.EdgeIds)
        {
            var e = _currentMesh.Edges[eid];
            if (e.ConnectsFaces && (e.FaceA == faceIdB || e.FaceB == faceIdB))
            {
                PushUndoState();
                _edgeOverrides[eid] = EdgeType.Fold;
                try { RerunUnfold(); StatusText = $"Attached face {faceIdA} → {faceIdB}."; }
                catch (Exception ex) { Error("Attach failed", ex); }
                return;
            }
        }
        StatusText = $"Faces {faceIdA} and {faceIdB} do not share an edge.";
    }

    /// Returns face IDs adjacent to faceId via CUT edges (attachable neighbors).
    public IEnumerable<int> GetCutNeighbors(int faceId)
    {
        if (_currentMesh == null || faceId < 0) yield break;
        foreach (var eid in _currentMesh.Faces[faceId].EdgeIds)
        {
            var e = _currentMesh.Edges[eid];
            if (e.ConnectsFaces && e.Type == EdgeType.Cut)
                yield return e.FaceA == faceId ? e.FaceB : e.FaceA;
        }
    }

    // ── Feature B: 3D edge hover highlight ────────────────────────────────────

    /// <summary>
    /// Highlights the specified edge as a thin cylinder in the 3D viewport.
    /// </summary>
    /// <param name="edgeId">Mesh edge index.</param>
    /// <param name="isDetach">True = fold edge (show detach colour); False = cut edge (attach colour).</param>
    public void HoverEdge(int edgeId, bool isDetach)
    {
        if (_currentMesh == null || edgeId < 0 || edgeId >= _currentMesh.Edges.Count)
        {
            ClearEdgeHover();
            return;
        }

        var edge  = _currentMesh.Edges[edgeId];
        var p1    = _currentMesh.Vertices[edge.V1].Position;
        var p2    = _currentMesh.Vertices[edge.V2].Position;

        var geo   = BuildThinCylinder(
            new Point3D(p1.X, p1.Y, p1.Z),
            new Point3D(p2.X, p2.Y, p2.Z),
            radius: 2.0 / ScaleMmPerUnit);   // 2 mm physical radius; formula is mm/scale not scale*const

        var colorHex = isDetach ? S.View3D.EdgeHoverDetachColor : S.View3D.EdgeHoverAttachColor;
        var fallback = isDetach ? "#ff3333" : "#33cc33";
        var mat   = new DiffuseMaterial(ParseBrush(colorHex, fallback));
        var model = new GeometryModel3D(geo, mat) { BackMaterial = mat };
        var group = new Model3DGroup();
        group.Children.Add(model);
        EdgeHighlightModel = group;
    }

    /// <summary>Removes the 3D edge highlight.</summary>
    public void ClearEdgeHover() => EdgeHighlightModel = null;

    /// Builds a hexagonal prism between two 3D points (thin cylinder approximation).
    private static MeshGeometry3D BuildThinCylinder(Point3D start, Point3D end, double radius)
    {
        var dir = end - start;
        double len = dir.Length;
        if (len < 1e-10) return new MeshGeometry3D();
        dir.Normalize();

        // Perpendicular vectors (Gram-Schmidt)
        var up    = Math.Abs(dir.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0);
        var perp1 = Vector3D.CrossProduct(dir, up); perp1.Normalize();
        var perp2 = Vector3D.CrossProduct(dir, perp1);

        const int sides = 6;
        var positions = new Point3DCollection(sides * 2);
        var indices   = new Int32Collection(sides * 6);

        for (int i = 0; i < sides; i++)
        {
            double ang    = i * 2 * Math.PI / sides;
            var    offset = (perp1 * Math.Cos(ang) + perp2 * Math.Sin(ang)) * radius;
            positions.Add(start + offset);
            positions.Add(end   + offset);
        }

        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            int b0 = i * 2, b1 = next * 2, t0 = i * 2 + 1, t1 = next * 2 + 1;
            indices.Add(b0); indices.Add(t0); indices.Add(b1);
            indices.Add(b1); indices.Add(t0); indices.Add(t1);
        }

        return new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
    }

    // ── selection overlay (TD-5: cached per piece group) ────────────────────────

    private void BuildSelectionOverlay(PieceViewModel? piece)
    {
        if (piece == null || _currentMesh == null)
        {
            SelectionOverlayModel = null;
            _cachedOverlayGroupId = null;
            _cachedOverlayModel   = null;
            return;
        }

        // Return cached model if same piece is re-selected
        if (_cachedOverlayGroupId == piece.GroupId && _cachedOverlayModel != null)
        {
            SelectionOverlayModel = _cachedOverlayModel;
            return;
        }

        var faceSet   = new HashSet<int>(piece.Faces.Select(f => f.FaceId));
        var positions = new Point3DCollection(faceSet.Count * 3);
        var indices   = new Int32Collection(faceSet.Count * 3);

        foreach (var mf in _currentMesh.Faces.Where(f => faceSet.Contains(f.Id)))
        {
            int baseIdx = positions.Count;
            var va = _currentMesh.Vertices[mf.A].Position;
            var vb = _currentMesh.Vertices[mf.B].Position;
            var vc = _currentMesh.Vertices[mf.C].Position;

            // Offset along face normal to prevent z-fighting
            var ab  = new System.Numerics.Vector3(vb.X - va.X, vb.Y - va.Y, vb.Z - va.Z);
            var ac  = new System.Numerics.Vector3(vc.X - va.X, vc.Y - va.Y, vc.Z - va.Z);
            var cross = System.Numerics.Vector3.Cross(ab, ac);
            float len = cross.Length();
            var n = (len > 1e-10f ? cross / len : System.Numerics.Vector3.UnitY) * 0.015f;

            positions.Add(new Point3D(va.X + n.X, va.Y + n.Y, va.Z + n.Z));
            positions.Add(new Point3D(vb.X + n.X, vb.Y + n.Y, vb.Z + n.Z));
            positions.Add(new Point3D(vc.X + n.X, vc.Y + n.Y, vc.Z + n.Z));
            indices.Add(baseIdx); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
        }

        var geo   = new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
        var mat   = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(160, 255, 210, 30)));
        var model = new GeometryModel3D(geo, mat);
        var group = new Model3DGroup();
        group.Children.Add(model);
        group.Freeze();

        _cachedOverlayGroupId = piece.GroupId;
        _cachedOverlayModel   = group;
        SelectionOverlayModel = group;
    }

    private void InvalidateOverlayCache()
    {
        _cachedOverlayGroupId = null;
        _cachedOverlayModel   = null;
        SelectionOverlayModel = null;
        SelectedFaceId        = -1;
        ClearEdgeHover();   // also clear 3D edge hover (mesh changed)
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private void RerunUnfold(bool preservePositions = true)
    {
        if (_currentMesh == null) return;
        var oldPos = Pieces.ToDictionary(p => p.GroupId,
                                         p => (p.PositionX, p.PositionY, p.Rotation));
        var result = _unfoldService.Unfold(_currentMesh, _edgeOverrides, _settingsService.Current.Print);
        var groups = _unfoldService.ComputePieces(_currentMesh);
        RebuildPieces(result, groups, ScaleMmPerUnit);

        if (preservePositions)
        {
            // Restore positions for pieces whose GroupId survived the re-run
            int newPieceIdx = 0;
            foreach (var p in Pieces)
            {
                if (oldPos.TryGetValue(p.GroupId, out var pos))
                {
                    p.PositionX = pos.PositionX;
                    p.PositionY = pos.PositionY;
                    p.Rotation  = pos.Rotation;
                }
                else
                {
                    // TD-1: new piece spawned by a join/split — place it to the right of
                    // the paper boundary so it's visible but not stacked on existing pieces.
                    // Users can drag it to the desired position or use Auto-arrange.
                    double paperRight = PaperSizeModel.WidthMm + 15;
                    p.PositionX = paperRight + (newPieceIdx % 4) * 30;
                    p.PositionY = 15        + (newPieceIdx / 4) * 30;
                    newPieceIdx++;
                }
            }
        }

        // Invalidate overlay cache — mesh topology changed
        InvalidateOverlayCache();
    }

    private void RebuildPieces(UnfoldResult result, List<List<int>> groups, double scale)
    {
        _lastUnfoldResult = result;   // cache for BuildExportLayout

        // Suppress per-Add canvas rebuilds; fire one rebuild via PiecesVersion at the end
        BatchingPieces = true;
        Pieces.Clear();
        for (int g = 0; g < groups.Count; g++)
        {
            var groupId = groups[g].Min();
            var vm      = PieceViewModel.Create(groupId, groups[g], result, _currentMesh!, scale);
            Pieces.Add(vm);
        }
        BatchingPieces = false;
        PiecesVersion++;   // triggers exactly one RebuildAll in the canvas

        // Rebuild O(1) face→group lookup
        _faceToGroup.Clear();
        foreach (var p in Pieces)
            foreach (var fd in p.Faces)
                _faceToGroup[fd.FaceId] = p.GroupId;
    }

    // ── SVG export layout builder ─────────────────────────────────────────────

    /// Builds an UnfoldResult whose face/tab coordinates are in canvas-absolute mm,
    /// reflecting the piece positions and rotations the user has set on the 2D canvas.
    private UnfoldResult BuildExportLayout()
    {
        // Fast UV lookup: FaceId → UV coords from the last unfold
        var uvByFaceId = _lastUnfoldResult?.Faces
            .Where(f => f.UVCoords != null)
            .ToDictionary(f => f.FaceId, f => f.UVCoords!) ?? new();

        var faces = new List<UnfoldedFace>();
        var tabs  = new List<GlueTab>();

        foreach (var piece in Pieces)
        {
            double posX   = piece.PositionX;
            double posY   = piece.PositionY;
            double rotRad = piece.Rotation * Math.PI / 180.0;
            double cosR   = Math.Cos(rotRad);
            double sinR   = Math.Sin(rotRad);

            // Rotate local (mm) → translate to canvas (mm)
            Vector2 ToCanvas(double lx, double ly) => new(
                (float)(lx * cosR - ly * sinR + posX),
                (float)(lx * sinR + ly * cosR + posY));

            foreach (var fd in piece.Faces)
            {
                uvByFaceId.TryGetValue(fd.FaceId, out var uvCoords);
                faces.Add(new UnfoldedFace(
                    fd.FaceId,
                    ToCanvas(fd.V0.X, fd.V0.Y),
                    ToCanvas(fd.V1.X, fd.V1.Y),
                    ToCanvas(fd.V2.X, fd.V2.Y),
                    fd.EdgeIsFold, fd.EdgeIsBoundary, uvCoords,
                    fd.MaterialId));   // TD-22-3: propagate material ID to export
            }

            foreach (var tab in piece.GlueTabs)
            {
                var pts = tab.Points;
                tabs.Add(new GlueTab(tab.FaceId, tab.LocalEdgeIdx,
                    ToCanvas(pts[0].X, pts[0].Y),
                    ToCanvas(pts[1].X, pts[1].Y),
                    ToCanvas(pts[2].X, pts[2].Y),
                    ToCanvas(pts[3].X, pts[3].Y)));
            }
        }

        return new UnfoldResult(faces, tabs, false);
    }

    // ── auto-arrange ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void AutoArrange() => RunAutoArrange();

    /// Strip-packs pieces into a horizontal page grid.
    /// Improvements over the naive approach:
    ///   1. Sort pieces by bounding-box area descending (First Fit Decreasing)
    ///   2. For each piece try both 0° and 90° rotation; keep the one that wastes less width
    ///   3. Pages grow to the right (PagesWide) when vertical space runs out
    private void RunAutoArrange()
    {
        if (Pieces.Count == 0) return;

        double gap    = _settingsService.Current.View2D.PieceGapMm;
        double paperW = PaperSizeModel.WidthMm;
        double paperH = PaperSizeModel.HeightMm;
        double usableW = paperW - 2 * gap;
        double usableH = paperH - 2 * gap;

        // Build (piece, bbox) list sorted by area descending
        var items = Pieces
            .Where(p => p.Faces.Length > 0)
            .Select(p =>
            {
                var allX = p.Faces.SelectMany(f => new[] { f.V0.X, f.V1.X, f.V2.X });
                var allY = p.Faces.SelectMany(f => new[] { f.V0.Y, f.V1.Y, f.V2.Y });
                double minX = allX.Min(), maxX = allX.Max();
                double minY = allY.Min(), maxY = allY.Max();
                double w = maxX - minX, h = maxY - minY;
                return (piece: p, minX, minY, w, h);
            })
            .OrderByDescending(x => x.w * x.h) // area descending
            .ToList();

        double localX = gap, localY = gap, rowH = 0;
        int pageCol = 0, newPagesWide = 1;

        foreach (var (piece, minX, minY, wNat, hNat) in items)
        {
            // Try 90° rotation if it produces a narrower footprint that fits the current row
            double pw = wNat, ph = hNat;
            double rot = 0;
            if (hNat < wNat && hNat <= usableW) // rotating would make it narrower
            {
                pw  = hNat;
                ph  = wNat;
                rot = 90;
            }

            // Ensure single piece fits on a page
            pw = Math.Min(pw, usableW);
            ph = Math.Min(ph, usableH);

            // Wrap row within current page
            if (localX > gap && localX + pw > paperW - gap)
            {
                localX  = gap;
                localY += rowH + gap;
                rowH    = 0;
            }

            // Advance to next page column when vertical space exhausted
            if (localY + ph > paperH - gap)
            {
                pageCol++;
                newPagesWide = Math.Max(newPagesWide, pageCol + 1);
                localX = gap;
                localY = gap;
                rowH   = 0;
            }

            // Set piece position and rotation
            piece.Rotation  = rot;
            if (rot == 0)
            {
                piece.PositionX = pageCol * (paperW + PageSepMm) + localX - minX;
                piece.PositionY = localY - minY;
            }
            else
            {
                // After 90° rotation: new bbox origin shifts
                piece.PositionX = pageCol * (paperW + PageSepMm) + localX - (-minY);
                piece.PositionY = localY - minX;
            }

            localX += pw + gap;
            rowH    = Math.Max(rowH, ph);
        }

        PagesWide = newPagesWide;
        PagesTall = 1;
    }

    /// Expands the page grid if a piece's bounding box extends beyond the current pages.
    /// <param name="rightMm">Rightmost mm coordinate of the piece's bounding box.</param>
    /// <param name="bottomMm">Bottommost mm coordinate of the piece's bounding box.</param>
    /// Pushes an undo snapshot that captures the pre-drag positions.
    /// Called from the canvas after a drag completes with actual movement.
    public void PushDragUndo(IReadOnlyDictionary<int, (double X, double Y, double Rot)> preDrag)
    {
        _undoStack.Push(new EditSnapshot(
            new Dictionary<int, EdgeType>(_edgeOverrides),
            preDrag));
        _redoStack.Clear();
        NotifyUndoRedo();
        MarkDirty();
    }

    public void EnsurePageForPosition(double rightMm, double bottomMm)
    {
        double paperW     = PaperSizeModel.WidthMm;
        double paperH     = PaperSizeModel.HeightMm;
        double rightEdge  = PagesWide * paperW + (PagesWide - 1) * PageSepMm;
        double bottomEdge = PagesTall * paperH + (PagesTall - 1) * PageSepMm;

        if (rightMm  > rightEdge)  PagesWide++;
        if (bottomMm > bottomEdge) PagesTall++;
    }

    private ProjectState BuildProjectState()
    {
        var state = new ProjectState
        {
            MeshPath       = _currentMeshPath,
            TexturePath    = _committedTexturePath,
            ScaleMmPerUnit = ScaleMmPerUnit,
            PagesWide      = PagesWide,
            PagesTall      = PagesTall,
            Paper          = new ProjectState.PaperDto
            {
                Name     = PaperSizeModel.Name,
                WidthMm  = PaperSizeModel.WidthMm,
                HeightMm = PaperSizeModel.HeightMm
            }
        };

        foreach (var (id, type) in _edgeOverrides)
            state.EdgeOverrides[id] = type.ToString();

        foreach (var piece in Pieces)
            state.Layouts.Add(new ProjectState.PieceLayoutDto
            {
                GroupId   = piece.GroupId,
                PositionX = piece.PositionX,
                PositionY = piece.PositionY,
                Rotation  = piece.Rotation
            });

        // TD-22-2: persist per-material texture paths (for .pmc non-bundle format)
        foreach (var (matId, path) in GetMaterialTexturePaths())
            state.MaterialTexturePaths[matId] = path;

        return state;
    }

    private void RestoreProjectState(ProjectState state)
    {
        if (string.IsNullOrEmpty(state.MeshPath) || !File.Exists(state.MeshPath))
        {
            MessageBox.Show($"Mesh file not found:\n{state.MeshPath}",
                            "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusText   = "Restoring project …";
        _currentMesh = _meshService.LoadFromFile(state.MeshPath);
        _currentMeshPath = state.MeshPath;
        _edgeOverrides.Clear();
        RebuildMaterialSlots(_currentMesh);

        // Restore edge overrides
        foreach (var (id, typeName) in state.EdgeOverrides)
            if (Enum.TryParse<EdgeType>(typeName, out var t))
                _edgeOverrides[id] = t;

        // Restore paper
        PaperSizeModel = new PaperSizeModel(state.Paper.Name, state.Paper.WidthMm, state.Paper.HeightMm);

        // Re-run unfold
        var unfoldResult = _unfoldService.Unfold(_currentMesh, _edgeOverrides, _settingsService.Current.Print);
        var pieces       = _unfoldService.ComputePieces(_currentMesh);
        var layoutMap    = state.Layouts.ToDictionary(l => l.GroupId);

        ScaleMmPerUnit = state.ScaleMmPerUnit > 0 ? state.ScaleMmPerUnit : 1.0;
        RebuildPieces(unfoldResult, pieces, ScaleMmPerUnit);

        // Restore piece positions
        foreach (var piece in Pieces)
        {
            if (layoutMap.TryGetValue(piece.GroupId, out var layout))
            {
                piece.PositionX = layout.PositionX;
                piece.PositionY = layout.PositionY;
                piece.Rotation  = layout.Rotation;
            }
        }

        // Restore page layout
        PagesWide = Math.Max(1, state.PagesWide);
        PagesTall = Math.Max(1, state.PagesTall);

        // TD-22-2: restore per-material texture slots before building 3D model
        if (state.MaterialTexturePaths.Count > 0)
        {
            foreach (var (matId, path) in state.MaterialTexturePaths)
            {
                var slot = MaterialTextureSlots.FirstOrDefault(s => s.MaterialId == matId);
                if (slot != null)
                {
                    slot.TexturePath = path;
                    slot.Thumbnail   = LoadBitmapImage(path);
                    _materialBitmaps[matId] = slot.Thumbnail;
                }
            }
        }

        // Restore texture
        _committedTexturePath = state.TexturePath
            ?? MaterialTextureSlots.FirstOrDefault(s => s.HasTexture)?.TexturePath;
        var tex = LoadBitmapImage(_committedTexturePath);
        MeshModel  = BuildWpfModel(_currentMesh, tex, _materialBitmaps);
        CanUnfold  = true;
        CanExport  = true;
        IsUnfolded = true;

        UpdateTextureUI(tex, _committedTexturePath, isPreview: false);
        RefreshDerivedVisibility();
        RefreshColumnBindings();

        if (state.Warnings.Count > 0)
            StatusText = $"Project loaded with warnings: {string.Join("; ", state.Warnings)}";
        else
            StatusText = $"Project loaded — {Pieces.Count} pieces.";
        MarkClean(); // just loaded = clean
    }

    // ── texture helpers ───────────────────────────────────────────────────────

    private void EnterPreview(string? previewPath)
    {
        _pendingTexturePath = previewPath;
        _previewActive      = true;
        var tex = LoadBitmapImage(previewPath);
        MeshModel = BuildWpfModel(_currentMesh!, tex, _materialBitmaps);
        UpdateTextureUI(tex, previewPath, isPreview: true);
        RefreshDerivedVisibility();
    }

    private void CommitPreview(bool revert = false)
    {
        // _committedTexturePath holds the correct final path in both branches:
        //   Apply:  ApplyPreview() assigned _pendingTexturePath to it before calling here.
        //   Cancel: CancelPreview() calls here without touching _committedTexturePath.
        var path = _committedTexturePath;
        var tex  = LoadBitmapImage(path);
        _previewActive      = false;
        _pendingTexturePath = null;
        if (_currentMesh != null) MeshModel = BuildWpfModel(_currentMesh, tex, _materialBitmaps);
        UpdateTextureUI(tex, path, isPreview: false);
        RefreshDerivedVisibility();
        StatusText = revert
            ? "Preview cancelled — texture unchanged."
            : $"Texture applied: {Path.GetFileName(path ?? "none")}";
    }

    private void CancelPreviewSilently() { _previewActive = false; _pendingTexturePath = null; }

    private void RebuildMaterialSlots(Mesh mesh)
    {
        MaterialTextureSlots.Clear();
        _materialBitmaps.Clear();

        if (mesh.MaterialNames.Count == 0)
        {
            // No named materials — single default slot.
            // Prefer a file-based texture; fall back to PDO embedded texture.
            var bmp  = LoadBitmapImage(mesh.SuggestedTexturePath)
                       ?? (mesh.EmbeddedTextures.Count > 0
                           ? BitmapFromEmbedded(mesh.EmbeddedTextures, 0)
                           : null);
            var slot = new MaterialTextureViewModel(-1, "Default", mesh.SuggestedTexturePath, bmp);
            MaterialTextureSlots.Add(slot);
            _materialBitmaps[-1] = bmp;
        }
        else
        {
            for (int i = 0; i < mesh.MaterialNames.Count; i++)
            {
                var path = (i < mesh.MaterialTexturePaths.Count)
                    ? mesh.MaterialTexturePaths[i]
                    : null;
                // For PDO multi-texture: if no file path, check embedded by index
                var bmp  = LoadBitmapImage(path)
                           ?? (path == null && i < mesh.EmbeddedTextures.Count
                               ? BitmapFromEmbedded(mesh.EmbeddedTextures, i)
                               : null);
                var slot = new MaterialTextureViewModel(i, mesh.MaterialNames[i], path, bmp);
                MaterialTextureSlots.Add(slot);
                _materialBitmaps[i] = bmp;
            }
        }
    }

    /// <summary>
    /// TD-PDO-4: Cached wrapper — returns the BitmapImage for EmbeddedTextures[index],
    /// building it only on first access. Subsequent calls return the cached result.
    /// Cache is keyed by texture index and cleared when a new mesh is loaded.
    /// </summary>
    private BitmapImage? BitmapFromEmbedded(IReadOnlyList<EmbeddedTextureData> textures, int index)
    {
        if (_embeddedBitmapCache.TryGetValue(index, out var cached)) return cached;
        var result = BitmapFromEmbeddedCore(textures[index]);
        _embeddedBitmapCache[index] = result;
        return result;
    }

    /// <summary>
    /// Converts raw RGB24 embedded texture data to a frozen WPF BitmapImage.
    /// Called at most once per texture index per mesh lifetime (see <see cref="BitmapFromEmbedded"/>).
    /// </summary>
    private static BitmapImage? BitmapFromEmbeddedCore(EmbeddedTextureData emb)
    {
        try
        {
            // Create a BitmapSource from raw RGB24 bytes (R,G,B order, top-to-bottom).
            var src = System.Windows.Media.Imaging.BitmapSource.Create(
                emb.Width, emb.Height, 96, 96,
                System.Windows.Media.PixelFormats.Rgb24,
                null,
                emb.Rgb24Bytes,
                emb.Width * 3);  // stride = width × 3 bytes

            // Encode to PNG in memory so BitmapImage can be frozen (required for cross-thread).
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));

            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;

            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource  = ms;
            bmp.CacheOption   = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// TD-22-2/22-3: returns a dict of materialId → file path for all slots that have a texture.
    private IReadOnlyDictionary<int, string?> GetMaterialTexturePaths() =>
        MaterialTextureSlots.ToDictionary(s => s.MaterialId, s => (string?)s.TexturePath);

    private void UpdateTextureUI(BitmapImage? tex, string? path, bool isPreview)
    {
        ActiveTextureThumbnail = tex;
        Canvas2DTexture        = tex;   // 2D canvas always reflects the currently displayed texture
        HasTexture             = _committedTexturePath != null;
        if (isPreview)
        {
            TextureStatusText = path != null ? $"Preview: {Path.GetFileName(path)}" : "Preview: No texture";
            PreviewLabelText  = path != null ? $"⚡  PREVIEW — {Path.GetFileName(path)}" : "⚡  PREVIEW — removing texture";
        }
        else
        {
            TextureStatusText = path != null ? Path.GetFileName(path) : "No texture";
        }
        OnPropertyChanged(nameof(NoTexturePlaceholderVisible));
        OnPropertyChanged(nameof(CanRemoveTexture));
    }

    private void RefreshDerivedVisibility()
    {
        OnPropertyChanged(nameof(TexturePanelVisible));
        OnPropertyChanged(nameof(PreviewBadgeVisible));
        OnPropertyChanged(nameof(ViewportBorderBrush));
    }

    private void RefreshColumnBindings()
    {
        OnPropertyChanged(nameof(RightColumnWidth));
        OnPropertyChanged(nameof(SplitterColumnWidth));
        OnPropertyChanged(nameof(RightPanelVisible));
    }

    // ── 3-D model builder ─────────────────────────────────────────────────────

    /// <param name="singleTexture">Fallback texture applied to all materials that have no per-material entry.</param>
    /// <param name="perMaterial">Optional per-material textures (materialId → bitmap). Null = use singleTexture for all.</param>
    private Model3DGroup BuildWpfModel(Mesh mesh, BitmapImage? singleTexture,
        IReadOnlyDictionary<int, BitmapImage?>? perMaterial = null)
    {
        _geoFaceIds.Clear();
        var group = new Model3DGroup();
        var s3d   = S.View3D;

        // Group faces by material so each material gets its own texture and geometry
        foreach (var matGroup in mesh.Faces.GroupBy(f => f.MaterialId))
        {
            var faces = matGroup.ToList();
            int matId = matGroup.Key;

            // Resolve texture: per-material first, then single fallback
            BitmapImage? tex = null;
            perMaterial?.TryGetValue(matId, out tex);
            tex ??= singleTexture;

            var positions = new Point3DCollection(faces.Count * 3);
            var indices   = new Int32Collection(faces.Count * 3);
            var normals   = new Vector3DCollection(faces.Count * 3);
            bool useUV    = tex != null && mesh.HasUVs;
            PointCollection? uvCoords = useUV ? new(faces.Count * 3) : null;
            var faceIdList = new List<int>(faces.Count);

            foreach (var face in faces)
            {
                int baseIdx = positions.Count;
                faceIdList.Add(face.Id);

                var va = mesh.Vertices[face.A].Position;
                var vb = mesh.Vertices[face.B].Position;
                var vc = mesh.Vertices[face.C].Position;

                positions.Add(new Point3D(va.X, va.Y, va.Z));
                positions.Add(new Point3D(vb.X, vb.Y, vb.Z));
                positions.Add(new Point3D(vc.X, vc.Y, vc.Z));
                indices.Add(baseIdx); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);

                var ab = new Vector3D(vb.X - va.X, vb.Y - va.Y, vb.Z - va.Z);
                var ac = new Vector3D(vc.X - va.X, vc.Y - va.Y, vc.Z - va.Z);
                var n  = Vector3D.CrossProduct(ab, ac); n.Normalize();
                normals.Add(n); normals.Add(n); normals.Add(n);

                if (uvCoords != null)
                {
                    var (ua, ub, uc) = mesh.FaceUVs[face.Id];
                    uvCoords.Add(ToWpfUV(mesh, ua));
                    uvCoords.Add(ToWpfUV(mesh, ub));
                    uvCoords.Add(ToWpfUV(mesh, uc));
                }
            }

            var geometry = new MeshGeometry3D
            {
                Positions = positions, TriangleIndices = indices, Normals = normals
            };
            if (uvCoords != null) geometry.TextureCoordinates = uvCoords;

            // Track face IDs for this geometry so hit-testing can map back to global face ID
            _geoFaceIds[geometry] = faceIdList;

            Brush fb = (tex != null && uvCoords != null)
                ? (Brush)new ImageBrush(tex)
                  {
                      ViewportUnits = BrushMappingMode.Absolute,
                      Viewport      = new Rect(0, 0, 1, 1),
                      TileMode      = TileMode.Tile,
                      Stretch       = Stretch.Fill
                  }
                : ParseBrush(s3d.FaceColor, "#64a0dc");

            if (fb is SolidColorBrush scb && s3d.FaceOpacity < 1.0)
            {
                var c = scb.Color;
                fb = new SolidColorBrush(Color.FromArgb((byte)(s3d.FaceOpacity * 255), c.R, c.G, c.B));
            }

            var mat     = new DiffuseMaterial(fb);
            var backMat = new DiffuseMaterial(ParseBrush(s3d.BackFaceColor, "#3c6496"));
            var model   = new GeometryModel3D(geometry, mat) { BackMaterial = backMat };
            group.Children.Add(model);
        }

        return group;
    }

    /// Resolves a 3D hit-test result to a global face ID.
    /// Handles both single-geometry (old) and per-material multi-geometry models.
    public int ResolveHitFaceId(object? geoObj, int v1, int v2, int v3)
    {
        int minVert = Math.Min(v1, Math.Min(v2, v3));
        if (geoObj is MeshGeometry3D geo && _geoFaceIds.TryGetValue(geo, out var faceIds))
        {
            int localIdx = minVert / 3;
            return localIdx < faceIds.Count ? faceIds[localIdx] : -1;
        }
        return minVert / 3;  // fallback for single-geometry (no per-material)
    }

    private static System.Windows.Point ToWpfUV(Mesh mesh, int idx)
    {
        if (idx < 0 || idx >= mesh.UVs.Count) return default;
        var uv = mesh.UVs[idx];
        return new System.Windows.Point(uv.X, 1.0 - uv.Y);
    }

    private static BitmapImage? LoadBitmapImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource   = new Uri(path, UriKind.Absolute);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public void Dispose()
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;
        CleanupTempBundle();
    }

    private void Error(string title, Exception ex)
    {
        // Walk to the innermost exception for the most useful message
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        var shortMsg = inner == ex ? ex.Message : $"{inner.Message}\n\n(outer: {ex.Message})";
        StatusText = $"{title}: {inner.Message}";
        MessageBox.Show(shortMsg, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static Brush ParseBrush(string? hex, string fallback)
    {
        try
        {
            if (!string.IsNullOrEmpty(hex))
            {
                var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                var b = new SolidColorBrush(color);
                b.Freeze();
                return b;
            }
        }
        catch { /* fall through */ }

        var fb = new SolidColorBrush(
            (Color)System.Windows.Media.ColorConverter.ConvertFromString(fallback));
        fb.Freeze();
        return fb;
    }
}
