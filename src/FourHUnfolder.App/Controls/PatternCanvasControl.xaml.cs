using System.Collections.Specialized;
using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FourHUnfolder.App.ViewModels;
using FourHUnfolder.Domain.Models;
using FourHUnfolder.Domain.Settings;

namespace FourHUnfolder.App.Controls;

/// <summary>
/// Interactive 2-D layout canvas.
/// DataContext must be set to MainViewModel.
/// </summary>
public partial class PatternCanvasControl : UserControl
{
    // Tag placed on edge hit-zone lines; VisualLine points to the companion rendering Line
    private sealed class EdgeTag
    {
        public int  PieceId, FaceId, EdgeIdx, MeshEdgeId;
        public Line VisualLine = null!;
    }
    // ── constants ────────────────────────────────────────────────────────────
    private const double PaperMarginPx = 30.0;
    private const string GridTag       = "GRID";

    // ── state ────────────────────────────────────────────────────────────────
    private MainViewModel?              _vm;
    private double                      _pxPerMm = 3.0;
    private readonly Dictionary<int, Canvas> _containers = new();

    // TD-4 fix: explicit subscription tracking to prevent memory leaks
    private readonly Dictionary<PieceViewModel, PropertyChangedEventHandler> _pieceHandlers = new();

    // lasso (rubber-band) selection
    private bool       _lassoActive;
    private Point      _lassoOrigin;
    private Rectangle? _lassoRect;

    // middle-mouse pan
    private bool   _panActive;
    private Point  _panOriginMouse;
    private Point  _panLastMouse;
    private double _panOriginScrollH, _panOriginScrollV;

    // edge-edit mode
    private bool  _editModeActive;
    private Line? _hoveredEdgeLine;   // currently highlighted edge (restored on leave / mode-off)

    // rotate-by-point mode
    private bool   _rotatePtActive;
    private int    _rotatePtPhase;    // 0=pick pivot, 1=pick handle, 2=live rotation
    private readonly List<(Ellipse Dot, PieceViewModel Piece, double Lx, double Ly)> _vtxDots = new();
    private Ellipse?        _pivotDot;
    private Ellipse?        _handleDot;
    private PieceViewModel? _rotPiece;
    private double          _pivotCx, _pivotCy;      // pivot canvas-pixel position (fixed during drag)
    private double          _pivotLx, _pivotLy;      // pivot piece-local mm coords
    private double          _handleAngle0;            // initial pivot→handle angle when drag started
    private double          _pieceRotation0;          // piece.Rotation when drag started

    // drag state — multi-piece
    private PieceViewModel? _dragging;       // "leader" piece (the one that was clicked)
    private Point           _dragOriginMouse;
    private double          _dragOriginX, _dragOriginY;
    // origin positions of ALL selected pieces captured at drag start
    private Dictionary<int, (double X, double Y)>? _multiDragOrigins;

    // TD-N6: pre-drag snapshot for undo support (captured on MouseDown)
    private Dictionary<int, (double X, double Y, double Rot)>? _preDragPositions;

    // ── constructor ──────────────────────────────────────────────────────────
    public PatternCanvasControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // ── DataContext wiring ───────────────────────────────────────────────────
    private void OnDataContextChanged(object s, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel old)
        {
            old.Pieces.CollectionChanged -= OnPiecesChanged;
            old.PropertyChanged          -= OnVmPropertyChanged;
            old.ViewResetRequested       -= OnViewReset;
        }

        _vm = e.NewValue as MainViewModel;
        if (_vm == null) return;

        _pxPerMm = _vm.PixelsPerMm;
        if (ZoomLabel != null) ZoomLabel.Text = $"{_pxPerMm:F1} px/mm";

        _vm.Pieces.CollectionChanged += OnPiecesChanged;
        _vm.PropertyChanged          += OnVmPropertyChanged;
        _vm.ViewResetRequested       += OnViewReset;

        RebuildAll();
    }

    private void OnPiecesChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        // Suppress per-Add/Clear rebuilds during batch RebuildPieces; PiecesVersion fires one rebuild
        if (_vm?.BatchingPieces == true) return;
        Dispatcher.Invoke(RebuildAll);
    }

    private void OnViewReset()
    {
        Dispatcher.Invoke(() =>
        {
            Scroller.ScrollToTop();
            Scroller.ScrollToLeftEnd();
        });
    }

    private void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // Fast-path: just toggle grid line visibility without full rebuild
            case nameof(MainViewModel.GridVisible):
                if (_vm != null) Dispatcher.Invoke(() => ApplyGridVisibility(_vm.GridVisible));
                break;

            // Settings / state that affect piece rendering need full rebuild
            case nameof(MainViewModel.PaperSizeModel):
            case nameof(MainViewModel.PixelsPerMm):
            case nameof(MainViewModel.View2DSettings):
            case nameof(MainViewModel.PagesWide):
            case nameof(MainViewModel.PagesTall):
            case nameof(MainViewModel.PiecesVersion):   // single rebuild after batch RebuildPieces
            case nameof(MainViewModel.Canvas2DTexture): // texture added/removed/changed
                Dispatcher.Invoke(RebuildAll);
                break;
        }
    }

    // ── full rebuild ─────────────────────────────────────────────────────────
    private void RebuildAll()
    {
        _hoveredEdgeLine = null;

        // TD-S13-3: save pivot state so we can try to restore it after rebuild
        int    savedRotGroupId = _rotPiece?.GroupId ?? -1;
        int    savedPhase      = _rotatePtPhase;
        double savedPivotLx    = _pivotLx;
        double savedPivotLy    = _pivotLy;

        ClearVtxDots();
        ResetRotatePhase();

        // TD-4 fix: unsubscribe all tracked PropertyChanged handlers before clearing
        foreach (var (piece, handler) in _pieceHandlers)
            piece.PropertyChanged -= handler;
        _pieceHandlers.Clear();

        RootCanvas.Children.Clear();
        _containers.Clear();

        if (_vm == null) return;
        _pxPerMm = _vm.PixelsPerMm;

        DrawPaper(_vm.PaperSizeModel);

        foreach (var piece in _vm.Pieces)
            AddPiece(piece);

        SyncAllTransforms();
        UpdateCanvasSize();

        if (_rotatePtActive)
        {
            ShowVtxDots();
            // TD-S13-3: restore pivot dot after rebuild if piece still exists
            if (savedPhase >= 1 && savedRotGroupId >= 0)
            {
                var restoredPiece = _vm.Pieces.FirstOrDefault(p => p.GroupId == savedRotGroupId);
                if (restoredPiece != null)
                {
                    var entry = _vtxDots.FirstOrDefault(t =>
                        t.Piece.GroupId == savedRotGroupId &&
                        Math.Abs(t.Lx - savedPivotLx) < 0.01 &&
                        Math.Abs(t.Ly - savedPivotLy) < 0.01);
                    if (entry != default)
                    {
                        _pivotDot = entry.Dot;
                        _pivotDot.Fill = _dotRedBrush;
                        _rotPiece  = restoredPiece;
                        _pivotLx   = savedPivotLx;
                        _pivotLy   = savedPivotLy;
                        (_pivotCx, _pivotCy) = VtxCanvasPx(restoredPiece, savedPivotLx, savedPivotLy);
                        _rotatePtPhase = 1;
                    }
                }
            }
        }
    }

    // ── paper background ─────────────────────────────────────────────────────
    private void DrawPaper(PaperSizeModel paper)
    {
        var s2d = _vm?.View2DSettings;
        RootCanvas.Background = HexBrush(s2d?.CanvasBackground, "#3a3a5a");

        int pagesWide = _vm?.PagesWide ?? 1;
        int pagesTall = _vm?.PagesTall ?? 1;
        double sep    = MainViewModel.PageSepMm;

        for (int row = 0; row < pagesTall; row++)
        for (int col = 0; col < pagesWide; col++)
        {
            double ox = (col * (paper.WidthMm  + sep)) * _pxPerMm + PaperMarginPx;
            double oy = (row * (paper.HeightMm + sep)) * _pxPerMm + PaperMarginPx;
            DrawPageAt(paper, s2d, ox, oy, col, row);
        }
    }

    private void DrawPageAt(PaperSizeModel paper,
                            AppSettings.View2DSettings? s2d,
                            double ox, double oy, int col, int row)
    {
        double pw = paper.WidthMm  * _pxPerMm;
        double ph = paper.HeightMm * _pxPerMm;

        var shadow = new DropShadowEffect
            { BlurRadius = 12, Opacity = 0.5, ShadowDepth = 4, Color = Colors.Black };

        var rect = new Rectangle
        {
            Width  = pw, Height = ph,
            Fill   = HexBrush(s2d?.PaperColor, "#ffffff"),
            Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 200)),
            StrokeThickness = 1,
            Effect = shadow
        };
        Canvas.SetLeft(rect, ox);
        Canvas.SetTop (rect, oy);
        Panel.SetZIndex(rect, 0);
        RootCanvas.Children.Add(rect);

        // Page label
        string pageLabel = (_vm?.PagesWide * _vm?.PagesTall > 1)
            ? $"{paper.Name}  p.{row * (_vm?.PagesWide ?? 1) + col + 1}"
            : $"{paper.Name}  ({_vm?.FormatMm(paper.WidthMm) ?? ""} × {_vm?.FormatMm(paper.HeightMm) ?? ""})";
        var lbl = new TextBlock
        {
            Text = pageLabel, Foreground = Brushes.Gray, FontSize = 10
        };
        Canvas.SetLeft(lbl, ox);
        Canvas.SetTop (lbl, oy - 16);
        Panel.SetZIndex(lbl, 0);
        RootCanvas.Children.Add(lbl);

        // Grid lines — tagged for fast show/hide toggle
        bool showGrid = _vm?.GridVisible ?? (s2d?.ShowGrid ?? true);
        double gridMm = Math.Max(1, s2d?.GridSizeMm ?? 10);
        var   gridClr = HexBrush(s2d?.GridColor, "#28000080");
        var   gridVis = showGrid ? Visibility.Visible : Visibility.Collapsed;

        for (double x = 0; x <= pw; x += gridMm * _pxPerMm)
        {
            var line = new Line
            {
                X1 = ox + x, Y1 = oy,
                X2 = ox + x, Y2 = oy + ph,
                Stroke = gridClr, StrokeThickness = 0.5,
                Tag = GridTag, Visibility = gridVis
            };
            Panel.SetZIndex(line, 1);
            RootCanvas.Children.Add(line);
        }
        for (double y = 0; y <= ph; y += gridMm * _pxPerMm)
        {
            var line = new Line
            {
                X1 = ox,      Y1 = oy + y,
                X2 = ox + pw, Y2 = oy + y,
                Stroke = gridClr, StrokeThickness = 0.5,
                Tag = GridTag, Visibility = gridVis
            };
            Panel.SetZIndex(line, 1);
            RootCanvas.Children.Add(line);
        }
    }

    // ── grid fast-toggle (no full rebuild needed) ─────────────────────────────
    private void ApplyGridVisibility(bool show)
    {
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        foreach (UIElement el in RootCanvas.Children)
            if (el is FrameworkElement fe && GridTag.Equals(fe.Tag))
                fe.Visibility = vis;
    }

    // ── piece rendering ──────────────────────────────────────────────────────
    private void AddPiece(PieceViewModel piece)
    {
        var container = new Canvas { IsHitTestVisible = true };
        container.Tag = piece;

        // TD-4 fix: store handler reference so we can unsubscribe later
        PropertyChangedEventHandler handler = (_, ev) =>
        {
            switch (ev.PropertyName)
            {
                case nameof(PieceViewModel.IsSelected):
                    Dispatcher.Invoke(() =>
                    {
                        RenderPieceShapes(container, piece);
                        // BUG-PDO-3: when a piece becomes selected scroll the 2D canvas
                        // to bring it into view (guards against any positioning error placing
                        // the piece outside the visible viewport).
                        if (piece.IsSelected) ScrollToShowPiece(piece);
                    });
                    break;
                // Sync canvas transform whenever piece position or rotation changes in the VM
                case nameof(PieceViewModel.PositionX):
                case nameof(PieceViewModel.PositionY):
                case nameof(PieceViewModel.Rotation):
                    Dispatcher.Invoke(() => { SyncTransform(piece); if (_rotatePtActive) UpdateVtxDotPositions(piece); });
                    break;
            }
        };
        _pieceHandlers[piece] = handler;
        piece.PropertyChanged += handler;

        RenderPieceShapes(container, piece);

        container.MouseLeftButtonDown += Piece_MouseDown;
        Panel.SetZIndex(container, 10);
        RootCanvas.Children.Add(container);
        _containers[piece.GroupId] = container;
    }

    private void RenderPieceShapes(Canvas container, PieceViewModel piece)
    {
        container.Children.Clear();
        var s2d = _vm?.View2DSettings;

        var solidFill = HexBrush(s2d?.FaceFillColor, "#c8fffde2");
        var selFill   = new SolidColorBrush(Color.FromArgb(180, 160, 210, 255));

        var foldBrush  = HexBrush(s2d?.FoldLineColor, "#4169e1");
        var cutBrush   = HexBrush(s2d?.CutLineColor,  "#ff0000");
        // Boundary edges: outer mesh edges drawn as thin dark border (no tab, no fold)
        var boundBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
        double foldW   = s2d?.FoldLineWidth ?? 0.8;
        double cutW    = s2d?.CutLineWidth  ?? 1.0;
        var foldDash   = ParseDash(s2d?.FoldLineDash ?? "4,2");

        // TD-2 fix: deduplicate shared edges using mesh edge IDs
        var drawnEdgeIds = new HashSet<int>();

        foreach (var fd in piece.Faces)
        {
            // Per-face texture: use material-specific texture if available
            var texture = _vm?.GetCanvas2DTexture(fd.MaterialId);

            // Per-face fill: texture when UV data available, else solid; selection overrides all
            Brush fill;
            if (piece.IsSelected)
            {
                fill = selFill;
            }
            else if (texture != null && fd.UVCoords is { Length: >= 3 })
            {
                fill = BuildTextureBrush(texture,
                           Sc(fd.V0), Sc(fd.V1), Sc(fd.V2),
                           fd.UVCoords[0], fd.UVCoords[1], fd.UVCoords[2])
                       ?? solidFill;
            }
            else
            {
                fill = solidFill;
            }

            // TD-7: remove triangle border stroke — fold/cut/boundary lines handle all outlines
            var poly = new Polygon
            {
                Fill            = fill,
                Stroke          = null,
                StrokeThickness = 0,
                Points          = new PointCollection([Sc(fd.V0), Sc(fd.V1), Sc(fd.V2)])
            };
            poly.Tag = piece;
            container.Children.Add(poly);

            // Face number label (optional)
            if (s2d?.ShowFaceNumbers == true)
            {
                double cx = (fd.V0.X + fd.V1.X + fd.V2.X) / 3.0 * _pxPerMm;
                double cy = (fd.V0.Y + fd.V1.Y + fd.V2.Y) / 3.0 * _pxPerMm;
                var numLbl = new TextBlock
                {
                    Text             = fd.FaceId.ToString(),
                    FontSize         = Math.Max(6, _pxPerMm * 2),
                    Foreground       = HexBrush(s2d?.FaceNumberColor, "#888888"),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(numLbl, cx - 6);
                Canvas.SetTop (numLbl, cy - 6);
                container.Children.Add(numLbl);
            }

            // Edges — skip mesh edges already drawn (TD-2 dedup)
            Point[] verts = [Sc(fd.V0), Sc(fd.V1), Sc(fd.V2)];
            for (int i = 0; i < 3; i++)
            {
                int meshEdgeId = fd.MeshEdgeIds[i];
                if (!drawnEdgeIds.Add(meshEdgeId)) continue;

                bool isFold     = fd.EdgeIsFold[i];
                bool isBoundary = fd.EdgeIsBoundary[i];

                // TD-7: boundary edges get a distinct thin dark style
                Brush  stroke    = isBoundary ? boundBrush : (isFold ? foldBrush : cutBrush);
                double thickness = isBoundary ? 0.6 : (isFold ? foldW : cutW);
                var    dash      = (!isBoundary && isFold) ? foldDash : null;

                var et = new EdgeTag
                {
                    PieceId = piece.GroupId, FaceId = fd.FaceId,
                    EdgeIdx = i, MeshEdgeId = meshEdgeId
                };

                // Visual line — rendering only, no hit test
                var visLine = new Line
                {
                    X1 = verts[i].X,           Y1 = verts[i].Y,
                    X2 = verts[(i + 1) % 3].X, Y2 = verts[(i + 1) % 3].Y,
                    Stroke           = stroke,
                    StrokeThickness  = thickness,
                    StrokeDashArray  = dash,
                    IsHitTestVisible = false,
                    Tag              = et
                };
                et.VisualLine = visLine;
                container.Children.Add(visLine);

                // Transparent hit-zone line on top — wider area so edges are easy to click
                var hitLine = new Line
                {
                    X1 = visLine.X1, Y1 = visLine.Y1, X2 = visLine.X2, Y2 = visLine.Y2,
                    Stroke          = Brushes.Transparent,
                    StrokeThickness = 8,
                    Cursor          = isBoundary ? Cursors.Arrow : Cursors.Hand,
                    Tag             = et
                };
                if (!isBoundary)
                {
                    hitLine.MouseRightButtonDown += Edge_RightClick;
                    hitLine.MouseEnter           += Edge_MouseEnter;
                    hitLine.MouseLeave           += Edge_MouseLeave;
                    hitLine.MouseLeftButtonDown  += Edge_LeftClick;
                }
                container.Children.Add(hitLine);
            }
        }

        // Piece outline: merged boundary polygon for a cleaner silhouette
        var outlinePts = BuildPieceOutline(piece, _pxPerMm);
        if (outlinePts != null && outlinePts.Count >= 3)
        {
            var outlinePoly = new Polygon
            {
                Fill            = Brushes.Transparent,
                Stroke          = piece.IsSelected
                    ? new SolidColorBrush(Color.FromArgb(200, 80, 140, 255))
                    : HexBrush(s2d?.CutLineColor, "#ff0000"),
                StrokeThickness = piece.IsSelected ? 2.0 : (s2d?.CutLineWidth ?? 1.0) * 1.2,
                Points          = outlinePts,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };
            Panel.SetZIndex(outlinePoly, 2);
            container.Children.Add(outlinePoly);
        }

        // Glue tabs (conditionally shown)
        bool showTabs = s2d?.ShowGlueTabs ?? true;
        if (showTabs)
        {
            var tabFill = HexBrush(s2d?.GlueTabColor, "#7850c850");
            foreach (var tab in piece.GlueTabs)
            {
                var tabPoly = new Polygon
                {
                    Fill            = tabFill,
                    Stroke          = Brushes.DarkGreen,
                    StrokeThickness = 0.5,
                    Points          = new PointCollection(tab.Points.Select(Sc))
                };
                container.Children.Add(tabPoly);
            }
        }

        // Edge ID labels + glue arrows on cut edges
        if (s2d?.ShowEdgeIds == true)
        {
            var idBrush  = HexBrush(s2d?.EdgeIdColor, "#cc333333");
            double fontSize = Math.Max(5, _pxPerMm * 2.2);

            // Piece centroid in px for outward-arrow direction
            double allCx = piece.Faces.SelectMany(f => new[] { f.V0.X, f.V1.X, f.V2.X }).Average() * _pxPerMm;
            double allCy = piece.Faces.SelectMany(f => new[] { f.V0.Y, f.V1.Y, f.V2.Y }).Average() * _pxPerMm;

            var drawnPairIds = new HashSet<int>();
            foreach (var fd in piece.Faces)
            {
                Point[] verts = [Sc(fd.V0), Sc(fd.V1), Sc(fd.V2)];
                for (int i = 0; i < 3; i++)
                {
                    int pairId = fd.EdgePairIds[i];
                    if (pairId == 0 || !drawnPairIds.Add(fd.MeshEdgeIds[i])) continue;

                    var p0 = verts[i];
                    var p1 = verts[(i + 1) % 3];
                    double mx = (p0.X + p1.X) / 2;
                    double my = (p0.Y + p1.Y) / 2;

                    // Outward direction (away from centroid)
                    double nx = mx - allCx;
                    double ny = my - allCy;
                    double nLen = Math.Sqrt(nx * nx + ny * ny);
                    if (nLen > 0.001) { nx /= nLen; ny /= nLen; }

                    // Small arrow polygon pointing outward
                    double al = Math.Max(4, _pxPerMm * 1.5);
                    var ex = p1.X - p0.X; var ey = p1.Y - p0.Y;
                    double eLen = Math.Sqrt(ex * ex + ey * ey);
                    if (eLen > 0.001) { ex /= eLen; ey /= eLen; }
                    var arrow = new Polygon
                    {
                        Fill   = idBrush,
                        Points = new PointCollection([
                            new Point(mx + nx * al * 1.8,        my + ny * al * 1.8),
                            new Point(mx - ex * al * 0.6 + nx * al * 0.4, my - ey * al * 0.6 + ny * al * 0.4),
                            new Point(mx + ex * al * 0.6 + nx * al * 0.4, my + ey * al * 0.6 + ny * al * 0.4)
                        ]),
                        IsHitTestVisible = false
                    };
                    container.Children.Add(arrow);

                    // Number label
                    var lbl = new TextBlock
                    {
                        Text             = pairId.ToString(),
                        FontSize         = fontSize,
                        FontWeight       = FontWeights.Bold,
                        Foreground       = idBrush,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(lbl, mx + nx * (al * 2.2) - fontSize * 0.4);
                    Canvas.SetTop (lbl, my + ny * (al * 2.2) - fontSize * 0.55);
                    container.Children.Add(lbl);
                }
            }
        }
    }

    // ── transform sync ───────────────────────────────────────────────────────
    private void SyncAllTransforms()
    {
        foreach (var piece in _vm?.Pieces ?? [])
            SyncTransform(piece);
    }

    private void SyncTransform(PieceViewModel piece)
    {
        if (!_containers.TryGetValue(piece.GroupId, out var c)) return;

        var tg = new TransformGroup();
        tg.Children.Add(new RotateTransform(piece.Rotation));
        tg.Children.Add(new TranslateTransform(
            piece.PositionX * _pxPerMm + PaperMarginPx,
            piece.PositionY * _pxPerMm + PaperMarginPx));
        c.RenderTransform = tg;
    }

    /// <summary>
    /// Scrolls the 2D canvas viewport to bring the piece center into view.
    /// Only scrolls if the piece center is currently outside the visible viewport.
    /// </summary>
    private void ScrollToShowPiece(PieceViewModel piece)
    {
        double cx = piece.PositionX * _pxPerMm + PaperMarginPx;
        double cy = piece.PositionY * _pxPerMm + PaperMarginPx;

        double left   = Scroller.HorizontalOffset;
        double top    = Scroller.VerticalOffset;
        double right  = left + Scroller.ViewportWidth;
        double bottom = top  + Scroller.ViewportHeight;

        if (cx < left || cx > right || cy < top || cy > bottom)
        {
            Scroller.ScrollToHorizontalOffset(Math.Max(0, cx - Scroller.ViewportWidth  / 2));
            Scroller.ScrollToVerticalOffset  (Math.Max(0, cy - Scroller.ViewportHeight / 2));
        }
    }

    private void UpdateCanvasSize()
    {
        if (_vm == null) return;
        double sep    = MainViewModel.PageSepMm;
        double totalW = (_vm.PagesWide * _vm.PaperSizeModel.WidthMm  + (_vm.PagesWide - 1) * sep) * _pxPerMm
                        + PaperMarginPx * 2;
        double totalH = (_vm.PagesTall * _vm.PaperSizeModel.HeightMm + (_vm.PagesTall - 1) * sep) * _pxPerMm
                        + PaperMarginPx * 2;
        RootCanvas.Width  = Math.Max(totalW, 400);
        RootCanvas.Height = Math.Max(totalH, 400);
    }

    // ── drag handling ────────────────────────────────────────────────────────
    private void Piece_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_editModeActive || _rotatePtActive) return;   // special modes suppress drag
        if (sender is not Canvas c || c.Tag is not PieceViewModel piece) return;

        bool multiMod = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;

        if (_vm != null)
        {
            if (multiMod)
            {
                // Toggle this piece in multi-selection
                piece.IsSelected = !piece.IsSelected;
                if (piece.IsSelected) _vm.SelectPiece2D(piece.GroupId);
            }
            else if (!piece.IsSelected)
            {
                // Single-select only if not already selected (allows dragging a group)
                foreach (var p in _vm.Pieces) p.IsSelected = (p == piece);
                _vm.SelectPiece2D(piece.GroupId);
            }
        }

        _dragging        = piece;
        _dragOriginMouse = e.GetPosition(RootCanvas);
        _dragOriginX     = piece.PositionX;
        _dragOriginY     = piece.PositionY;

        // Snapshot origins of every selected piece for multi-drag
        _multiDragOrigins = _vm?.Pieces
            .Where(p => p.IsSelected)
            .ToDictionary(p => p.GroupId, p => (p.PositionX, p.PositionY));

        _preDragPositions = _vm?.Pieces
            .ToDictionary(p => p.GroupId, p => (p.PositionX, p.PositionY, p.Rotation));

        RootCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_LeftDown(object sender, MouseButtonEventArgs e)
    {
        // Phase 2: canvas click confirms rotation
        if (_rotatePtPhase == 2)
        {
            if (_preDragPositions != null && _vm != null)
                _vm.PushDragUndo(_preDragPositions);
            ResetRotatePhase();
            e.Handled = true;
            return;
        }

        // Normal mode: start lasso selection on empty canvas
        if (_editModeActive || _rotatePtActive) return;

        bool multiMod = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;

        // Deselect all immediately if no modifier key
        if (!multiMod) _vm?.ClearSelection();

        // Begin rubber-band lasso
        _lassoActive = true;
        _lassoOrigin = e.GetPosition(RootCanvas);
        _lassoRect   = new Rectangle
        {
            Stroke          = Brushes.DodgerBlue,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection([4, 2]),
            Fill            = new SolidColorBrush(Color.FromArgb(25, 30, 100, 255)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(_lassoRect, _lassoOrigin.X);
        Canvas.SetTop (_lassoRect, _lassoOrigin.Y);
        Panel.SetZIndex(_lassoRect, 200);
        RootCanvas.Children.Add(_lassoRect);
        RootCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        // ── rotate-by-point live drag (phase 2) ───────────────────────────────
        if (_rotatePtPhase == 2 && _rotPiece != null)
        {
            var mouse = e.GetPosition(RootCanvas);
            double angle    = Math.Atan2(mouse.Y - _pivotCy, mouse.X - _pivotCx);
            double angleDelta = angle - _handleAngle0;
            while (angleDelta >  Math.PI) angleDelta -= 2 * Math.PI;
            while (angleDelta < -Math.PI) angleDelta += 2 * Math.PI;

            double newRotDeg = _pieceRotation0 + angleDelta * 180.0 / Math.PI;
            double cosR = Math.Cos(newRotDeg * Math.PI / 180.0);
            double sinR = Math.Sin(newRotDeg * Math.PI / 180.0);

            // Keep pivot canvas-pixel position fixed
            _rotPiece.PositionX = (_pivotCx - PaperMarginPx) / _pxPerMm - (_pivotLx * cosR - _pivotLy * sinR);
            _rotPiece.PositionY = (_pivotCy - PaperMarginPx) / _pxPerMm - (_pivotLx * sinR + _pivotLy * cosR);
            _rotPiece.Rotation  = (newRotDeg % 360 + 360) % 360;
            SyncTransform(_rotPiece);
            UpdateVtxDotPositions(_rotPiece);
            return;
        }

        // Lasso resize
        if (_lassoActive && _lassoRect != null)
        {
            var lpos = e.GetPosition(RootCanvas);
            double lx = Math.Min(lpos.X, _lassoOrigin.X);
            double ly = Math.Min(lpos.Y, _lassoOrigin.Y);
            Canvas.SetLeft(_lassoRect, lx);
            Canvas.SetTop (_lassoRect, ly);
            _lassoRect.Width  = Math.Abs(lpos.X - _lassoOrigin.X);
            _lassoRect.Height = Math.Abs(lpos.Y - _lassoOrigin.Y);
            return;
        }

        if (_dragging == null) return;
        var pos   = e.GetPosition(RootCanvas);
        var delta = pos - _dragOriginMouse;

        double newX = _dragOriginX + delta.X / _pxPerMm;
        double newY = _dragOriginY + delta.Y / _pxPerMm;

        // Snap to grid if enabled
        var s2d = _vm?.View2DSettings;
        if ((_vm?.SnapToGrid == true) && s2d != null && s2d.GridSizeMm > 0)
        {
            double g = s2d.GridSizeMm;
            newX = Math.Round(newX / g) * g;
            newY = Math.Round(newY / g) * g;
        }

        // Move all selected pieces by same delta
        if (_multiDragOrigins != null && _vm != null)
        {
            foreach (var p in _vm.Pieces.Where(p => p.IsSelected))
            {
                if (!_multiDragOrigins.TryGetValue(p.GroupId, out var origin)) continue;
                double px = origin.X + delta.X / _pxPerMm;
                double py = origin.Y + delta.Y / _pxPerMm;
                if ((_vm.SnapToGrid) && s2d != null && s2d.GridSizeMm > 0)
                {
                    double g = s2d.GridSizeMm;
                    px = Math.Round(px / g) * g;
                    py = Math.Round(py / g) * g;
                }
                p.PositionX = px;
                p.PositionY = py;
                SyncTransform(p);
            }
        }
        else
        {
            _dragging.PositionX = newX;
            _dragging.PositionY = newY;
            SyncTransform(_dragging);
        }
    }

    private void Canvas_MouseUp(object sender, MouseEventArgs e)
    {
        // Lasso selection confirm
        if (_lassoActive)
        {
            RootCanvas.ReleaseMouseCapture();
            if (_lassoRect != null)
            {
                RootCanvas.Children.Remove(_lassoRect);
                // Only apply selection if lasso is bigger than a click
                if (_lassoRect.Width > 3 || _lassoRect.Height > 3)
                {
                    var lassoR = new Rect(
                        Canvas.GetLeft(_lassoRect), Canvas.GetTop(_lassoRect),
                        _lassoRect.Width, _lassoRect.Height);
                    if (_vm != null)
                        foreach (var p in _vm.Pieces)
                            if (AnyVertexInLasso(p, lassoR))
                                p.IsSelected = true;
                }
                _lassoRect = null;
            }
            _lassoActive = false;
            return;
        }

        if (_dragging == null) return;
        RootCanvas.ReleaseMouseCapture();

        bool moved = Math.Abs(_dragging.PositionX - _dragOriginX) > 0.5
                  || Math.Abs(_dragging.PositionY - _dragOriginY) > 0.5;

        if (moved && _vm != null)
        {
            if (_preDragPositions != null)
                _vm.PushDragUndo(_preDragPositions);

            // Expand page for each moved piece's rotated bounding box
            var movedPieces = _multiDragOrigins != null
                ? _vm.Pieces.Where(p => p.IsSelected && _multiDragOrigins.ContainsKey(p.GroupId))
                : (IEnumerable<PieceViewModel>)[_dragging];

            foreach (var piece in movedPieces.Where(p => p.Faces.Length > 0))
            {
                var allX = piece.Faces.SelectMany(f => new[] { f.V0.X, f.V1.X, f.V2.X });
                var allY = piece.Faces.SelectMany(f => new[] { f.V0.Y, f.V1.Y, f.V2.Y });
                double lMinX = allX.Min(), lMaxX = allX.Max();
                double lMinY = allY.Min(), lMaxY = allY.Max();
                double rotRad = piece.Rotation * Math.PI / 180.0;
                double cosR = Math.Cos(rotRad), sinR = Math.Sin(rotRad);
                double[] cxs = { lMinX, lMaxX, lMinX, lMaxX };
                double[] cys = { lMinY, lMinY, lMaxY, lMaxY };
                _vm.EnsurePageForPosition(
                    piece.PositionX + cxs.Zip(cys, (x, y) => x * cosR - y * sinR).Max(),
                    piece.PositionY + cxs.Zip(cys, (x, y) => x * sinR + y * cosR).Max());
            }
            _vm.TrimEmptyPages();
        }

        _preDragPositions = null;
        _multiDragOrigins = null;
        _dragging = null;
    }

    // ── edge right-click context menu ────────────────────────────────────────
    private void Edge_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Line line || line.Tag is not EdgeTag et) return;
        if (_vm == null) return;

        var isFold = _vm.IsEdgeFold(et.MeshEdgeId);
        var menu   = new ContextMenu();

        if (isFold)
        {
            var split = new MenuItem { Header = "✂  Split piece here (Fold → Cut)" };
            split.Click += (_, _) => _vm.ToggleEdge(et.MeshEdgeId);
            menu.Items.Add(split);
        }
        else
        {
            var join = new MenuItem { Header = "🔗  Join pieces here (Cut → Fold)" };
            join.Click += (_, _) => _vm.ToggleEdge(et.MeshEdgeId);
            menu.Items.Add(join);
        }

        menu.IsOpen = true;
        e.Handled   = true;
    }

    // ── toolbar buttons ──────────────────────────────────────────────────────
    private void RotateCW_Click (object s, RoutedEventArgs e) => RotateSelected(+90);
    private void RotateCCW_Click(object s, RoutedEventArgs e) => RotateSelected(-90);

    // F5: Parts alignment
    private void AlignLeft_Click   (object s, RoutedEventArgs e) => AlignSelected(AlignMode.Left);
    private void AlignRight_Click  (object s, RoutedEventArgs e) => AlignSelected(AlignMode.Right);
    private void AlignTop_Click    (object s, RoutedEventArgs e) => AlignSelected(AlignMode.Top);
    private void AlignBottom_Click (object s, RoutedEventArgs e) => AlignSelected(AlignMode.Bottom);
    private void AlignCenterH_Click(object s, RoutedEventArgs e) => AlignSelected(AlignMode.CenterH);
    private void AlignCenterV_Click(object s, RoutedEventArgs e) => AlignSelected(AlignMode.CenterV);

    private enum AlignMode { Left, Right, Top, Bottom, CenterH, CenterV }

    private void AlignSelected(AlignMode mode)
    {
        if (_vm == null) return;
        var sel = _vm.Pieces.Where(p => p.IsSelected).ToList();
        if (sel.Count < 2) return;

        var pre = _vm.Pieces.ToDictionary(p => p.GroupId, p => (p.PositionX, p.PositionY, p.Rotation));

        // Compute each piece's AABB in canvas mm coords
        static (double minX, double minY, double maxX, double maxY) PieceAabb(PieceViewModel piece)
        {
            double rotRad = piece.Rotation * Math.PI / 180.0;
            double cosR = Math.Cos(rotRad), sinR = Math.Sin(rotRad);
            var pts = piece.Faces.SelectMany(f => new[] { f.V0, f.V1, f.V2 });
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var pt in pts)
            {
                double rx = pt.X * cosR - pt.Y * sinR + piece.PositionX;
                double ry = pt.X * sinR + pt.Y * cosR + piece.PositionY;
                if (rx < minX) minX = rx; if (rx > maxX) maxX = rx;
                if (ry < minY) minY = ry; if (ry > maxY) maxY = ry;
            }
            return (minX, minY, maxX, maxY);
        }

        var boxes = sel.Select(p => (piece: p, aabb: PieceAabb(p))).ToList();
        double refL = boxes.Min(b => b.aabb.minX);
        double refR = boxes.Max(b => b.aabb.maxX);
        double refT = boxes.Min(b => b.aabb.minY);
        double refB = boxes.Max(b => b.aabb.maxY);
        double refCH = (refL + refR) / 2;
        double refCV = (refT + refB) / 2;

        foreach (var (piece, aabb) in boxes)
        {
            double dx = mode switch
            {
                AlignMode.Left    => refL   - aabb.minX,
                AlignMode.Right   => refR   - aabb.maxX,
                AlignMode.CenterH => refCH  - (aabb.minX + aabb.maxX) / 2,
                _                 => 0
            };
            double dy = mode switch
            {
                AlignMode.Top     => refT   - aabb.minY,
                AlignMode.Bottom  => refB   - aabb.maxY,
                AlignMode.CenterV => refCV  - (aabb.minY + aabb.maxY) / 2,
                _                 => 0
            };
            piece.PositionX += dx;
            piece.PositionY += dy;
        }

        _vm.PushDragUndo(pre);
    }

    private void RotateSelected(double delta)
    {
        if (_vm == null) return;
        var selected = _vm.Pieces.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;
        var pre = _vm.Pieces.ToDictionary(p => p.GroupId, p => (p.PositionX, p.PositionY, p.Rotation));
        foreach (var piece in selected)
        {
            piece.Rotation = (piece.Rotation + delta + 360) % 360;
            SyncTransform(piece);
        }
        _vm.PushDragUndo(pre);
    }

    private void AutoArrange_Click(object s, RoutedEventArgs e) =>
        _vm?.AutoArrangeCommand.Execute(null);

    // ── grid toggle (via ViewModel command) ──────────────────────────────────
    private void GridToggle_Click(object s, RoutedEventArgs e) =>
        _vm?.ToggleGridCommand.Execute(null);

    // ── snap toggle (via ViewModel command) ──────────────────────────────────
    private void SnapToggle_Click(object s, RoutedEventArgs e) =>
        _vm?.ToggleSnapCommand.Execute(null);

    // ── middle-mouse pan ─────────────────────────────────────────────────────

    private void Scroller_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton != MouseButtonState.Pressed) return;
        _panActive        = true;
        _panOriginMouse   = _panLastMouse = e.GetPosition(Scroller);
        _panOriginScrollH = Scroller.HorizontalOffset;
        _panOriginScrollV = Scroller.VerticalOffset;
        Scroller.CaptureMouse();
        e.Handled = true;
    }

    private void Scroller_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panActive) return;
        var pos = e.GetPosition(Scroller);
        double vx = pos.X - _panLastMouse.X;
        double vy = pos.Y - _panLastMouse.Y;
        // TD-S14-2: velocity-based pan acceleration (1×–2.5× based on mouse speed)
        double speed = Math.Sqrt(vx * vx + vy * vy);
        double accel = 1.0 + Math.Min(speed / 10.0, 1.5);
        _panLastMouse = pos;
        Scroller.ScrollToHorizontalOffset(Math.Max(0, Scroller.HorizontalOffset - vx * accel));
        Scroller.ScrollToVerticalOffset  (Math.Max(0, Scroller.VerticalOffset   - vy * accel));
    }

    private void Scroller_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panActive || e.MiddleButton != MouseButtonState.Released) return;
        _panActive = false;
        Scroller.ReleaseMouseCapture();
        e.Handled = true;
    }

    // ── zoom (mouse wheel, centered on cursor) ───────────────────────────────
    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;   // prevent default scroll
        double factor  = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        double newPx   = Math.Max(0.5, Math.Min(12.0, _pxPerMm * factor));
        if (Math.Abs(newPx - _pxPerMm) < 0.005) return;

        // Canvas position under mouse (in logical pixels, before zoom change)
        var mouseInViewport = e.GetPosition(Scroller);
        double worldX = (Scroller.HorizontalOffset + mouseInViewport.X - PaperMarginPx) / _pxPerMm;
        double worldY = (Scroller.VerticalOffset   + mouseInViewport.Y - PaperMarginPx) / _pxPerMm;

        _pxPerMm = newPx;
        if (_vm != null) _vm.PixelsPerMm = newPx;
        if (ZoomLabel != null) ZoomLabel.Text = $"{_pxPerMm:F1} px/mm";
        RebuildAll();

        // Re-centre view on the same world point
        double newCx = worldX * newPx + PaperMarginPx;
        double newCy = worldY * newPx + PaperMarginPx;
        Scroller.ScrollToHorizontalOffset(Math.Max(0, newCx - mouseInViewport.X));
        Scroller.ScrollToVerticalOffset  (Math.Max(0, newCy - mouseInViewport.Y));
    }

    // ── rotate-by-point mode ──────────────────────────────────────────────────

    private void RotatePoint_Click(object s, RoutedEventArgs e)
    {
        _rotatePtActive = RotatePointBtn.IsChecked == true;
        // Mutual exclusion with edge-edit mode
        if (_rotatePtActive && _editModeActive)
        {
            _editModeActive = false;
            EditEdgesBtn.IsChecked = false;
            if (_hoveredEdgeLine != null) { RestoreEdgeStyle(_hoveredEdgeLine); _hoveredEdgeLine = null; }
        }
        RootCanvas.Cursor = _rotatePtActive ? Cursors.Arrow : null;
        if (_rotatePtActive)
            ShowVtxDots();
        else
        {
            ClearVtxDots();
            ResetRotatePhase();
        }
    }

    // ── vertex dot helpers ────────────────────────────────────────────────────

    private void ShowVtxDots()
    {
        ClearVtxDots();
        var pieces = _vm?.Pieces;
        if (pieces == null) return;
        // TD-S13-2: show dots for selected pieces only; fall back to all if none selected
        var targets = pieces.Where(p => p.IsSelected).ToList();
        if (targets.Count == 0) targets = [.. pieces];
        foreach (var piece in targets)
        {
            var seen = new HashSet<(int, int)>();
            foreach (var fd in piece.Faces)
                foreach (var v in new[] { fd.V0, fd.V1, fd.V2 })
                {
                    var key = ((int)Math.Round(v.X * 100), (int)Math.Round(v.Y * 100));
                    if (!seen.Add(key)) continue;
                    AddVtxDot(piece, v.X, v.Y);
                }
        }
    }

    private void AddVtxDot(PieceViewModel piece, double lx, double ly)
    {
        const double R = 5.0;
        var dot = new Ellipse
        {
            Width  = R * 2, Height = R * 2,
            Fill   = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            Stroke = new SolidColorBrush(Color.FromArgb(160, 180, 180, 180)),
            StrokeThickness  = 1,
            Cursor           = Cursors.Hand,
            IsHitTestVisible = true
        };
        dot.MouseEnter           += VtxDot_MouseEnter;
        dot.MouseLeave           += VtxDot_MouseLeave;
        dot.MouseLeftButtonDown  += VtxDot_Click;

        PositionVtxDot(dot, piece, lx, ly, R);
        Panel.SetZIndex(dot, 100);
        RootCanvas.Children.Add(dot);
        _vtxDots.Add((dot, piece, lx, ly));
    }

    private void PositionVtxDot(Ellipse dot, PieceViewModel piece, double lx, double ly, double r)
    {
        var (cx, cy) = VtxCanvasPx(piece, lx, ly);
        Canvas.SetLeft(dot, cx - r);
        Canvas.SetTop (dot, cy - r);
    }

    private (double cx, double cy) VtxCanvasPx(PieceViewModel piece, double lx, double ly)
    {
        double rotRad = piece.Rotation * Math.PI / 180.0;
        double cosR = Math.Cos(rotRad), sinR = Math.Sin(rotRad);
        double lpx = lx * _pxPerMm, lpy = ly * _pxPerMm;
        double cx = lpx * cosR - lpy * sinR + piece.PositionX * _pxPerMm + PaperMarginPx;
        double cy = lpx * sinR + lpy * cosR + piece.PositionY * _pxPerMm + PaperMarginPx;
        return (cx, cy);
    }

    private void UpdateVtxDotPositions(PieceViewModel piece)
    {
        const double R = 5.0;
        foreach (var (dot, p, lx, ly) in _vtxDots)
            if (p == piece) PositionVtxDot(dot, piece, lx, ly, R);
    }

    private void ClearVtxDots()
    {
        foreach (var (dot, _, _, _) in _vtxDots)
        {
            dot.MouseEnter          -= VtxDot_MouseEnter;
            dot.MouseLeave          -= VtxDot_MouseLeave;
            dot.MouseLeftButtonDown -= VtxDot_Click;
            RootCanvas.Children.Remove(dot);
        }
        _vtxDots.Clear();
        _pivotDot = _handleDot = null;
    }

    private void VtxDot_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Ellipse dot || dot == _pivotDot || dot == _handleDot) return;
        dot.Fill = new SolidColorBrush(Color.FromArgb(255, 255, 220, 80));
    }

    private void VtxDot_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not Ellipse dot || dot == _pivotDot || dot == _handleDot) return;
        dot.Fill = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
    }

    private static readonly Brush _dotRedBrush = HexBrush("#dc3232", "#dc3232"); // frozen (TD-S13-1)

    private void VtxDot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Ellipse dot) return;
        e.Handled = true;

        var entry = _vtxDots.FirstOrDefault(t => t.Dot == dot);
        if (entry == default) return;
        var (_, piece, lx, ly) = entry;

        if (_rotatePtPhase == 0)
        {
            // Select pivot
            _pivotDot = dot;
            dot.Fill  = _dotRedBrush;
            _rotPiece = piece;
            _pivotLx  = lx;  _pivotLy = ly;
            (_pivotCx, _pivotCy) = VtxCanvasPx(piece, lx, ly);
            _rotatePtPhase = 1;
        }
        else if (_rotatePtPhase == 1 && dot != _pivotDot)
        {
            // Select handle → start live rotation
            _handleDot = dot;
            dot.Fill   = _dotRedBrush;
            var (hcx, hcy) = VtxCanvasPx(piece, lx, ly);
            _handleAngle0   = Math.Atan2(hcy - _pivotCy, hcx - _pivotCx);
            _pieceRotation0 = piece.Rotation;
            // Capture pre-rotate state for undo
            _preDragPositions = _vm?.Pieces
                .ToDictionary(p => p.GroupId, p => (p.PositionX, p.PositionY, p.Rotation));
            _rotatePtPhase = 2;
        }
    }

    private void ResetRotatePhase()
    {
        const byte A = 210;
        var whiteFill = new SolidColorBrush(Color.FromArgb(A, 255, 255, 255));
        if (_pivotDot  != null) { _pivotDot.Fill  = whiteFill; _pivotDot  = null; }
        if (_handleDot != null) { _handleDot.Fill = whiteFill; _handleDot = null; }
        _rotatePtPhase    = 0;
        _rotPiece         = null;
        _preDragPositions = null;
    }

    // ── edge-edit mode ────────────────────────────────────────────────────────

    private void EditEdges_Click(object s, RoutedEventArgs e)
    {
        // Mutual exclusion with rotate-point mode
        if (EditEdgesBtn.IsChecked == true && _rotatePtActive)
        {
            _rotatePtActive = false;
            RotatePointBtn.IsChecked = false;
            ClearVtxDots();
            ResetRotatePhase();
            RootCanvas.Cursor = null;
        }
        _editModeActive = EditEdgesBtn.IsChecked == true;
        RootCanvas.Cursor = _editModeActive ? Cursors.Arrow : null;

        // Restore any currently-highlighted edge when turning mode off
        if (!_editModeActive && _hoveredEdgeLine != null)
        {
            RestoreEdgeStyle(_hoveredEdgeLine);
            _hoveredEdgeLine = null;
        }
    }

    private void Edge_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_editModeActive || sender is not Line hitLine) return;
        if (hitLine.Tag is not EdgeTag et) return;
        var visLine = et.VisualLine;
        _hoveredEdgeLine      = visLine;
        var hoverBrush        = HexBrush(_vm?.View2DSettings?.EdgeHoverColor, "#ffff9900");
        visLine.Stroke          = hoverBrush;
        visLine.StrokeThickness = 3.5;
        visLine.StrokeDashArray = null;
    }

    private void Edge_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not Line hitLine) return;
        if (hitLine.Tag is not EdgeTag et) return;
        var visLine = et.VisualLine;
        if (visLine == _hoveredEdgeLine) _hoveredEdgeLine = null;
        RestoreEdgeStyle(visLine);
    }

    private void Edge_LeftClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Line line || line.Tag is not EdgeTag et) return;
        if (_vm == null) return;

        // F4: double-click on any edge → snap piece so that edge aligns to H or V
        if (e.ClickCount == 2 && !_editModeActive)
        {
            AutoAlignEdge(et.PieceId, et.FaceId, et.EdgeIdx);
            e.Handled = true;
            return;
        }

        if (!_editModeActive) return;
        e.Handled = true;
        _hoveredEdgeLine = null;
        _vm.ToggleEdge(et.MeshEdgeId);
    }

    private void AutoAlignEdge(int pieceId, int faceId, int edgeIdx)
    {
        var piece = _vm?.Pieces.FirstOrDefault(p => p.GroupId == pieceId);
        if (piece == null) return;

        var fd = piece.Faces.FirstOrDefault(f => f.FaceId == faceId);
        if (fd == null) return;

        // Edge vector in local mm coords
        Point[] localPts = [fd.V0, fd.V1, fd.V2];
        var     lp0      = localPts[edgeIdx];
        var     lp1      = localPts[(edgeIdx + 1) % 3];
        double  dx       = lp1.X - lp0.X;
        double  dy       = lp1.Y - lp0.Y;

        // Current edge angle in canvas space (piece rotation already applied)
        double edgeAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI + piece.Rotation;

        // Snap to nearest multiple of 90°
        double snapped = Math.Round(edgeAngle / 90.0) * 90.0;
        double delta   = snapped - edgeAngle;

        // Capture pre-snap for undo
        var snap = _vm!.Pieces.ToDictionary(p => p.GroupId, p => (p.PositionX, p.PositionY, p.Rotation));
        piece.Rotation = (piece.Rotation + delta + 360) % 360;
        _vm.PushDragUndo(snap);
    }

    /// Restores a visual edge Line's stroke to its original fold/cut style from current settings.
    private void RestoreEdgeStyle(Line visLine)
    {
        if (visLine.Tag is not EdgeTag et) return;
        var s2d     = _vm?.View2DSettings;
        bool isFold = _vm?.IsEdgeFold(et.MeshEdgeId) ?? false;
        visLine.Stroke          = isFold
            ? HexBrush(s2d?.FoldLineColor, "#4169e1")
            : HexBrush(s2d?.CutLineColor,  "#ff0000");
        visLine.StrokeThickness = isFold ? (s2d?.FoldLineWidth ?? 0.8) : (s2d?.CutLineWidth ?? 1.0);
        visLine.StrokeDashArray = isFold ? ParseDash(s2d?.FoldLineDash ?? "4,2") : null;
    }

    // ── lasso helper ─────────────────────────────────────────────────────────

    /// Returns the axis-aligned bounding box of a piece in RootCanvas pixel coordinates.
    private Rect PieceBoundsCanvas(PieceViewModel piece)
    {
        if (piece.Faces.Length == 0) return Rect.Empty;
        var allLx = piece.Faces.SelectMany(f => new[] { f.V0.X, f.V1.X, f.V2.X });
        var allLy = piece.Faces.SelectMany(f => new[] { f.V0.Y, f.V1.Y, f.V2.Y });
        double lMinX = allLx.Min(), lMaxX = allLx.Max();
        double lMinY = allLy.Min(), lMaxY = allLy.Max();

        double rotRad = piece.Rotation * Math.PI / 180.0;
        double cosR = Math.Cos(rotRad), sinR = Math.Sin(rotRad);
        double[] cx = new double[4], cy = new double[4];
        int i = 0;
        foreach (var (lx, ly) in new[] {(lMinX,lMinY),(lMaxX,lMinY),(lMinX,lMaxY),(lMaxX,lMaxY)})
        {
            double px = lx * _pxPerMm, py = ly * _pxPerMm;
            cx[i] = px * cosR - py * sinR + piece.PositionX * _pxPerMm + PaperMarginPx;
            cy[i] = px * sinR + py * cosR + piece.PositionY * _pxPerMm + PaperMarginPx;
            i++;
        }
        double minX = cx.Min(), minY = cy.Min();
        return new Rect(minX, minY, cx.Max() - minX, cy.Max() - minY);
    }

    // TD-S14-1: accurate lasso selection — check if any canvas-space vertex falls within the lasso
    private bool AnyVertexInLasso(PieceViewModel p, Rect lasso)
    {
        if (p.Faces.Length == 0) return false;
        double rotRad = p.Rotation * Math.PI / 180.0;
        double cosR = Math.Cos(rotRad), sinR = Math.Sin(rotRad);
        var seenKeys = new HashSet<(int, int)>();
        foreach (var fd in p.Faces)
            foreach (var v in new[] { fd.V0, fd.V1, fd.V2 })
            {
                var key = ((int)Math.Round(v.X * 1000), (int)Math.Round(v.Y * 1000));
                if (!seenKeys.Add(key)) continue;
                double px = v.X * _pxPerMm, py = v.Y * _pxPerMm;
                double cx = px * cosR - py * sinR + p.PositionX * _pxPerMm + PaperMarginPx;
                double cy = px * sinR + py * cosR + p.PositionY * _pxPerMm + PaperMarginPx;
                if (lasso.Contains(cx, cy)) return true;
            }
        return false;
    }

    // ── affine UV texture mapping ─────────────────────────────────────────────
    /// <summary>
    /// Builds an ImageBrush that correctly UV-maps the texture onto one triangle.
    ///
    /// Strategy: use a unit Viewport (0,0,1,1) + Stretch=Fill so the full image
    /// is represented in the [0,1]² UV space — DPI-agnostic.  The MatrixTransform M
    /// maps flipped-UV coordinates to canvas-local pixels so each vertex samples
    /// the correct texel.  Cramer's rule solves the 3-point affine system.
    ///
    /// WPF semantics: canvas pixel P samples the brush at M⁻¹(P), so M must map
    /// source (UV) → destination (canvas pixel):  M(uv_i) = canvas_pixel_i.
    /// </summary>
    private static Brush? BuildTextureBrush(
        BitmapImage texture,
        System.Windows.Point p0, System.Windows.Point p1, System.Windows.Point p2,
        Vector2 uv0, Vector2 uv1, Vector2 uv2)
    {
        if (texture.PixelWidth < 1 || texture.PixelHeight < 1) return null;

        // Source coords: Y-flipped UV [0,1] (matches WPF/3D: OBJ v=0 is bottom, image y=0 is top)
        double su0 = uv0.X, sv0 = 1.0 - uv0.Y;
        double su1 = uv1.X, sv1 = 1.0 - uv1.Y;
        double su2 = uv2.X, sv2 = 1.0 - uv2.Y;

        // Determinant of source (UV) triangle — guard against degenerate mapping
        double det = su0 * (sv1 - sv2) + su1 * (sv2 - sv0) + su2 * (sv0 - sv1);
        if (Math.Abs(det) < 1e-9) return null;

        double p0x = p0.X, p0y = p0.Y;
        double p1x = p1.X, p1y = p1.Y;
        double p2x = p2.X, p2y = p2.Y;

        // Solve M(su,sv) = (px,py) via Cramer's rule
        // WPF Matrix: x' = m11*x + m21*y + offX,  y' = m12*x + m22*y + offY
        double m11  = (p0x*(sv1-sv2) + p1x*(sv2-sv0) + p2x*(sv0-sv1)) / det;
        double m21  = (su0*(p1x-p2x) + su1*(p2x-p0x) + su2*(p0x-p1x)) / det;
        double offX = (su0*(sv1*p2x-sv2*p1x) + su1*(sv2*p0x-sv0*p2x) + su2*(sv0*p1x-sv1*p0x)) / det;

        double m12  = (p0y*(sv1-sv2) + p1y*(sv2-sv0) + p2y*(sv0-sv1)) / det;
        double m22  = (su0*(p1y-p2y) + su1*(p2y-p0y) + su2*(p0y-p1y)) / det;
        double offY = (su0*(sv1*p2y-sv2*p1y) + su1*(sv2*p0y-sv0*p2y) + su2*(sv0*p1y-sv1*p0y)) / det;

        // Unit viewport (0,0,1,1) + Stretch.Fill: DPI-agnostic mapping of full image to UV [0,1]²
        return new ImageBrush(texture)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport      = new Rect(0, 0, 1, 1),
            TileMode      = TileMode.None,
            Stretch       = Stretch.Fill,
            Transform     = new MatrixTransform(m11, m12, m21, m22, offX, offY)
        };
    }

    // ── color/dash helpers ────────────────────────────────────────────────────
    private static Brush HexBrush(string? hex, string fallback)
    {
        try
        {
            var src   = string.IsNullOrEmpty(hex) ? fallback : hex;
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(src);
            var b     = new SolidColorBrush(color); b.Freeze(); return b;
        }
        catch { return Brushes.Transparent; }
    }

    private static DoubleCollection? ParseDash(string dashStr)
    {
        if (string.IsNullOrEmpty(dashStr) ||
            dashStr.Equals("Solid", StringComparison.OrdinalIgnoreCase))
            return null;
        try { return new DoubleCollection(dashStr.Split(',').Select(double.Parse)); }
        catch { return new DoubleCollection([4, 2]); }
    }

    // Scale a WPF Point by the current pixels-per-mm factor
    private Point Sc(Point p) => new(p.X * _pxPerMm, p.Y * _pxPerMm);

    // ── piece outline merging ─────────────────────────────────────────────────

    /// Computes the boundary polygon of a piece by collecting all non-fold edges,
    /// chaining them into an ordered polygon path.  Returns null for degenerate pieces.
    private static PointCollection? BuildPieceOutline(PieceViewModel piece, double pxPerMm)
    {
        if (piece.Faces.Length == 0) return null;

        // Collect all non-fold edges as (p0, p1) pairs in local mm coords
        var edges = new List<(Point A, Point B)>();
        var seen  = new HashSet<int>();

        foreach (var fd in piece.Faces)
        {
            Point[] verts = [fd.V0, fd.V1, fd.V2];
            for (int i = 0; i < 3; i++)
            {
                if (fd.EdgeIsFold[i]) continue; // internal fold edge — skip
                int meshId = fd.MeshEdgeIds[i];
                if (!seen.Add(meshId)) continue; // already added this boundary edge
                edges.Add((verts[i], verts[(i + 1) % 3]));
            }
        }

        if (edges.Count == 0) return null;

        // Chain edges into an ordered polygon
        var polygon = new List<Point>();
        var remaining = new List<(Point A, Point B)>(edges);

        polygon.Add(remaining[0].A);
        polygon.Add(remaining[0].B);
        remaining.RemoveAt(0);

        const double SnapDist = 0.001; // mm tolerance

        for (int safety = 0; safety < edges.Count && remaining.Count > 0; safety++)
        {
            var tail = polygon[^1];
            bool found = false;

            for (int k = 0; k < remaining.Count; k++)
            {
                var (a, b) = remaining[k];
                if (Near(tail, a, SnapDist)) { polygon.Add(b); remaining.RemoveAt(k); found = true; break; }
                if (Near(tail, b, SnapDist)) { polygon.Add(a); remaining.RemoveAt(k); found = true; break; }
            }

            if (!found) break; // open boundary (non-manifold mesh) — stop here
        }

        return new PointCollection(polygon.Select(p => new Point(p.X * pxPerMm, p.Y * pxPerMm)));
    }

    private static bool Near(Point a, Point b, double eps)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy < eps * eps;
    }
}
