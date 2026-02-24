using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BacpacGUI.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BacpacGUI.App.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private const int MaxHighlights = 300;

    private readonly ISqlPackageService _sqlPackageService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IUserInteractionService _userInteractionService;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private string server = string.Empty;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> databases = new();

    [ObservableProperty]
    private string? selectedDatabase;

    [ObservableProperty]
    private string outputPath = string.Empty;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private bool isLoadingDatabases;

    [ObservableProperty]
    private bool isBackupCompleted;

    [ObservableProperty]
    private string completionMessage = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private string logOutput = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> activityHighlights = new();

    public IAsyncRelayCommand LoadDatabasesCommand { get; }

    public IAsyncRelayCommand BrowseOutputPathCommand { get; }

    public IAsyncRelayCommand StartBackupCommand { get; }

    public IRelayCommand CancelBackupCommand { get; }

    public IAsyncRelayCommand CopyLogsCommand { get; }

    public IRelayCommand ClearLogsCommand { get; }

    public IAsyncRelayCommand OpenOutputFolderCommand { get; }

    public BackupViewModel(
        ISqlPackageService sqlPackageService,
        IFolderPickerService folderPickerService,
        IUserInteractionService userInteractionService)
    {
        _sqlPackageService = sqlPackageService;
        _folderPickerService = folderPickerService;
        _userInteractionService = userInteractionService;

        LoadDatabasesCommand = new AsyncRelayCommand(LoadDatabasesAsync, CanLoadDatabases);
        BrowseOutputPathCommand = new AsyncRelayCommand(BrowseOutputPathAsync, CanBrowseOutputPath);
        StartBackupCommand = new AsyncRelayCommand(StartBackupAsync, CanStartBackup);
        CancelBackupCommand = new RelayCommand(CancelBackup, () => IsRunning);
        CopyLogsCommand = new AsyncRelayCommand(CopyLogsAsync, CanCopyLogs);
        ClearLogsCommand = new RelayCommand(ClearLogs, CanClearLogs);
        OpenOutputFolderCommand = new AsyncRelayCommand(OpenOutputFolderAsync, CanOpenOutputFolder);
    }

    partial void OnIsRunningChanged(bool value)
    {
        LoadDatabasesCommand.NotifyCanExecuteChanged();
        BrowseOutputPathCommand.NotifyCanExecuteChanged();
        StartBackupCommand.NotifyCanExecuteChanged();
        CancelBackupCommand.NotifyCanExecuteChanged();
        OpenOutputFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingDatabasesChanged(bool value)
    {
        LoadDatabasesCommand.NotifyCanExecuteChanged();
        BrowseOutputPathCommand.NotifyCanExecuteChanged();
        StartBackupCommand.NotifyCanExecuteChanged();
    }

    partial void OnLogOutputChanged(string value)
    {
        CopyLogsCommand.NotifyCanExecuteChanged();
        ClearLogsCommand.NotifyCanExecuteChanged();
    }

    partial void OnOutputPathChanged(string value)
    {
        OpenOutputFolderCommand.NotifyCanExecuteChanged();
    }

    private bool CanLoadDatabases() => !IsRunning && !IsLoadingDatabases;

    private bool CanBrowseOutputPath() => !IsRunning && !IsLoadingDatabases;

    private bool CanStartBackup() => !IsRunning && !IsLoadingDatabases;

    private bool CanCopyLogs() => !string.IsNullOrWhiteSpace(LogOutput);

    private bool CanClearLogs() => !string.IsNullOrWhiteSpace(LogOutput);

    private bool CanOpenOutputFolder() => !IsRunning && !string.IsNullOrWhiteSpace(OutputPath);

    private async Task LoadDatabasesAsync()
    {
        ClearHighlights();
        StatusMessage = "Loading databases...";

        if (string.IsNullOrWhiteSpace(Server))
        {
            AppendHighlight("Server is required.");
            StatusMessage = "Server required";
            return;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            AppendHighlight("Username is required.");
            StatusMessage = "Username required";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            AppendHighlight("Password is required.");
            StatusMessage = "Password required";
            return;
        }

        IsLoadingDatabases = true;

        try
        {
            AppendHighlight("Connecting to server and loading databases...");
            var dbs = await _sqlPackageService.GetUserDatabasesAsync(Server, Username, Password, CancellationToken.None);

            Databases.Clear();
            foreach (var db in dbs)
            {
                Databases.Add(db);
            }

            SelectedDatabase = Databases.FirstOrDefault();
            AppendHighlight($"Found {Databases.Count} available database(s).");
            StatusMessage = $"{Databases.Count} database(s) ready";
        }
        catch (Exception ex)
        {
            AppendHighlight($"Failed to load databases: {ex.Message}");
            StatusMessage = "Database load failed";
        }
        finally
        {
            IsLoadingDatabases = false;
        }
    }

    private async Task BrowseOutputPathAsync()
    {
        try
        {
            var folderPath = await _folderPickerService.PickFolderAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            var dbName = string.IsNullOrWhiteSpace(SelectedDatabase) ? "database" : SelectedDatabase;
            var safeDbName = SanitizeFileName(dbName);
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
            OutputPath = Path.Combine(folderPath, $"{safeDbName}-{timestamp}.bacpac");
            AppendHighlight("Output file path prepared from selected folder.");
        }
        catch (OperationCanceledException)
        {
            AppendHighlight("Folder selection canceled.");
        }
        catch (Exception ex)
        {
            AppendHighlight($"Could not choose output folder: {ex.Message}");
        }
    }

    private async Task StartBackupAsync()
    {
        ClearHighlights();
        IsBackupCompleted = false;
        CompletionMessage = string.Empty;
        StatusMessage = "Running backup...";

        if (string.IsNullOrWhiteSpace(Server))
        {
            AppendHighlight("Server is required.");
            StatusMessage = "Server required";
            return;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            AppendHighlight("Username is required.");
            StatusMessage = "Username required";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            AppendHighlight("Password is required.");
            StatusMessage = "Password required";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            AppendHighlight("Select a database first.");
            StatusMessage = "Database required";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            AppendHighlight("Output file path is required.");
            StatusMessage = "Output path required";
            return;
        }

        var connectionString = SqlPackageService.BuildSqlAuthConnectionString(Server, Username, Password, SelectedDatabase);

        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;

        try
        {
            AppendHighlight($"Backup started for '{SelectedDatabase}'.");
            var progress = new Progress<string>(AppendHighlight);
            await _sqlPackageService.ExportAsync(connectionString, OutputPath, progress, _cancellationTokenSource.Token);
            AppendHighlight("Backup completed successfully.");
            CompletionMessage = $"Backup created at: {OutputPath}";
            IsBackupCompleted = true;
            StatusMessage = "Backup complete";
        }
        catch (OperationCanceledException)
        {
            AppendHighlight("Backup canceled.");
            IsBackupCompleted = false;
            StatusMessage = "Backup canceled";
        }
        catch (Exception ex)
        {
            AppendHighlight($"Backup failed: {ex.Message}");
            IsBackupCompleted = false;
            StatusMessage = "Backup failed";
        }
        finally
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            IsRunning = false;
        }
    }

    private void CancelBackup()
    {
        _cancellationTokenSource?.Cancel();
    }

    private async Task CopyLogsAsync()
    {
        try
        {
            await _userInteractionService.CopyToClipboardAsync(LogOutput, CancellationToken.None);
            AppendHighlight("Logs copied to clipboard.");
        }
        catch (Exception ex)
        {
            AppendHighlight($"Could not copy logs: {ex.Message}");
        }
    }

    private async Task OpenOutputFolderAsync()
    {
        try
        {
            await _userInteractionService.OpenPathInFileManagerAsync(OutputPath, CancellationToken.None);
            AppendHighlight("Opened output location.");
        }
        catch (Exception ex)
        {
            AppendHighlight($"Could not open output location: {ex.Message}");
        }
    }

    private void ClearLogs()
    {
        ClearHighlights();
        StatusMessage = "Ready";
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

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    }
}
