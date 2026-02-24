using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace BacpacGUI.App.Services;

#pragma warning disable CS0618
public sealed class AvaloniaFolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken token)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            return null;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choose backup destination folder"
        };

        var folderPath = await dialog.ShowAsync(desktop.MainWindow);
        token.ThrowIfCancellationRequested();

        return string.IsNullOrWhiteSpace(folderPath) ? null : folderPath;
    }
}
#pragma warning restore CS0618
