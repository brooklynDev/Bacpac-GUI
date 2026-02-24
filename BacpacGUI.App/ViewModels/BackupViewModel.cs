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
    private const int MaxHighlights = 10;

    private readonly ISqlPackageService _sqlPackageService;
    private readonly IFolderPickerService _folderPickerService;
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _lastHighlight;

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
    private string logOutput = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> activityHighlights = new();

    public IAsyncRelayCommand LoadDatabasesCommand { get; }

    public IAsyncRelayCommand BrowseOutputPathCommand { get; }

    public IAsyncRelayCommand StartBackupCommand { get; }

    public IRelayCommand CancelBackupCommand { get; }

    public BackupViewModel(ISqlPackageService sqlPackageService, IFolderPickerService folderPickerService)
    {
        _sqlPackageService = sqlPackageService;
        _folderPickerService = folderPickerService;
        LoadDatabasesCommand = new AsyncRelayCommand(LoadDatabasesAsync, CanLoadDatabases);
        BrowseOutputPathCommand = new AsyncRelayCommand(BrowseOutputPathAsync, CanBrowseOutputPath);
        StartBackupCommand = new AsyncRelayCommand(StartBackupAsync, CanStartBackup);
        CancelBackupCommand = new RelayCommand(CancelBackup, () => IsRunning);
    }

    partial void OnIsRunningChanged(bool value)
    {
        LoadDatabasesCommand.NotifyCanExecuteChanged();
        BrowseOutputPathCommand.NotifyCanExecuteChanged();
        StartBackupCommand.NotifyCanExecuteChanged();
        CancelBackupCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingDatabasesChanged(bool value)
    {
        LoadDatabasesCommand.NotifyCanExecuteChanged();
        BrowseOutputPathCommand.NotifyCanExecuteChanged();
        StartBackupCommand.NotifyCanExecuteChanged();
    }

    private bool CanLoadDatabases() => !IsRunning && !IsLoadingDatabases;

    private bool CanBrowseOutputPath() => !IsRunning && !IsLoadingDatabases;

    private bool CanStartBackup() => !IsRunning && !IsLoadingDatabases;

    private async Task LoadDatabasesAsync()
    {
        ClearHighlights();

        if (string.IsNullOrWhiteSpace(Server))
        {
            AppendHighlight("Server is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            AppendHighlight("Username is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            AppendHighlight("Password is required.");
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
        }
        catch (Exception ex)
        {
            AppendHighlight($"Failed to load databases: {ex.Message}");
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

        if (string.IsNullOrWhiteSpace(Server))
        {
            AppendHighlight("Server is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            AppendHighlight("Username is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            AppendHighlight("Password is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            AppendHighlight("Select a database first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            AppendHighlight("Output file path is required.");
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
        }
        catch (OperationCanceledException)
        {
            AppendHighlight("Backup canceled.");
        }
        catch (Exception ex)
        {
            AppendHighlight($"Backup failed: {ex.Message}");
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

    private void ClearHighlights()
    {
        ActivityHighlights.Clear();
        LogOutput = string.Empty;
        _lastHighlight = null;
    }

    private void AppendHighlight(string message)
    {
        var normalized = NormalizeMessage(message);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (string.Equals(normalized, _lastHighlight, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastHighlight = normalized;
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
        var trimmed = message.Trim();
        if (trimmed.Length > 140)
        {
            trimmed = $"{trimmed[..140]}...";
        }

        return trimmed;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    }
}
