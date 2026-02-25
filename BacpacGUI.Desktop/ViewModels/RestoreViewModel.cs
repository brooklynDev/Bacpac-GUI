using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BacpacGUI.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BacpacGUI.Desktop.ViewModels;

public partial class RestoreViewModel : ObservableObject
{
    private const int MaxHighlights = 300;
    private const int CompletionQuietPeriodMs = 800;
    private const int CompletionQuietTimeoutMs = 15000;

    private readonly ISqlPackageService _sqlPackageService;
    private readonly IFilePickerService _filePickerService;
    private readonly IUserInteractionService _userInteractionService;
    private CancellationTokenSource? _cancellationTokenSource;
    private DateTime _lastHighlightTimestampUtc = DateTime.UtcNow;

    [ObservableProperty]
    private string bacpacPath = string.Empty;

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
    private bool createNewDatabase;

    [ObservableProperty]
    private string newDatabaseName = string.Empty;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private bool isLoadingDatabases;

    [ObservableProperty]
    private bool isTestingConnection;

    [ObservableProperty]
    private bool isRestoreCompleted;

    [ObservableProperty]
    private string completionMessage = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private string logOutput = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> activityHighlights = new();

    [ObservableProperty]
    private bool isPreviewLoading;

    [ObservableProperty]
    private bool hasPreview;

    [ObservableProperty]
    private string previewFileName = string.Empty;

    [ObservableProperty]
    private string previewDatabaseName = string.Empty;

    [ObservableProperty]
    private string previewFileSize = string.Empty;

    [ObservableProperty]
    private int previewTableCount;

    [ObservableProperty]
    private int previewViewCount;

    [ObservableProperty]
    private int previewProcedureCount;

    public bool IsExistingDatabaseMode => !CreateNewDatabase;

    public bool ShowRestoreCompletedBanner => IsRestoreCompleted && !IsRunning;

    public bool ShowPreviewPanel => IsPreviewLoading || HasPreview;

    public int PreviewObjectCount => PreviewTableCount + PreviewViewCount + PreviewProcedureCount;

    public IAsyncRelayCommand BrowseBacpacPathCommand { get; }

    public IAsyncRelayCommand PreviewBacpacCommand { get; }

    public IAsyncRelayCommand LoadDatabasesCommand { get; }

    public IAsyncRelayCommand TestConnectionCommand { get; }

    public IAsyncRelayCommand StartRestoreCommand { get; }

    public IRelayCommand CancelRestoreCommand { get; }

    public IAsyncRelayCommand CopyLogsCommand { get; }

    public IRelayCommand ClearLogsCommand { get; }

    public IAsyncRelayCommand OpenBacpacFolderCommand { get; }

    public RestoreViewModel(
        ISqlPackageService sqlPackageService,
        IFilePickerService filePickerService,
        IUserInteractionService userInteractionService)
    {
        _sqlPackageService = sqlPackageService;
        _filePickerService = filePickerService;
        _userInteractionService = userInteractionService;

        BrowseBacpacPathCommand = new AsyncRelayCommand(BrowseBacpacPathAsync, CanBrowseOrLoad);
        PreviewBacpacCommand = new AsyncRelayCommand(PreviewBacpacAsync, CanPreviewBacpac);
        LoadDatabasesCommand = new AsyncRelayCommand(LoadDatabasesAsync, CanBrowseOrLoad);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanTestConnection);
        StartRestoreCommand = new AsyncRelayCommand(StartRestoreAsync, CanStartRestore);
        CancelRestoreCommand = new RelayCommand(CancelRestore, () => IsRunning);
        CopyLogsCommand = new AsyncRelayCommand(CopyLogsAsync, CanCopyLogs);
        ClearLogsCommand = new RelayCommand(ClearLogs, CanClearLogs);
        OpenBacpacFolderCommand = new AsyncRelayCommand(OpenBacpacFolderAsync, CanOpenBacpacFolder);
    }

    partial void OnCreateNewDatabaseChanged(bool value)
    {
        OnPropertyChanged(nameof(IsExistingDatabaseMode));

        if (value && string.IsNullOrWhiteSpace(NewDatabaseName))
        {
            var suggestedName = string.IsNullOrWhiteSpace(PreviewDatabaseName)
                ? GetDatabaseNameFromBacpacPath(BacpacPath)
                : PreviewDatabaseName;
            if (!string.IsNullOrWhiteSpace(suggestedName))
            {
                NewDatabaseName = suggestedName;
            }
        }
    }

    partial void OnIsRunningChanged(bool value)
    {
        BrowseBacpacPathCommand.NotifyCanExecuteChanged();
        LoadDatabasesCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        StartRestoreCommand.NotifyCanExecuteChanged();
        CancelRestoreCommand.NotifyCanExecuteChanged();
        OpenBacpacFolderCommand.NotifyCanExecuteChanged();
        PreviewBacpacCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowRestoreCompletedBanner));
    }

    partial void OnIsRestoreCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRestoreCompletedBanner));
    }

    partial void OnIsLoadingDatabasesChanged(bool value)
    {
        BrowseBacpacPathCommand.NotifyCanExecuteChanged();
        PreviewBacpacCommand.NotifyCanExecuteChanged();
        LoadDatabasesCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        StartRestoreCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTestingConnectionChanged(bool value)
    {
        BrowseBacpacPathCommand.NotifyCanExecuteChanged();
        PreviewBacpacCommand.NotifyCanExecuteChanged();
        LoadDatabasesCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        StartRestoreCommand.NotifyCanExecuteChanged();
    }

    partial void OnLogOutputChanged(string value)
    {
        CopyLogsCommand.NotifyCanExecuteChanged();
        ClearLogsCommand.NotifyCanExecuteChanged();
    }

    partial void OnBacpacPathChanged(string value)
    {
        OpenBacpacFolderCommand.NotifyCanExecuteChanged();
        PreviewBacpacCommand.NotifyCanExecuteChanged();
        ClearPreview();
        OnPropertyChanged(nameof(ShowPreviewPanel));
    }

    partial void OnIsPreviewLoadingChanged(bool value)
    {
        PreviewBacpacCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowPreviewPanel));
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

    partial void OnHasPreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPreviewPanel));
    }

    partial void OnPreviewTableCountChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewObjectCount));
    }

    partial void OnPreviewViewCountChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewObjectCount));
    }

    partial void OnPreviewProcedureCountChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewObjectCount));
    }

    private bool CanBrowseOrLoad() => !IsRunning && !IsLoadingDatabases && !IsTestingConnection;

    private bool CanPreviewBacpac() => !IsRunning && !IsLoadingDatabases && !IsPreviewLoading && !string.IsNullOrWhiteSpace(BacpacPath);

    private bool CanTestConnection()
    {
        return !IsRunning &&
               !IsLoadingDatabases &&
               !IsTestingConnection &&
               !string.IsNullOrWhiteSpace(Server) &&
               !string.IsNullOrWhiteSpace(Username) &&
               !string.IsNullOrWhiteSpace(Password);
    }

    private bool CanStartRestore() => !IsRunning && !IsLoadingDatabases && !IsTestingConnection;

    private bool CanCopyLogs() => !string.IsNullOrWhiteSpace(LogOutput);

    private bool CanClearLogs() => !string.IsNullOrWhiteSpace(LogOutput);

    private bool CanOpenBacpacFolder() => !IsRunning && !string.IsNullOrWhiteSpace(BacpacPath);

    private async Task BrowseBacpacPathAsync()
    {
        try
        {
            var filePath = await _filePickerService.PickBacpacFileAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            BacpacPath = filePath;
            AppendHighlight("Bacpac file selected.");
        }
        catch (OperationCanceledException)
        {
            AppendHighlight("File selection canceled.");
        }
        catch (Exception ex)
        {
            AppendHighlight($"Could not choose bacpac file: {ex.Message}");
        }
    }

    private async Task LoadDatabasesAsync()
    {
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

    private async Task StartRestoreAsync()
    {
        ClearHighlights();
        IsRestoreCompleted = false;
        CompletionMessage = string.Empty;
        StatusMessage = "Running restore...";

        if (string.IsNullOrWhiteSpace(BacpacPath))
        {
            AppendHighlight("Bacpac file path is required.");
            StatusMessage = "Bacpac file required";
            return;
        }

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

        var targetDatabase = CreateNewDatabase ? NewDatabaseName : SelectedDatabase;
        if (string.IsNullOrWhiteSpace(targetDatabase))
        {
            AppendHighlight(CreateNewDatabase
                ? "New database name is required."
                : "Select an existing database or choose new database mode.");
            StatusMessage = "Target database required";
            return;
        }

        var connectionString = SqlPackageService.BuildSqlAuthConnectionString(Server, Username, Password, targetDatabase);

        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;
        BufferedLogProgress? progress = null;
        var restoreSucceeded = false;

        try
        {
            AppendHighlight($"Restore started for '{targetDatabase}'.");
            progress = new BufferedLogProgress(AppendHighlight, _cancellationTokenSource.Token);
            await _sqlPackageService.ImportAsync(BacpacPath, connectionString, progress, _cancellationTokenSource.Token);
            await progress.DisposeAsync();
            progress = null;
            await WaitForLogQuiescenceAsync();
            restoreSucceeded = true;
        }
        catch (OperationCanceledException)
        {
            AppendHighlight("Restore canceled.");
            IsRestoreCompleted = false;
            StatusMessage = "Restore canceled";
            restoreSucceeded = false;
        }
        catch (Exception ex)
        {
            AppendHighlight($"Restore failed: {ex.Message}");
            IsRestoreCompleted = false;
            StatusMessage = "Restore failed";
            restoreSucceeded = false;
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

            if (restoreSucceeded)
            {
                AppendHighlight("Restore completed successfully.");
                CompletionMessage = $"Database ready: {targetDatabase}";
                IsRestoreCompleted = true;
                StatusMessage = "Restore complete";
            }
        }
    }

    private async Task TestConnectionAsync()
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

        var connectionString = SqlPackageService.BuildSqlAuthConnectionString(Server, Username, Password, "master");

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

    private void CancelRestore()
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

    private async Task OpenBacpacFolderAsync()
    {
        try
        {
            await _userInteractionService.OpenPathInFileManagerAsync(BacpacPath, CancellationToken.None);
            AppendHighlight("Opened bacpac location.");
        }
        catch (Exception ex)
        {
            AppendHighlight($"Could not open bacpac location: {ex.Message}");
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

        var entry = $"{DateTime.Now:HH:mm:ss}  {normalized}";
        _lastHighlightTimestampUtc = DateTime.UtcNow;

        ActivityHighlights.Add(entry);
        if (ActivityHighlights.Count > MaxHighlights)
        {
            ActivityHighlights.RemoveAt(0);
        }

        LogOutput = string.Join(Environment.NewLine, ActivityHighlights);
    }

    private static string NormalizeMessage(string message)
    {
        return message.Trim();
    }

    private static string GetDatabaseNameFromBacpacPath(string bacpacPath)
    {
        if (string.IsNullOrWhiteSpace(bacpacPath))
        {
            return string.Empty;
        }

        return Path.GetFileNameWithoutExtension(bacpacPath);
    }

    private async Task PreviewBacpacAsync()
    {
        if (string.IsNullOrWhiteSpace(BacpacPath))
        {
            AppendHighlight("Select a bacpac file first.");
            return;
        }

        IsPreviewLoading = true;
        HasPreview = false;
        ClearPreviewValues();

        try
        {
            AppendHighlight("Reading bacpac preview...");
            var preview = await _sqlPackageService.PreviewAsync(BacpacPath, CancellationToken.None);

            PreviewFileName = preview.FileName;
            PreviewDatabaseName = preview.SuggestedDatabaseName;
            PreviewFileSize = FormatFileSize(preview.FileSizeBytes);
            PreviewTableCount = preview.TableCount;
            PreviewViewCount = preview.ViewCount;
            PreviewProcedureCount = preview.ProcedureCount;
            HasPreview = true;

            if (CreateNewDatabase && string.IsNullOrWhiteSpace(NewDatabaseName) && !string.IsNullOrWhiteSpace(PreviewDatabaseName))
            {
                NewDatabaseName = PreviewDatabaseName;
            }

            AppendHighlight("Bacpac preview loaded.");
            StatusMessage = "Preview ready";
        }
        catch (Exception ex)
        {
            AppendHighlight($"Bacpac preview failed: {ex.Message}");
            StatusMessage = "Preview failed";
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        const double kb = 1024;
        const double mb = 1024 * 1024;
        const double gb = 1024 * 1024 * 1024;

        if (bytes >= gb)
        {
            return $"{bytes / gb:0.##} GB";
        }

        if (bytes >= mb)
        {
            return $"{bytes / mb:0.##} MB";
        }

        if (bytes >= kb)
        {
            return $"{bytes / kb:0.##} KB";
        }

        return $"{bytes} B";
    }

    private void ClearPreview()
    {
        IsPreviewLoading = false;
        HasPreview = false;
        ClearPreviewValues();
    }

    private void ClearPreviewValues()
    {
        PreviewFileName = string.Empty;
        PreviewDatabaseName = string.Empty;
        PreviewFileSize = string.Empty;
        PreviewTableCount = 0;
        PreviewViewCount = 0;
        PreviewProcedureCount = 0;
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
}
