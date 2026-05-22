using Microsoft.Extensions.DependencyInjection;
using PepakuraClone.App.ViewModels;
using PepakuraClone.Application.Interfaces;
using PepakuraClone.Application.Services;
using PepakuraClone.Infrastructure.Exporters;
using PepakuraClone.Infrastructure.Loaders;
using System.Windows;

namespace PepakuraClone.App;

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
