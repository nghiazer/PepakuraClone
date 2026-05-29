using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using FourHUnfolder.App.ViewModels;
using FourHUnfolder.Domain.Models;
using HelixToolkit.Wpf;

namespace FourHUnfolder.App.Dialogs;

public partial class ModelOrientationDialog : Window
{
    public ModelOrientationViewModel Result { get; }

    /// <summary>True = user clicked OK; False = user clicked Skip (no transform applied).</summary>
    public bool Applied { get; private set; }

    /// <summary>TD-25-1: True if the user checked "Don't ask again" before closing.</summary>
    public bool DontAskAgain => ChkDontAskAgain.IsChecked == true;

    // ── live preview state ────────────────────────────────────────────────────
    private readonly MatrixTransform3D _liveTransform = new(Matrix3D.Identity);
    private readonly ModelVisual3D     _meshVisual    = new();

    public ModelOrientationDialog(Mesh? mesh = null)
    {
        Result      = new ModelOrientationViewModel();
        DataContext = Result;
        InitializeComponent();

        AddAxisLines();

        if (mesh != null)
            BuildMeshPreview(mesh);

        // TD-27-2: update axis label screen positions whenever camera moves
        CubeViewport.CameraChanged += (_, _) => UpdateAxisLabelPositions();

        // Live-update the preview transform whenever Up or Front axis changes
        Result.PropertyChanged += (_, _) => UpdateLiveTransform();
    }

    // ── axis lines ────────────────────────────────────────────────────────────

    private void AddAxisLines()
    {
        AddAxis(new Point3D(0, 0, 0), new Point3D(1.6, 0,   0  ), Color.FromRgb(0xFF, 0x55, 0x55)); // +X red
        AddAxis(new Point3D(0, 0, 0), new Point3D(0,   1.6, 0  ), Color.FromRgb(0x44, 0xCC, 0x44)); // +Y green
        AddAxis(new Point3D(0, 0, 0), new Point3D(0,   0,   1.6), Color.FromRgb(0x55, 0x99, 0xFF)); // +Z blue
    }

    private void AddAxis(Point3D from, Point3D to, Color color)
    {
        var lines = new LinesVisual3D
        {
            Color     = color,
            Thickness = 2.5,
            Points    = new Point3DCollection { from, to }
        };
        CubeViewport.Children.Add(lines);
    }

    // ── mesh preview ──────────────────────────────────────────────────────────

    private void BuildMeshPreview(Mesh mesh)
    {
        if (mesh.Vertices.Count == 0 || mesh.Faces.Count == 0) return;

        // Compute axis-aligned bounding box
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        foreach (var v in mesh.Vertices)
        {
            if (v.Position.X < minX) minX = v.Position.X;
            if (v.Position.Y < minY) minY = v.Position.Y;
            if (v.Position.Z < minZ) minZ = v.Position.Z;
            if (v.Position.X > maxX) maxX = v.Position.X;
            if (v.Position.Y > maxY) maxY = v.Position.Y;
            if (v.Position.Z > maxZ) maxZ = v.Position.Z;
        }

        var cx    = (minX + maxX) / 2f;
        var cy    = (minY + maxY) / 2f;
        var cz    = (minZ + maxZ) / 2f;
        var range = Math.Max(Math.Max(maxX - minX, maxY - minY), maxZ - minZ);
        var scale = range > 0 ? 2.0 / range : 1.0;   // normalize to ~2-unit cube

        // Build WPF mesh geometry (normalized & centered)
        var positions = new Point3DCollection(mesh.Vertices.Count);
        foreach (var v in mesh.Vertices)
            positions.Add(new Point3D(
                (v.Position.X - cx) * scale,
                (v.Position.Y - cy) * scale,
                (v.Position.Z - cz) * scale));

        var indices = new Int32Collection(mesh.Faces.Count * 3);
        foreach (var f in mesh.Faces)
        {
            indices.Add(f.A);
            indices.Add(f.B);
            indices.Add(f.C);
        }

        var geo   = new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
        var model = new GeometryModel3D
        {
            Geometry     = geo,
            Material     = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x5A, 0x9A, 0xD8))),
            BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x2A, 0x55, 0x88)))
        };

        _meshVisual.Content   = model;
        _meshVisual.Transform = _liveTransform;
        CubeViewport.Children.Add(_meshVisual);

        // TD-27-1: auto-fit camera after first layout pass
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
            () =>
            {
                CubeViewport.ZoomExtents(0);
                UpdateAxisLabelPositions();   // labels need final layout to project correctly
            });

        // Apply current selection as starting state
        UpdateLiveTransform();
    }

    // ── live transform (mesh rotates when user changes axis) ──────────────────

    private void UpdateLiveTransform()
    {
        var rot = Result.ComputeRotation();
        _liveTransform.Matrix = new Matrix3D(
            rot.M11, rot.M12, rot.M13, rot.M14,
            rot.M21, rot.M22, rot.M23, rot.M24,
            rot.M31, rot.M32, rot.M33, rot.M34,
            rot.M41, rot.M42, rot.M43, rot.M44);
    }

    // ── TD-27-2: axis label positioning ──────────────────────────────────────

    private void UpdateAxisLabelPositions()
    {
        // The axis lines end at ±1.6 in model space; labels sit at 1.7 to clear the tip
        PlaceLabel(LabelX, new Point3D(1.7, 0,   0  ));
        PlaceLabel(LabelY, new Point3D(0,   1.7, 0  ));
        PlaceLabel(LabelZ, new Point3D(0,   0,   1.7));
    }

    private void PlaceLabel(System.Windows.Controls.TextBlock lbl, Point3D worldPt)
    {
        var screen = Viewport3DHelper.Point3DtoPoint2D(CubeViewport.Viewport, worldPt);
        if (double.IsNaN(screen.X) || double.IsNaN(screen.Y)) return;
        Canvas.SetLeft(lbl, screen.X - 4);
        Canvas.SetTop (lbl, screen.Y - 9);
    }

    // ── button handlers ───────────────────────────────────────────────────────

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        Applied      = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Applied      = false;
        DialogResult = false;
    }
}
