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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainViewModel>();
        Loaded     += OnLoaded;
        Vm.PropertyChanged += OnVmPropertyChanged;
    }

    // ── startup ───────────────────────────────────────────────────────────────

    private void OnLoaded(object s, RoutedEventArgs e) => ApplyCameraSettings();

    private void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CameraSettingsVersion))
            ApplyCameraSettings();
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

        int faceId = HitTestFace(e.GetPosition(Viewport3D.Viewport));
        if (faceId >= 0)
            Vm.SelectFace3D(faceId);
        else
            Vm.ClearSelection();   // click on empty 3D space → deselect
        // Do NOT set e.Handled — let HelixToolkit keep orbit control
    }

    private void Viewport3D_PreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        if (!Vm.IsUnfolded) return;

        int faceId = HitTestFace(e.GetPosition(Viewport3D.Viewport));
        if (faceId < 0) return;

        Vm.SelectFace3D(faceId);
        ShowFaceContextMenu(faceId);
        e.Handled = true;   // prevent HelixToolkit from processing right-click
    }

    private void ShowFaceContextMenu(int faceId)
    {
        var menu = new ContextMenu();

        // ── header (non-clickable info) ──────────────────────────────────────
        menu.Items.Add(new MenuItem
        {
            Header = $"Face #{faceId}",
            IsEnabled = false,
            FontWeight = FontWeights.Bold
        });
        menu.Items.Add(new Separator());

        // ── Detach this face ─────────────────────────────────────────────────
        var detachFace = new MenuItem { Header = "✂  Detach this face" };
        detachFace.Click += (_, _) => Vm.DetachFace(faceId);
        menu.Items.Add(detachFace);

        // ── Detach whole piece ────────────────────────────────────────────────
        var detachPiece = new MenuItem { Header = "✂✂ Detach entire piece" };
        detachPiece.Click += (_, _) => Vm.DetachPiece(faceId);
        menu.Items.Add(detachPiece);

        // ── Attach to cut neighbors ───────────────────────────────────────────
        var neighbors = Vm.GetCutNeighbors(faceId).ToList();
        if (neighbors.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (int nb in neighbors)
            {
                var nbCapture = nb;
                var item = new MenuItem { Header = $"🔗  Attach to face {nb}" };
                item.Click += (_, _) => Vm.AttachFaces(faceId, nbCapture);
                menu.Items.Add(item);
            }
        }

        menu.IsOpen = true;
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

        // Each face occupies exactly 3 consecutive positions (unshared vertices).
        // The smallest vertex index of the three gives the face base index.
        int minVert = Math.Min(hit.VertexIndex1,
                     Math.Min(hit.VertexIndex2, hit.VertexIndex3));
        return minVert / 3;
    }
}
