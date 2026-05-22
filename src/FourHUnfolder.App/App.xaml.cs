using Microsoft.Extensions.DependencyInjection;
using FourHUnfolder.App.ViewModels;
using FourHUnfolder.Application.Interfaces;
using FourHUnfolder.Application.Services;
using FourHUnfolder.Infrastructure.Exporters;
using FourHUnfolder.Infrastructure.Loaders;
using System.Windows;

namespace FourHUnfolder.App;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();

        // Settings (must be registered and loaded before anything that needs it)
        sc.AddSingleton<SettingsService>();

        // Infrastructure
        sc.AddSingleton<IMeshLoader,  ObjMeshLoader>();
        sc.AddSingleton<IExporter,    SvgExporter>();

        // Application
        sc.AddSingleton<MeshService>();
        sc.AddSingleton<UnfoldService>();
        sc.AddSingleton<ProjectSerializer>();

        // UI
        sc.AddTransient<MainViewModel>();

        Services = sc.BuildServiceProvider();

        // Load persisted settings before the main window opens
        Services.GetRequiredService<SettingsService>().Load();
    }
}
