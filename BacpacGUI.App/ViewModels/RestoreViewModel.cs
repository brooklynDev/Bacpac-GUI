using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using BacpacGUI.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BacpacGUI.App.ViewModels;

public partial class RestoreViewModel : ObservableObject
{
    private const int MaxHighlights = 300;

    private readonly ISqlPackageService _sqlPackageService;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private string bacpacPath = string.Empty;

    [ObservableProperty]
    private string connectionString = string.Empty;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string logOutput = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> activityHighlights = new();

    public IAsyncRelayCommand StartRestoreCommand { get; }

    public IRelayCommand CancelRestoreCommand { get; }

    public RestoreViewModel(ISqlPackageService sqlPackageService)
    {
        _sqlPackageService = sqlPackageService;
        StartRestoreCommand = new AsyncRelayCommand(StartRestoreAsync, CanStartRestore);
        CancelRestoreCommand = new RelayCommand(CancelRestore, () => IsRunning);
    }

    partial void OnIsRunningChanged(bool value)
    {
        StartRestoreCommand.NotifyCanExecuteChanged();
        CancelRestoreCommand.NotifyCanExecuteChanged();
    }

    private bool CanStartRestore() => !IsRunning;

    private async Task StartRestoreAsync()
    {
        ClearHighlights();

        if (string.IsNullOrWhiteSpace(BacpacPath))
        {
            AppendHighlight("Bacpac file path is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            AppendHighlight("Target connection string is required.");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;

        try
        {
            AppendHighlight("Restore started.");
            var progress = new Progress<string>(AppendHighlight);
            await _sqlPackageService.ImportAsync(BacpacPath, ConnectionString, progress, _cancellationTokenSource.Token);
            AppendHighlight("Restore completed successfully.");
        }
        catch (OperationCanceledException)
        {
            AppendHighlight("Restore canceled.");
        }
        catch (Exception ex)
        {
            AppendHighlight($"Restore failed: {ex.Message}");
        }
        finally
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            IsRunning = false;
        }
    }

    private void CancelRestore()
    {
        _cancellationTokenSource?.Cancel();
    }

    private void ClearHighlights()
    {
        ActivityHighlights.Clear();
        LogOutput = string.Empty;
    }

    private void AppendHighlight(string message)
    {
        var normalized = NormalizeMessage(message);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var entry = $"{DateTime.Now:HH:mm:ss}  {normalized}";

        ActivityHighlights.Add(entry);
        if (ActivityHighlights.Count > MaxHighlights)
        {
            ActivityHighlights.RemoveAt(0);
        }

        LogOutput = string.IsNullOrEmpty(LogOutput)
            ? entry
            : $"{LogOutput}{Environment.NewLine}{entry}";
    }

    private static string NormalizeMessage(string message)
    {
        return message.Trim();
    }
}
