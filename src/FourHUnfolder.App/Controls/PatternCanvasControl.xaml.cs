using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using FourHUnfolder.App.ViewModels;
using FourHUnfolder.Domain.Models;

namespace FourHUnfolder.App.Controls;

/// <summary>
/// Interactive 2-D layout canvas.
/// DataContext must be set to MainViewModel.
/// </summary>
public partial class PatternCanvasControl : UserControl
{
    // ── constants ────────────────────────────────────────────────────────────
    private const double PaperMarginPx = 30.0;
    private const string GridTag       = "GRID";

    // ── state ────────────────────────────────────────────────────────────────
    private MainViewModel?              _vm;
    private double                      _pxPerMm = 3.0;
    private readonly Dictionary<int, Canvas> _containers = new();

    // TD-4 fix: explicit subscription tracking to prevent memory leaks
    private readonly Dictionary<PieceViewModel, PropertyChangedEventHandler> _pieceHandlers = new();

    // drag state
    private PieceViewModel? _dragging;
    private Point           _dragOriginMouse;
    private double          _dragOriginX, _dragOriginY;

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
        }

        _vm = e.NewValue as MainViewModel;
        if (_vm == null) return;

        _pxPerMm = _vm.PixelsPerMm;
        ZoomSlider.Value = _pxPerMm;

        _vm.Pieces.CollectionChanged += OnPiecesChanged;
        _vm.PropertyChanged          += OnVmPropertyChanged;

        RebuildAll();
    }

    private void OnPiecesChanged(object? s, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.Invoke(RebuildAll);

    private void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // Fast-path: just toggle grid line visibility without full rebuild
            case nameof(MainViewModel.GridVisible):
                if (_vm != null) Dispatcher.Invoke(() => ApplyGridVisibility(_vm.GridVisible));
                break;

            // Settings that affect piece rendering need full rebuild
            case nameof(MainViewModel.PaperSizeModel):
            case nameof(MainViewModel.PixelsPerMm):
            case nameof(MainViewModel.View2DSettings):
                Dispatcher.Invoke(RebuildAll);
                break;
        }
    }

    // ── full rebuild ─────────────────────────────────────────────────────────
    private void RebuildAll()
    {
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
    }

    // ── paper background ─────────────────────────────────────────────────────
    private void DrawPaper(PaperSizeModel paper)
    {
        var s2d = _vm?.View2DSettings;
        double pw = paper.WidthMm  * _pxPerMm;
        double ph = paper.HeightMm * _pxPerMm;

        RootCanvas.Background = HexBrush(s2d?.CanvasBackground, "#3a3a5a");

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
        Canvas.SetLeft(rect, PaperMarginPx);
        Canvas.SetTop (rect, PaperMarginPx);
        Panel.SetZIndex(rect, 0);
        RootCanvas.Children.Add(rect);

        // Paper dimension label (unit-aware via MainViewModel)
        string labelText = _vm != null
            ? $"{paper.Name}  ({_vm.FormatMm(paper.WidthMm)} × {_vm.FormatMm(paper.HeightMm)})"
            : paper.ToString();
        var lbl = new TextBlock
        {
            Text       = labelText,
            Foreground = Brushes.Gray,
            FontSize   = 10
        };
        Canvas.SetLeft(lbl, PaperMarginPx);
        Canvas.SetTop (lbl, PaperMarginPx - 16);
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
                X1 = PaperMarginPx + x, Y1 = PaperMarginPx,
                X2 = PaperMarginPx + x, Y2 = PaperMarginPx + ph,
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
                X1 = PaperMarginPx,      Y1 = PaperMarginPx + y,
                X2 = PaperMarginPx + pw, Y2 = PaperMarginPx + y,
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
            if (ev.PropertyName is nameof(PieceViewModel.IsSelected))
                Dispatcher.Invoke(() => RenderPieceShapes(container, piece));
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

        var faceFill = HexBrush(s2d?.FaceFillColor, "#c8fffde2");
        var selFill  = new SolidColorBrush(Color.FromArgb(180, 160, 210, 255));
        var fill     = piece.IsSelected ? selFill : faceFill;

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

                var line = new Line
                {
                    X1 = verts[i].X,           Y1 = verts[i].Y,
                    X2 = verts[(i + 1) % 3].X, Y2 = verts[(i + 1) % 3].Y,
                    Stroke          = stroke,
                    StrokeThickness = thickness,
                    StrokeDashArray = dash,
                    Cursor          = isBoundary ? Cursors.Arrow : Cursors.Hand,
                    Tag             = (PieceId: piece.GroupId, FaceId: fd.FaceId,
                                       EdgeIdx: i, MeshEdgeId: meshEdgeId)
                };
                if (!isBoundary) line.MouseRightButtonDown += Edge_RightClick;
                container.Children.Add(line);
            }
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

    private void UpdateCanvasSize()
    {
        if (_vm == null) return;
        double pw = _vm.PaperSizeModel.WidthMm  * _pxPerMm + PaperMarginPx * 2;
        double ph = _vm.PaperSizeModel.HeightMm * _pxPerMm + PaperMarginPx * 2;
        RootCanvas.Width  = Math.Max(pw, 400);
        RootCanvas.Height = Math.Max(ph, 400);
    }

    // ── drag handling ────────────────────────────────────────────────────────
    private void Piece_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Canvas c || c.Tag is not PieceViewModel piece) return;

        if (_vm != null)
        {
            foreach (var p in _vm.Pieces)
                p.IsSelected = (p == piece);
            _vm.SelectPiece2D(piece.GroupId);
        }

        _dragging        = piece;
        _dragOriginMouse = e.GetPosition(RootCanvas);
        _dragOriginX     = piece.PositionX;
        _dragOriginY     = piece.PositionY;
        RootCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
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

        _dragging.PositionX = newX;
        _dragging.PositionY = newY;
        SyncTransform(_dragging);
    }

    private void Canvas_MouseUp(object sender, MouseEventArgs e)
    {
        if (_dragging == null) return;
        RootCanvas.ReleaseMouseCapture();
        _dragging = null;
    }

    // ── edge right-click context menu ────────────────────────────────────────
    private void Edge_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Line line) return;
        if (line.Tag is not (int pieceId, int faceId, int edgeIdx, int meshEdgeId)) return;
        if (_vm == null) return;

        var isFold = _vm.IsEdgeFold(meshEdgeId);
        var menu   = new ContextMenu();

        if (isFold)
        {
            var split = new MenuItem { Header = "✂  Split piece here (Fold → Cut)" };
            split.Click += (_, _) => _vm.ToggleEdge(meshEdgeId);
            menu.Items.Add(split);
        }
        else
        {
            var join = new MenuItem { Header = "🔗  Join pieces here (Cut → Fold)" };
            join.Click += (_, _) => _vm.ToggleEdge(meshEdgeId);
            menu.Items.Add(join);
        }

        menu.IsOpen = true;
        e.Handled   = true;
    }

    // ── toolbar buttons ──────────────────────────────────────────────────────
    private void RotateCW_Click (object s, RoutedEventArgs e) => RotateSelected(+90);
    private void RotateCCW_Click(object s, RoutedEventArgs e) => RotateSelected(-90);

    private void FlipH_Click(object s, RoutedEventArgs e)
    {
        var piece = _vm?.Pieces.FirstOrDefault(p => p.IsSelected);
        if (piece == null) return;
        piece.Rotation = (180 - piece.Rotation + 360) % 360;
        SyncTransform(piece);
    }

    private void RotateSelected(double delta)
    {
        var piece = _vm?.Pieces.FirstOrDefault(p => p.IsSelected);
        if (piece == null) return;
        piece.Rotation = (piece.Rotation + delta + 360) % 360;
        SyncTransform(piece);
    }

    private void AutoArrange_Click(object s, RoutedEventArgs e)
    {
        if (_vm == null) return;

        double gap    = _vm.View2DSettings?.PieceGapMm ?? 5.0;
        double paperW = _vm.PaperSizeModel.WidthMm;
        double curX   = gap, curY = gap, rowH = 0;

        foreach (var piece in _vm.Pieces)
        {
            if (piece.Faces.Length == 0) continue;

            var allX = piece.Faces.SelectMany(f => new[] { f.V0.X, f.V1.X, f.V2.X });
            var allY = piece.Faces.SelectMany(f => new[] { f.V0.Y, f.V1.Y, f.V2.Y });
            double pw = allX.Max() - allX.Min() + gap;
            double ph = allY.Max() - allY.Min() + gap;

            if (curX + pw > paperW) { curX = gap; curY += rowH + gap; rowH = 0; }

            piece.PositionX = curX + pw / 2;
            piece.PositionY = curY + ph / 2;
            curX += pw + gap;
            rowH  = Math.Max(rowH, ph);
            SyncTransform(piece);
        }
    }

    // ── grid toggle (via ViewModel command) ──────────────────────────────────
    private void GridToggle_Click(object s, RoutedEventArgs e) =>
        _vm?.ToggleGridCommand.Execute(null);

    // ── snap toggle (via ViewModel command) ──────────────────────────────────
    private void SnapToggle_Click(object s, RoutedEventArgs e) =>
        _vm?.ToggleSnapCommand.Execute(null);

    // ── zoom ─────────────────────────────────────────────────────────────────
    private void Zoom_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        _pxPerMm = e.NewValue;
        ZoomLabel.Text = $"{_pxPerMm:F1} px/mm";
        if (_vm != null) { _vm.PixelsPerMm = _pxPerMm; RebuildAll(); }
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
}
