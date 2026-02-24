using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using System;
using BacpacGUI.App.Services;
using BacpacGUI.App.ViewModels;

namespace BacpacGUI.App;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var services = ConfigureServices();
        BuildAvaloniaApp(services)
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp(IServiceProvider services)
        => AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static IServiceProvider ConfigureServices()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddSingleton<ISqlPackageService, SqlPackageService>();
        serviceCollection.AddSingleton<IFolderPickerService, AvaloniaFolderPickerService>();
        serviceCollection.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        serviceCollection.AddSingleton<IUserInteractionService, AvaloniaUserInteractionService>();

        serviceCollection.AddTransient<BackupViewModel>();
        serviceCollection.AddTransient<RestoreViewModel>();
        serviceCollection.AddTransient<MainWindowViewModel>();

        serviceCollection.AddTransient<MainWindow>();

        return serviceCollection.BuildServiceProvider();
    }
}
