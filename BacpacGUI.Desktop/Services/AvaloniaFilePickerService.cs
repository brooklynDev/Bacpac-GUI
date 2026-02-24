using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace BacpacGUI.Desktop.Services;

#pragma warning disable CS0618
public sealed class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<string?> PickBacpacFileAsync(CancellationToken token)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            return null;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose bacpac file",
            AllowMultiple = false,
            Filters = new List<FileDialogFilter>
            {
                new() { Name = "Bacpac files", Extensions = new List<string> { "bacpac" } },
                new() { Name = "All files", Extensions = new List<string> { "*" } }
            }
        };

        var files = await dialog.ShowAsync(desktop.MainWindow);
        token.ThrowIfCancellationRequested();

        return files?.FirstOrDefault();
    }
}
#pragma warning restore CS0618
