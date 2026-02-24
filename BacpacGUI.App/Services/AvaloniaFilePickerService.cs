using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace BacpacGUI.App.Services;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<string?> PickBacpacFileAsync(CancellationToken token)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.StorageProvider is null)
        {
            return null;
        }

        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Choose bacpac file"
        });

        token.ThrowIfCancellationRequested();

        var file = files.FirstOrDefault();
        return file?.TryGetLocalPath();
    }
}
