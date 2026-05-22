using System.Globalization;
using System.Windows;
using FourHUnfolder.Domain.Models;

namespace FourHUnfolder.App.Dialogs;

public sealed class UnfoldSetupResult
{
    public ModelScale  Scale     { get; init; } = ModelScale.Default;
    public PaperSizeModel Paper  { get; init; } = PaperSizeModel.A4;
}

public partial class UnfoldSetupDialog : Window
{
    private static readonly PaperSizeModel[] PaperPresets = PaperSizeModel.Presets;

    private bool _customEnabled;

    public UnfoldSetupResult? Result { get; private set; }

    public UnfoldSetupDialog(string boundingBoxInfo)
    {
        InitializeComponent();
        BBoxLabel.Text      = $"Bounding box: {boundingBoxInfo}";
        PaperPresetCombo.SelectedIndex = 0;
    }

    private void PaperPreset_Changed(object sender,
                                     System.Windows.Controls.SelectionChangedEventArgs e)
    {
        bool isCustom = PaperPresetCombo.SelectedIndex == 6; // "Custom …"
        _customEnabled = isCustom;
        if (CustomWidthBox  != null) CustomWidthBox.IsEnabled  = isCustom;
        if (CustomHeightBox != null) CustomHeightBox.IsEnabled = isCustom;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        // ── scale ──────────────────────────────────────────────────────────
        if (!double.TryParse(TargetSizeBox.Text, NumberStyles.Any,
                             CultureInfo.InvariantCulture, out double size) || size <= 0)
        {
            MessageBox.Show("Please enter a valid positive number for the target size.",
                            "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var unit = UnitCombo.SelectedIndex switch
        {
            1 => ScaleUnit.Cm,
            2 => ScaleUnit.Inch,
            _ => ScaleUnit.Mm
        };
        var axis = AxisCombo.SelectedIndex switch
        {
            0 => ScaleAxis.Width,
            1 => ScaleAxis.Height,
            2 => ScaleAxis.Depth,
            _ => ScaleAxis.Longest
        };

        // ── paper ──────────────────────────────────────────────────────────
        PaperSizeModel paper;
        int idx = PaperPresetCombo.SelectedIndex;

        if (idx < PaperPresets.Length)
        {
            paper = PaperPresets[idx];
        }
        else
        {
            if (!double.TryParse(CustomWidthBox.Text,  NumberStyles.Any,
                                  CultureInfo.InvariantCulture, out double cw) ||
                !double.TryParse(CustomHeightBox.Text, NumberStyles.Any,
                                  CultureInfo.InvariantCulture, out double ch) ||
                cw <= 0 || ch <= 0)
            {
                MessageBox.Show("Please enter valid positive dimensions for the custom paper size.",
                                "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            paper = PaperSizeModel.Custom(cw, ch);
        }

        if (LandscapeRadio.IsChecked == true)
            paper = paper.Landscape();

        Result = new UnfoldSetupResult
        {
            Scale = new ModelScale(size, unit, axis),
            Paper = paper
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
