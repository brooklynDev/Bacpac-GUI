namespace BacpacGUI.Desktop.Services;

public sealed record BacpacPreviewResult(
    string FilePath,
    string FileName,
    string SuggestedDatabaseName,
    long FileSizeBytes,
    int TableCount,
    int ViewCount,
    int ProcedureCount);
