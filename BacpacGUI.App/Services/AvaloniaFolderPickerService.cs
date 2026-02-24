using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace BacpacGUI.App.Services;

public sealed class AvaloniaFolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken token)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.StorageProvider is null)
        {
            return null;
        }

        var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Choose backup destination folder"
        });

        token.ThrowIfCancellationRequested();

        var folder = folders.FirstOrDefault();
        return folder?.TryGetLocalPath();
    }
}
