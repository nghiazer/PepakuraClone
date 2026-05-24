using System.Windows;
using FourHUnfolder.App.ViewModels;

namespace FourHUnfolder.App.Dialogs;

public partial class ModelOrientationDialog : Window
{
    public ModelOrientationViewModel Result { get; }

    /// <summary>True = user clicked OK; False = user clicked Skip (no transform applied).</summary>
    public bool Applied { get; private set; }

    public ModelOrientationDialog()
    {
        Result      = new ModelOrientationViewModel();
        DataContext = Result;
        InitializeComponent();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        Applied      = true;
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Applied      = false;
        DialogResult = false;
    }
}
