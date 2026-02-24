using System;
using System.Collections.ObjectModel;
using System.Data.Common;
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

    [ObservableProperty]
    private bool useConnectionStringMode;

    [ObservableProperty]
    private string connectionString = string.Empty;

    public bool IsManualMode => !UseConnectionStringMode;

    public bool IsConnectionStringMode => UseConnectionStringMode;

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

    partial void OnUseConnectionStringModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualMode));
        OnPropertyChanged(nameof(IsConnectionStringMode));
        LoadDatabasesCommand.NotifyCanExecuteChanged();
    }

    private bool CanLoadDatabases() => !UseConnectionStringMode && !IsRunning && !IsLoadingDatabases;

    private bool CanBrowseOutputPath() => !IsRunning && !IsLoadingDatabases;

    private bool CanStartBackup() => !IsRunning && !IsLoadingDatabases;

    private bool CanCopyLogs() => !string.IsNullOrWhiteSpace(LogOutput);

    private bool CanClearLogs() => !string.IsNullOrWhiteSpace(LogOutput);

    private bool CanOpenOutputFolder() => !IsRunning && !string.IsNullOrWhiteSpace(OutputPath);

    private async Task LoadDatabasesAsync()
    {
        if (UseConnectionStringMode)
        {
            return;
        }

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

            var dbName = GetBackupDatabaseNameCandidate();
            if (string.IsNullOrWhiteSpace(dbName))
            {
                dbName = "database";
            }

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

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            AppendHighlight("Output file path is required.");
            StatusMessage = "Output path required";
            return;
        }

        string dbName;
        string connectionString;

        if (UseConnectionStringMode)
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                AppendHighlight("Connection string is required.");
                StatusMessage = "Connection string required";
                return;
            }

            if (!TryGetDatabaseNameFromConnectionString(ConnectionString, out dbName))
            {
                AppendHighlight("Connection string must include Initial Catalog (or Database).");
                StatusMessage = "Database missing in connection string";
                return;
            }

            connectionString = ConnectionString.Trim();
        }
        else
        {
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

            dbName = SelectedDatabase;
            connectionString = SqlPackageService.BuildSqlAuthConnectionString(Server, Username, Password, SelectedDatabase);
        }

        var resolvedOutputPath = PrepareBackupOutputPath(OutputPath, dbName);
        if (string.IsNullOrWhiteSpace(resolvedOutputPath))
        {
            AppendHighlight("Output file path is invalid.");
            StatusMessage = "Output path invalid";
            return;
        }

        OutputPath = resolvedOutputPath;

        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;

        try
        {
            AppendHighlight($"Backup started for '{dbName}'.");
            AppendHighlight($"Resolved output path: {OutputPath}");
            var progress = new Progress<string>(AppendHighlight);
            await _sqlPackageService.ExportAsync(connectionString, resolvedOutputPath, progress, _cancellationTokenSource.Token);
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
            AppendHighlight(ex.ToString());
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

    private static string PrepareBackupOutputPath(string outputPath, string? selectedDatabase)
    {
        var trimmed = outputPath.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (Directory.Exists(trimmed) || IsDirectoryPathHint(trimmed))
        {
            trimmed = Path.Combine(trimmed, BuildDefaultBacpacName(selectedDatabase));
        }
        else if (!string.Equals(Path.GetExtension(trimmed), ".bacpac", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Path.GetExtension(trimmed)))
            {
                trimmed = $"{trimmed}.bacpac";
            }
        }

        return Path.GetFullPath(trimmed);
    }

    private static bool IsDirectoryPathHint(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar);
    }

    private static string BuildDefaultBacpacName(string? databaseName)
    {
        var safeDbName = SanitizeFileName(string.IsNullOrWhiteSpace(databaseName) ? "database" : databaseName);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        return $"{safeDbName}-{timestamp}.bacpac";
    }

    private string GetBackupDatabaseNameCandidate()
    {
        if (UseConnectionStringMode && TryGetDatabaseNameFromConnectionString(ConnectionString, out var dbFromConnection))
        {
            return dbFromConnection;
        }

        return SelectedDatabase ?? string.Empty;
    }

    private static bool TryGetDatabaseNameFromConnectionString(string rawConnectionString, out string databaseName)
    {
        databaseName = string.Empty;
        if (string.IsNullOrWhiteSpace(rawConnectionString))
        {
            return false;
        }

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = rawConnectionString.Trim() };

            if (TryGetConnectionValue(builder, "Initial Catalog", out var initialCatalog) &&
                !string.IsNullOrWhiteSpace(initialCatalog))
            {
                databaseName = initialCatalog;
                return true;
            }

            if (TryGetConnectionValue(builder, "Database", out var database) &&
                !string.IsNullOrWhiteSpace(database))
            {
                databaseName = database;
                return true;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        return false;
    }

    private static bool TryGetConnectionValue(DbConnectionStringBuilder builder, string key, out string value)
    {
        if (builder.TryGetValue(key, out var raw) && raw is not null)
        {
            value = raw.ToString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
