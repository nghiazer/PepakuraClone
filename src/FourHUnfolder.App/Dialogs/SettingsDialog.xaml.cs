using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FourHUnfolder.App.ViewModels;
using FourHUnfolder.Application.Services;
using FourHUnfolder.Domain.Settings;

namespace FourHUnfolder.App.Dialogs;

public partial class SettingsDialog : Window
{
    private readonly SettingsService   _service;
    private readonly SettingsViewModel _vm;
    private readonly AppSettings       _originalSettings;

    public SettingsDialog(SettingsService service)
    {
        _service          = service;
        _originalSettings = service.Current;   // capture before any live Apply
        _vm               = new SettingsViewModel();
        _vm.LoadFrom(service.Current);

        InitializeComponent();
        DataContext = _vm;

        NavList.SelectedIndex = 0;

        // TD-S14-3: live-preview paper size changes
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.DefaultPaperSizeName))
            _service.Apply(_vm.ToSettings());
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = NavList.SelectedIndex;
        Panel3D     .Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        Panel2D     .Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelPrint  .Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelGeneral.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        Commit();
        DialogResult = true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => Commit();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _service.Apply(_originalSettings);   // revert any live preview changes
        DialogResult = false;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Reset all settings to their default values?",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        _vm.LoadFrom(new AppSettings());
    }

    private void Commit()
    {
        _service.Apply(_vm.ToSettings());
    }
}
