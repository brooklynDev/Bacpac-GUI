using System;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BacpacGUI.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BacpacGUI.Desktop.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private const int MaxHighlights = 300;
    private const int CompletionQuietPeriodMs = 800;
    private const int CompletionQuietTimeoutMs = 15000;
    private static readonly Regex OverallProgressRegex = new(
        @"(?:SQL73\d+:\s*)?Processing\s+(Export|Import)\.\s*(?<percent>100(?:\.0+)?|(?:\d{1,2})(?:\.\d+)?)%\s*done\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ISqlPackageService _sqlPackageService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IUserInteractionService _userInteractionService;
    private readonly IRecentHistoryService _recentHistoryService;
    private CancellationTokenSource? _cancellationTokenSource;
    private DateTime _lastHighlightTimestampUtc = DateTime.UtcNow;

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
    private bool isTestingConnection;

    [ObservableProperty]
    private bool isBackupCompleted;

    [ObservableProperty]
    private string completionMessage = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private double operationProgressPercent;

    [ObservableProperty]
    private bool isOperationProgressIndeterminate = true;

    [ObservableProperty]
    private string operationProgressStep = "Waiting for progress...";

    [ObservableProperty]
    private string logOutput = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> recentServers = new();

    [ObservableProperty]
    private ObservableCollection<string> activityHighlights = new();

    [ObservableProperty]
    private bool useConnectionStringMode;

    [ObservableProperty]
    private string connectionString = string.Empty;

    public bool IsManualMode => !UseConnectionStringMode;

    public bool IsConnectionStringMode => UseConnectionStringMode;

    public bool ShowBackupCompletedBanner => IsBackupCompleted && !IsRunning;

    public IAsyncRelayCommand LoadDatabasesCommand { get; }

    public IAsyncRelayCommand BrowseOutputPathCommand { get; }

    public IAsyncRelayCommand TestConnectionCommand { get; }

    public IAsyncRelayCommand StartBackupCommand { get; }

    public IRelayCommand CancelBackupCommand { get; }

    public IAsyncRelayCommand CopyLogsCommand { get; }

    public IRelayCommand ClearLogsCommand { get; }

    public IAsyncRelayCommand OpenOutputFolderCommand { get; }

    public BackupViewModel(
        ISqlPackageService sqlPackageService,
        IFolderPickerService folderPickerService,
        IUserInteractionService userInteractionService,
        IRecentHistoryService recentHistoryService)
    {
        _sqlPackageService = sqlPackageService;
        _folderPickerService = folderPickerService;
        _userInteractionService = userInteractionService;
        _recentHistoryService = recentHistoryService;

        LoadDatabasesCommand = new AsyncRelayCommand(LoadDatabasesAsync, CanLoadDatabases);
        BrowseOutputPathCommand = new AsyncRelayCommand(BrowseOutputPathAsync, CanBrowseOutputPath);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanTestConnection);
        StartBackupCommand = new AsyncRelayCommand(StartBackupAsync, CanStartBackup);
        CancelBackupCommand = new RelayCommand(CancelBackup, () => IsRunning);
        CopyLogsCommand = new AsyncRelayCommand(CopyLogsAsync, CanCopyLogs);
        ClearLogsCommand = new RelayCommand(ClearLogs, CanClearLogs);
        OpenOutputFolderCommand = new AsyncRelayCommand(OpenOutputFolderAsync, CanOpenOutputFolder);

        foreach (var recentServer in _recentHistoryService.GetRecentServers())
        {
            RecentServers.Add(recentServer);
        }
    }

    partial void OnIsRunningChanged(bool value)
    {
        LoadDatabasesCommand.NotifyCanExecuteChanged();
        BrowseOutputPathCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        StartBackupCommand.NotifyCanExecuteChanged();
        CancelBackupCommand.NotifyCanExecuteChanged();
        OpenOutputFolderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowBackupCompletedBanner));
    }

    partial void OnIsBackupCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBackupCompletedBanner));
    }

    partial void OnIsLoadingDatabasesChanged(bool value)
    {
        LoadDatabasesCommand.NotifyCanExecuteChanged();
        BrowseOutputPathCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        StartBackupCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTestingConnectionChanged(bool value)
    {
        LoadDatabasesCommand.NotifyCanExecuteChanged();
        BrowseOutputPathCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
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
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnServerChanged(string value)
    {
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnUsernameChanged(string value)
    {
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnPasswordChanged(string value)
    {
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnConnectionStringChanged(string value)
    {
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    private bool CanLoadDatabases() => !UseConnectionStringMode && !IsRunning && !IsLoadingDatabases && !IsTestingConnection;

    private bool CanBrowseOutputPath() => !IsRunning && !IsLoadingDatabases && !IsTestingConnection;

    private bool CanTestConnection()
    {
        if (IsRunning || IsLoadingDatabases || IsTestingConnection)
        {
            return false;
        }

        if (UseConnectionStringMode)
        {
            return !string.IsNullOrWhiteSpace(ConnectionString);
        }

        return !string.IsNullOrWhiteSpace(Server) &&
               !string.IsNullOrWhiteSpace(Username) &&
               !string.IsNullOrWhiteSpace(Password);
    }

    private bool CanStartBackup() => !IsRunning && !IsLoadingDatabases && !IsTestingConnection;

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
        RememberServer(Server);

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
            RememberServer(Server);
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
        ResetOperationProgress("Preparing backup...");
        IsRunning = true;
        BufferedLogProgress? progress = null;
        var backupSucceeded = false;

        try
        {
            AppendHighlight($"Backup started for '{dbName}'.");
            AppendHighlight($"Resolved output path: {OutputPath}");
            progress = new BufferedLogProgress(AppendHighlight, _cancellationTokenSource.Token);
            await _sqlPackageService.ExportAsync(connectionString, resolvedOutputPath, progress, _cancellationTokenSource.Token);
            await progress.DisposeAsync();
            progress = null;
            await WaitForLogQuiescenceAsync();
            backupSucceeded = true;
        }
        catch (OperationCanceledException)
        {
            AppendHighlight("Backup canceled.");
            IsOperationProgressIndeterminate = false;
            OperationProgressStep = "Canceled";
            IsBackupCompleted = false;
            StatusMessage = "Backup canceled";
            backupSucceeded = false;
        }
        catch (Exception ex)
        {
            AppendHighlight($"Backup failed: {ex.Message}");
            AppendHighlight(ex.ToString());
            IsOperationProgressIndeterminate = false;
            OperationProgressStep = "Failed";
            IsBackupCompleted = false;
            StatusMessage = "Backup failed";
            backupSucceeded = false;
        }
        finally
        {
            if (progress is not null)
            {
                await progress.DisposeAsync();
            }

            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            IsRunning = false;

            if (backupSucceeded)
            {
                OperationProgressPercent = 100;
                IsOperationProgressIndeterminate = false;
                OperationProgressStep = "Completed";
                AppendHighlight("Backup completed successfully.");
                CompletionMessage = $"Backup created at: {OutputPath}";
                IsBackupCompleted = true;
                StatusMessage = "Backup complete";
            }
        }
    }

    private async Task TestConnectionAsync()
    {
        string connectionString;
        if (UseConnectionStringMode)
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                AppendHighlight("Connection string is required.");
                StatusMessage = "Connection string required";
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

            connectionString = SqlPackageService.BuildSqlAuthConnectionString(Server, Username, Password, "master");
            RememberServer(Server);
        }

        IsTestingConnection = true;
        StatusMessage = "Testing connection...";
        AppendHighlight("Testing SQL connection...");

        try
        {
            await _sqlPackageService.TestConnectionAsync(connectionString, CancellationToken.None);
            AppendHighlight("Connection successful.");
            StatusMessage = "Connection successful";
        }
        catch (Exception ex)
        {
            AppendHighlight($"Connection failed: {ex.Message}");
            StatusMessage = "Connection failed";
        }
        finally
        {
            IsTestingConnection = false;
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
        _lastHighlightTimestampUtc = DateTime.UtcNow;
    }

    private void AppendHighlight(string message)
    {
        var normalized = NormalizeMessage(message);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        TryApplyOverallProgressFromMessage(normalized);

        var entry = $"{DateTime.Now:HH:mm:ss}  {normalized}";
        _lastHighlightTimestampUtc = DateTime.UtcNow;

        ActivityHighlights.Add(entry);
        if (ActivityHighlights.Count > MaxHighlights)
        {
            ActivityHighlights.RemoveAt(0);
        }

        LogOutput = string.Join(Environment.NewLine, ActivityHighlights);
    }

    private async Task WaitForLogQuiescenceAsync()
    {
        var startedUtc = DateTime.UtcNow;

        while (true)
        {
            var nowUtc = DateTime.UtcNow;
            var quietForMs = (nowUtc - _lastHighlightTimestampUtc).TotalMilliseconds;
            if (quietForMs >= CompletionQuietPeriodMs)
            {
                return;
            }

            var waitedMs = (nowUtc - startedUtc).TotalMilliseconds;
            if (waitedMs >= CompletionQuietTimeoutMs)
            {
                return;
            }

            await Task.Delay(100);
        }
    }

    private static string NormalizeMessage(string message)
    {
        return message.Trim();
    }

    private void ResetOperationProgress(string initialStep)
    {
        OperationProgressPercent = 0;
        IsOperationProgressIndeterminate = true;
        OperationProgressStep = initialStep;
    }

    private void TryApplyOverallProgressFromMessage(string message)
    {
        if (!IsRunning)
        {
            return;
        }

        var match = OverallProgressRegex.Match(message);
        if (!match.Success)
        {
            return;
        }

        if (!double.TryParse(match.Groups["percent"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return;
        }

        var candidate = Math.Clamp(parsed, 0, 99.5);
        if (candidate > OperationProgressPercent)
        {
            OperationProgressPercent = candidate;
        }

        IsOperationProgressIndeterminate = false;
        var operationName = match.Groups[1].Value;
        OperationProgressStep = $"Processing {operationName}...";
    }

    private void RememberServer(string server)
    {
        var normalized = server.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _recentHistoryService.AddRecentServer(normalized);

        var existingIndex = -1;
        for (var i = 0; i < RecentServers.Count; i++)
        {
            if (string.Equals(RecentServers[i], normalized, StringComparison.OrdinalIgnoreCase))
            {
                existingIndex = i;
                break;
            }
        }

        if (existingIndex == 0)
        {
            return;
        }

        if (existingIndex > 0)
        {
            RecentServers.RemoveAt(existingIndex);
        }

        RecentServers.Insert(0, normalized);
        if (RecentServers.Count > 12)
        {
            RecentServers.RemoveAt(RecentServers.Count - 1);
        }
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
