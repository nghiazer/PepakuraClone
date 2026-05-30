using System.Windows;
using System.Windows.Media.Media3D;
using FourHUnfolder.App.ViewModels;

namespace FourHUnfolder.App.Dialogs;

/// <summary>
/// Code-behind for the Assembly Animation window.
/// All logic lives in <see cref="AssemblyViewModel"/>; this file only wires
/// the window lifetime to the VM's Dispose and sets the initial camera.
/// </summary>
public partial class AssemblyAnimationWindow : Window
{
    private readonly AssemblyViewModel _vm;

    public AssemblyAnimationWindow(AssemblyViewModel vm)
    {
        InitializeComponent();
        _vm        = vm;
        DataContext = vm;
        Closed     += OnWindowClosed;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var (pos, lookDir) = _vm.CameraHint;
        Viewport3D.Camera = new PerspectiveCamera
        {
            Position          = pos,
            LookDirection     = lookDir,
            UpDirection       = new Vector3D(0, 1, 0),
            FieldOfView       = 40,
            NearPlaneDistance = 0.01,
            FarPlaneDistance  = 100_000
        };
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _vm.Dispose();   // stop the DispatcherTimer
    }
}
