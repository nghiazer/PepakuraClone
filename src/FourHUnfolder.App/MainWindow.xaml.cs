using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Extensions.DependencyInjection;
using FourHUnfolder.App.Dialogs;
using FourHUnfolder.App.ViewModels;

namespace FourHUnfolder.App;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;
    private TextureDialog? _textureDialog;

    // ── Feature B: 3D edge hover state ────────────────────────────────────────
    private int   _hoveredEdgeId = -1;
    private Point _rmbDownPos;
    private const double HoverThresholdPx = 8.0;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainViewModel>();
        Loaded     += OnLoaded;
        Closing    += OnClosing;
        Vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!Vm.ConfirmDiscardIfDirty("exit"))
            e.Cancel = true;
    }

    // ── startup ───────────────────────────────────────────────────────────────

    private void OnLoaded(object s, RoutedEventArgs e) => ApplyCameraSettings();

    private void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CameraSettingsVersion))
            ApplyCameraSettings();

        // When mesh changes (new unfold or load), reset hover state
        if (e.PropertyName is nameof(MainViewModel.PiecesVersion)
                           or nameof(MainViewModel.IsUnfolded)
                           or nameof(MainViewModel.CurrentMesh))
        {
            _hoveredEdgeId = -1;
        }
    }

    private void ApplyCameraSettings()
    {
        if (MainCamera3D == null) return;
        MainCamera3D.FieldOfView      = Vm.CameraFOV;
        MainCamera3D.NearPlaneDistance = Vm.CameraNearPlane;
        MainCamera3D.FarPlaneDistance  = Vm.CameraFarPlane;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  3-D PICKING
    // ══════════════════════════════════════════════════════════════════════════

    private void Viewport3D_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (!Vm.IsUnfolded) return;
        if (_hoveredEdgeId >= 0) return;  // LMB on edge is handled by PreviewLMBDown

        int faceId = HitTestFace(e.GetPosition(Viewport3D.Viewport));
        if (faceId >= 0)
            Vm.SelectFace3D(faceId);
        else
            Vm.ClearSelection();
        // Do NOT set e.Handled — let HelixToolkit keep orbit control
    }

    // ── Feature B: Left-click on hovered edge = toggle fold/cut ──────────────

    private void Viewport3D_PreviewLMBDown(object sender, MouseButtonEventArgs e)
    {
        if (_hoveredEdgeId < 0) return;
        if (!Vm.IsUnfolded) return;

        Vm.ToggleEdge(_hoveredEdgeId);
        _hoveredEdgeId = -1;       // reset hover (edge type just changed)
        Vm.ClearEdgeHover();
        e.Handled = true;          // prevent orbit from starting
    }

    // ── Feature B: Right-click = set rotation pivot (click, not drag) ────────

    private void Viewport3D_RMBDown(object sender, MouseButtonEventArgs e)
    {
        _rmbDownPos = e.GetPosition(Viewport3D);
        // Do NOT handle — let HelixToolkit start its orbit/pan tracking
    }

    private void Viewport3D_RMBUp(object sender, MouseButtonEventArgs e)
    {
        if (!Vm.IsUnfolded) return;

        var upPos     = e.GetPosition(Viewport3D);
        double drag   = (upPos - _rmbDownPos).Length;
        if (drag >= 4.0) return;   // was a drag, not a click

        // Set camera pivot to the 3D point under the cursor
        var hitPos = e.GetPosition(Viewport3D.Viewport);
        if (Viewport3D.FindNearest(hitPos, out Point3D hitPt, out _, out _))
        {
            Viewport3D.LookAt(hitPt, 300);   // 300 ms animation
            e.Handled = true;
        }
    }

    // ── Feature B: Mouse move = edge hover detection ──────────────────────────

    private void Viewport3D_MouseMove(object sender, MouseEventArgs e)
    {
        if (!Vm.IsUnfolded) return;

        var mousePos  = e.GetPosition(Viewport3D);
        int newHover  = FindNearestEdge(mousePos, HoverThresholdPx);

        if (newHover == _hoveredEdgeId) return;   // unchanged

        _hoveredEdgeId = newHover;
        if (newHover < 0)
            Vm.ClearEdgeHover();
        else
            Vm.HoverEdge(newHover, Vm.IsEdgeFold(newHover));
    }

    /// <summary>
    /// Projects all inner mesh edges to screen space and returns the one
    /// closest to <paramref name="mousePos"/> within <paramref name="threshold"/> pixels.
    /// Returns -1 if no edge is close enough.
    /// </summary>
    private int FindNearestEdge(Point mousePos, double threshold)
    {
        var mesh = Vm.CurrentMesh;
        if (mesh == null) return -1;

        int    bestEdge = -1;
        double bestDist = threshold;

        foreach (var edge in mesh.Edges)
        {
            if (!edge.ConnectsFaces) continue;   // boundary edge — skip

            var pa = mesh.Vertices[edge.V1].Position;
            var pb = mesh.Vertices[edge.V2].Position;

            var screenA = ProjectToScreen(new Point3D(pa.X, pa.Y, pa.Z));
            var screenB = ProjectToScreen(new Point3D(pb.X, pb.Y, pb.Z));

            if (double.IsNaN(screenA.X) || double.IsNaN(screenB.X)) continue;

            double d = DistPointToSegment(mousePos, screenA, screenB);
            if (d < bestDist)
            {
                bestDist = d;
                bestEdge = edge.Id;
            }
        }

        return bestEdge;
    }

    /// <summary>
    /// Projects a world-space 3D point to 2D screen coordinates using the
    /// PerspectiveCamera's current view and projection.
    /// Returns NaN,NaN if the point is behind the camera.
    /// </summary>
    private Point ProjectToScreen(Point3D worldPt)
    {
        var cam = MainCamera3D;
        if (cam == null) return new Point(double.NaN, double.NaN);

        double w = Viewport3D.ActualWidth;
        double h = Viewport3D.ActualHeight;
        if (w <= 0 || h <= 0) return new Point(double.NaN, double.NaN);

        var lookDir = cam.LookDirection;  lookDir.Normalize();
        var upDir   = cam.UpDirection;    upDir.Normalize();

        var rightDir = Vector3D.CrossProduct(lookDir, upDir);
        rightDir.Normalize();
        var trueUp = Vector3D.CrossProduct(rightDir, lookDir);

        var diff   = worldPt - cam.Position;
        double xCam = Vector3D.DotProduct(diff, rightDir);
        double yCam = Vector3D.DotProduct(diff, trueUp);
        double zCam = Vector3D.DotProduct(diff, lookDir);

        if (zCam <= 0) return new Point(double.NaN, double.NaN);   // behind camera

        double fovRad     = cam.FieldOfView * Math.PI / 180.0;
        double tanHalfFov = Math.Tan(fovRad / 2.0);
        double aspect     = w / h;

        double ndcX = xCam / (zCam * tanHalfFov * aspect);
        double ndcY = yCam / (zCam * tanHalfFov);

        return new Point(
            (ndcX + 1.0) / 2.0 * w,
            (1.0 - ndcY) / 2.0 * h);
    }

    /// Minimum distance from point <paramref name="p"/> to line segment AB.
    private static double DistPointToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-10)
        {
            double ex = p.X - a.X, ey = p.Y - a.Y;
            return Math.Sqrt(ex * ex + ey * ey);
        }
        double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq));
        double projX = a.X + t * dx, projY = a.Y + t * dy;
        double fx = p.X - projX, fy = p.Y - projY;
        return Math.Sqrt(fx * fx + fy * fy);
    }

    // ── texture dialog ────────────────────────────────────────────────────────

    private void TextureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_textureDialog == null || !_textureDialog.IsLoaded)
        {
            _textureDialog = new TextureDialog { Owner = this, DataContext = DataContext };
            _textureDialog.Closed += (_, _) => _textureDialog = null;
            _textureDialog.Show();
        }
        else
        {
            _textureDialog.Activate();
        }
    }

    /// <returns>Face index (>= 0) or -1 if nothing was hit.</returns>
    private int HitTestFace(Point pos)
    {
        RayMeshGeometry3DHitTestResult? hit = null;

        VisualTreeHelper.HitTest(
            Viewport3D.Viewport,
            null,
            r =>
            {
                if (r is RayMeshGeometry3DHitTestResult m) hit = m;
                return HitTestResultBehavior.Stop;
            },
            new PointHitTestParameters(pos));

        if (hit == null) return -1;

        return Vm.ResolveHitFaceId(hit.MeshHit, hit.VertexIndex1, hit.VertexIndex2, hit.VertexIndex3);
    }
}
