using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace BacpacGUI.Desktop.Services;

public sealed class AvaloniaUserInteractionService : IUserInteractionService
{
    public async Task CopyToClipboardAsync(string text, CancellationToken token)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.Clipboard is null)
        {
            throw new InvalidOperationException("Clipboard is not available.");
        }

        token.ThrowIfCancellationRequested();
        await desktop.MainWindow.Clipboard.SetTextAsync(text);
        token.ThrowIfCancellationRequested();
    }

    public Task OpenPathInFileManagerAsync(string path, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        token.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        var targetPath = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath) ?? fullPath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{targetPath}\"") { UseShellExecute = true });
            return Task.CompletedTask;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start(new ProcessStartInfo("open", $"\"{targetPath}\"") { UseShellExecute = false });
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo("xdg-open", $"\"{targetPath}\"") { UseShellExecute = false });
        return Task.CompletedTask;
    }
}
