using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using FourHUnfolder.App.Dialogs;
using FourHUnfolder.Application.Interfaces;
using FourHUnfolder.Application.Services;
using FourHUnfolder.Domain.Models;
using FourHUnfolder.Domain.Persistence;
using FourHUnfolder.Domain.Results;
using FourHUnfolder.Domain.Settings;
// Alias to avoid ambiguity with FourHUnfolder.Application namespace
using WpfApp = System.Windows.Application;

namespace FourHUnfolder.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly MeshService       _meshService;
    private readonly UnfoldService     _unfoldService;
    private readonly IExporter         _exporter;
    private readonly ProjectSerializer _serializer;
    private readonly SettingsService   _settingsService;

    // ── core state ────────────────────────────────────────────────────────────
    private Mesh?   _currentMesh;
    private string? _currentMeshPath;
    private string? _committedTexturePath;
    private string? _pendingTexturePath;
    private bool    _previewActive;
    private double  _currentScaleMmPerUnit = 1.0;   // persisted between re-runs

    // edge overrides: mesh edge ID → EdgeType (user's join/split operations)
    private readonly Dictionary<int, EdgeType> _edgeOverrides = new();

    // TD-5: selection overlay cache — avoid rebuilding geometry on every click
    private int?          _cachedOverlayGroupId;
    private Model3DGroup? _cachedOverlayModel;

    // TD-9: undo/redo — snapshots of (_edgeOverrides, piece layouts)
    private readonly record struct EditSnapshot(
        IReadOnlyDictionary<int, EdgeType> EdgeOverrides,
        IReadOnlyDictionary<int, (double X, double Y, double Rot)> PieceLayouts);

    private readonly Stack<EditSnapshot> _undoStack = new();
    private readonly Stack<EditSnapshot> _redoStack = new();

    // ── observable properties ─────────────────────────────────────────────────
    [ObservableProperty] private Model3DGroup? _meshModel;
    [ObservableProperty] private Model3DGroup? _selectionOverlayModel;  // 3D highlight
    [ObservableProperty] private string        _statusText   = "Ready — load a mesh to begin.";
    [ObservableProperty] private bool          _canUnfold;
    [ObservableProperty] private bool          _canExport;
    [ObservableProperty] private bool          _isUnfolded;
    [ObservableProperty] private int           _selectedFaceId = -1;

    // texture
    [ObservableProperty] private ImageSource?  _activeTextureThumbnail;
    [ObservableProperty] private string        _textureStatusText = "No texture";
    [ObservableProperty] private string        _previewLabelText  = string.Empty;
    [ObservableProperty] private bool          _hasTexture;

    // paper canvas
    [ObservableProperty] private PaperSizeModel _paperSizeModel = PaperSizeModel.A4;
    [ObservableProperty] private double          _pixelsPerMm    = 3.0;
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
                         IExporter exporter, ProjectSerializer serializer,
                         SettingsService settingsService)
    {
        _meshService     = meshService;
        _unfoldService   = unfoldService;
        _exporter        = exporter;
        _serializer      = serializer;
        _settingsService = settingsService;

        // Apply initial values from settings
        _pixelsPerMm = settingsService.Current.View2D.DefaultPixelsPerMm;
        _gridVisible  = settingsService.Current.View2D.ShowGrid;
        _snapToGrid   = settingsService.Current.View2D.SnapToGrid;

        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    // ── settings shortcut ─────────────────────────────────────────────────────
    private AppSettings S => _settingsService.Current;

    // ── settings changed handler ──────────────────────────────────────────────
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // Sync fast-path toggles from new settings
        GridVisible = S.View2D.ShowGrid;
        SnapToGrid  = S.View2D.SnapToGrid;

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

        if (_currentMesh != null)
        {
            var tex = LoadBitmapImage(_committedTexturePath);
            MeshModel = BuildWpfModel(_currentMesh, tex);
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
            Title  = "Open OBJ Mesh",
            Filter = "Wavefront OBJ (*.obj)|*.obj|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            StatusText = $"Loading {Path.GetFileName(dlg.FileName)} …";
            _currentMeshPath   = dlg.FileName;
            _currentMesh       = _meshService.LoadFromFile(dlg.FileName);
            _edgeOverrides.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            NotifyUndoRedo();
            InvalidateOverlayCache();

            CancelPreviewSilently();
            _committedTexturePath = _currentMesh.SuggestedTexturePath;

            var tex    = LoadBitmapImage(_committedTexturePath);
            MeshModel  = BuildWpfModel(_currentMesh, tex);
            CanUnfold  = true;
            CanExport  = false;
            IsUnfolded = false;
            Pieces.Clear();

            UpdateTextureUI(tex, _committedTexturePath, isPreview: false);
            RefreshDerivedVisibility();

            var texNote = _committedTexturePath != null ? " · texture from MTL" : string.Empty;
            StatusText = $"Loaded — {_currentMesh.Faces.Count:N0} faces, " +
                         $"{_currentMesh.Vertices.Count:N0} vertices{texNote}.";
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
            _currentScaleMmPerUnit = scale;
            var    unfoldResult = _unfoldService.Unfold(_currentMesh, _edgeOverrides);
            var    pieces       = _unfoldService.ComputePieces(_currentMesh);

            RebuildPieces(unfoldResult, pieces, scale);

            CanExport  = true;
            IsUnfolded = true;
            RefreshColumnBindings();

            var overlap = unfoldResult.HasOverlaps ? "  ⚠ overlaps" : string.Empty;
            StatusText = $"Unfolded — {Pieces.Count} pieces, " +
                         $"{unfoldResult.GlueTabs.Count} glue tabs.{overlap}";
        }
        catch (Exception ex) { Error("Unfold failed", ex); }
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
            // Re-run unfold to get a fresh UnfoldResult for SVG
            var result = _unfoldService.Unfold(_currentMesh, _edgeOverrides);
            // TD-8: pass committed texture path so SVG can embed it when UV data is present
            _exporter.Export(result, dlg.FileName, _committedTexturePath);
            StatusText = $"Exported to {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { Error("Export failed", ex); }
    }

    // ── SAVE / LOAD PROJECT ───────────────────────────────────────────────────
    [RelayCommand]
    private void SaveProject()
    {
        if (_currentMesh == null) { StatusText = "Nothing to save."; return; }

        var dlg = new SaveFileDialog
        {
            Title      = "Save Project",
            Filter     = "FourHUnfolder project (*.pmc)|*.pmc",
            DefaultExt = "pmc",
            FileName   = Path.GetFileNameWithoutExtension(_currentMeshPath ?? "project")
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var state = BuildProjectState();
            _serializer.Save(state, dlg.FileName);
            StatusText = $"Project saved: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { Error("Save failed", ex); }
    }

    [RelayCommand]
    private void LoadProject()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Open Project",
            Filter = "FourHUnfolder project (*.pmc)|*.pmc|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var state = _serializer.Load(dlg.FileName);
            RestoreProjectState(state);
        }
        catch (Exception ex) { Error("Load project failed", ex); }
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
    [RelayCommand] private void ApplyPreview()   { if (!_previewActive) return; _committedTexturePath = _pendingTexturePath; CommitPreview(); }
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

        // Highlight corresponding 2D piece and build 3D overlay
        var piece = Pieces.FirstOrDefault(p => p.Faces.Any(f => f.FaceId == faceId));
        foreach (var p in Pieces) p.IsSelected = (p == piece);
        BuildSelectionOverlay(piece);

        StatusText = piece != null
            ? $"Selected face {faceId}  ·  piece #{piece.GroupId}  ·  {piece.Faces.Length} triangles"
            : $"Selected face {faceId}  (no 2D piece — unfold first)";
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
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private void RerunUnfold(bool preservePositions = true)
    {
        if (_currentMesh == null) return;
        var oldPos = Pieces.ToDictionary(p => p.GroupId,
                                         p => (p.PositionX, p.PositionY, p.Rotation));
        var result = _unfoldService.Unfold(_currentMesh, _edgeOverrides);
        var groups = _unfoldService.ComputePieces(_currentMesh);
        RebuildPieces(result, groups, _currentScaleMmPerUnit);

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
        Pieces.Clear();
        for (int g = 0; g < groups.Count; g++)
        {
            var groupId = groups[g].Min();
            var vm      = PieceViewModel.Create(groupId, groups[g], result, _currentMesh!, scale);
            Pieces.Add(vm);
        }
    }

    private ProjectState BuildProjectState()
    {
        var state = new ProjectState
        {
            MeshPath       = _currentMeshPath,
            TexturePath    = _committedTexturePath,
            ScaleMmPerUnit = _currentScaleMmPerUnit,
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

        // Restore edge overrides
        foreach (var (id, typeName) in state.EdgeOverrides)
            if (Enum.TryParse<EdgeType>(typeName, out var t))
                _edgeOverrides[id] = t;

        // Restore paper
        PaperSizeModel = new PaperSizeModel(state.Paper.Name, state.Paper.WidthMm, state.Paper.HeightMm);

        // Re-run unfold
        var unfoldResult = _unfoldService.Unfold(_currentMesh, _edgeOverrides);
        var pieces       = _unfoldService.ComputePieces(_currentMesh);
        var layoutMap    = state.Layouts.ToDictionary(l => l.GroupId);

        _currentScaleMmPerUnit = state.ScaleMmPerUnit > 0 ? state.ScaleMmPerUnit : 1.0;
        RebuildPieces(unfoldResult, pieces, _currentScaleMmPerUnit);

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

        // Restore texture
        _committedTexturePath = state.TexturePath;
        var tex = LoadBitmapImage(_committedTexturePath);
        MeshModel  = BuildWpfModel(_currentMesh, tex);
        CanUnfold  = true;
        CanExport  = true;
        IsUnfolded = true;

        UpdateTextureUI(tex, _committedTexturePath, isPreview: false);
        RefreshDerivedVisibility();
        RefreshColumnBindings();

        StatusText = $"Project loaded — {Pieces.Count} pieces.";
    }

    // ── texture helpers ───────────────────────────────────────────────────────

    private void EnterPreview(string? previewPath)
    {
        _pendingTexturePath = previewPath;
        _previewActive      = true;
        var tex = LoadBitmapImage(previewPath);
        MeshModel = BuildWpfModel(_currentMesh!, tex);
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
        if (_currentMesh != null) MeshModel = BuildWpfModel(_currentMesh, tex);
        UpdateTextureUI(tex, path, isPreview: false);
        RefreshDerivedVisibility();
        StatusText = revert
            ? "Preview cancelled — texture unchanged."
            : $"Texture applied: {Path.GetFileName(path ?? "none")}";
    }

    private void CancelPreviewSilently() { _previewActive = false; _pendingTexturePath = null; }

    private void UpdateTextureUI(BitmapImage? tex, string? path, bool isPreview)
    {
        ActiveTextureThumbnail = tex;
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

    private Model3DGroup BuildWpfModel(Mesh mesh, BitmapImage? texture)
    {
        var positions = new Point3DCollection(mesh.Faces.Count * 3);
        var indices   = new Int32Collection(mesh.Faces.Count * 3);
        var normals   = new Vector3DCollection(mesh.Faces.Count * 3);

        bool useUV = texture != null && mesh.HasUVs;
        PointCollection? uvCoords = useUV ? new(mesh.Faces.Count * 3) : null;

        foreach (var face in mesh.Faces)
        {
            int baseIdx = positions.Count;
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

        var s3d = S.View3D;
        Brush fb = (texture != null && uvCoords != null)
            ? (Brush)new ImageBrush(texture) { Stretch = Stretch.Fill }
            : ParseBrush(s3d.FaceColor, "#64a0dc");

        // Apply face opacity
        if (fb is SolidColorBrush scb && s3d.FaceOpacity < 1.0)
        {
            var c = scb.Color;
            fb = new SolidColorBrush(Color.FromArgb(
                (byte)(s3d.FaceOpacity * 255), c.R, c.G, c.B));
        }

        var mat     = new DiffuseMaterial(fb);
        var backMat = new DiffuseMaterial(ParseBrush(s3d.BackFaceColor, "#3c6496"));
        var model   = new GeometryModel3D(geometry, mat) { BackMaterial = backMat };
        var group   = new Model3DGroup();
        group.Children.Add(model);
        return group;
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

    private void Error(string title, Exception ex)
    {
        StatusText = $"{title}: {ex.Message}";
        MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
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
