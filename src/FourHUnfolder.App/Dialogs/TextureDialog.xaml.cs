using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using FourHUnfolder.App.ViewModels;

namespace FourHUnfolder.App.Dialogs;

public partial class TextureDialog : Window
{
    private MainViewModel? Vm => DataContext as MainViewModel;
    private MaterialTextureViewModel? _selected;

    public TextureDialog()
    {
        InitializeComponent();
        // Build the checkerboard brush using current theme colours so it updates
        // correctly on Light ↔ Dark switch (DrawingBrush in XAML cannot use
        // DynamicResource because WPF freezes it; code-behind bypasses this).
        ApplyCheckerBrush();
    }

    /// <summary>
    /// Creates (or replaces) the <c>CheckerBrush</c> Window resource using the
    /// two <c>TransparencyCheckerA/B</c> theme keys from Application.Resources.
    /// </summary>
    private void ApplyCheckerBrush()
    {
        // Use fully-qualified System.Windows.Application to avoid ambiguity with
        // the project's FourHUnfolder.Application namespace.
        var app = System.Windows.Application.Current;
        var colorA = (app.TryFindResource("TransparencyCheckerA") as SolidColorBrush)?.Color
                     ?? Color.FromRgb(0x33, 0x33, 0x44);
        var colorB = (app.TryFindResource("TransparencyCheckerB") as SolidColorBrush)?.Color
                     ?? Color.FromRgb(0x55, 0x55, 0x66);

        // Geometry.Parse is System.Windows.Media.Geometry — qualify to avoid clash
        // with FourHUnfolder.Geometry namespace.
        var brush = new DrawingBrush
        {
            TileMode      = TileMode.Tile,
            Viewport      = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Drawing       = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(new SolidColorBrush(colorB), null,
                        System.Windows.Media.Geometry.Parse("M0,0 H8 V8 H0 Z")),
                    new GeometryDrawing(new SolidColorBrush(colorA), null,
                        System.Windows.Media.Geometry.Parse("M0,0 H4 V4 H0 Z M4,4 H8 V8 H4 Z"))
                }
            }
        };
        Resources["CheckerBrush"] = brush;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (MaterialList.Items.Count > 0)
            MaterialList.SelectedIndex = 0;
    }

    // Rebuild the checker brush whenever the dialog is activated (e.g. after switching
    // theme in Settings and then re-focusing this dialog).  The brush is baked with
    // hard Color values so it can't auto-update via DynamicResource; re-building on
    // each activation is cheap (one DrawingBrush) and keeps it in sync with the theme.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        ApplyCheckerBrush();
    }

    private void MaterialList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = MaterialList.SelectedItem as MaterialTextureViewModel;
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        if (_selected == null)
        {
            SelectedMaterialLabel.Text = "Select a material slot";
            SelectedFileLabel.Text     = "";
            LargeThumbnail.Source      = null;
            NoTextureLbl.Visibility    = Visibility.Visible;
            LoadBtn.IsEnabled          = false;
            RemoveBtn.IsEnabled        = false;
            return;
        }

        SelectedMaterialLabel.Text = _selected.MaterialName;
        SelectedFileLabel.Text     = _selected.HasTexture
            ? _selected.TexturePath
            : "(no texture assigned)";
        LargeThumbnail.Source   = _selected.Thumbnail;
        NoTextureLbl.Visibility = _selected.HasTexture ? Visibility.Collapsed : Visibility.Visible;
        LoadBtn.IsEnabled       = true;
        RemoveBtn.IsEnabled     = _selected.HasTexture;
    }

    private void LoadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || Vm == null) return;

        var dlg = new OpenFileDialog
        {
            Title  = $"Load texture for \"{_selected.MaterialName}\"",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.tiff|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        Vm.SetMaterialTexture(_selected.MaterialId, dlg.FileName);
        RefreshDetail();
    }

    private void RemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || Vm == null) return;
        Vm.SetMaterialTexture(_selected.MaterialId, null);
        RefreshDetail();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
