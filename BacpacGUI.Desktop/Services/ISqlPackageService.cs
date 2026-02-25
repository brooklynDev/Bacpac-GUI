using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BacpacGUI.Desktop.Services;

public interface ISqlPackageService
{
    Task TestConnectionAsync(string connectionString, CancellationToken token);

    Task ExportAsync(string connectionString, string outputPath, IProgress<string> logProgress, CancellationToken token);

    Task ImportAsync(string bacpacPath, string connectionString, IProgress<string> logProgress, CancellationToken token);

    Task<IReadOnlyList<string>> GetUserDatabasesAsync(string server, string username, string password, CancellationToken token);

    Task<BacpacPreviewResult> PreviewAsync(string bacpacPath, CancellationToken token);
}
