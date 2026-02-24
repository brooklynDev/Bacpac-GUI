using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BacpacGUI.App.Services;

public interface ISqlPackageService
{
    Task ExportAsync(string connectionString, string outputPath, IProgress<string> logProgress, CancellationToken token);

    Task ImportAsync(string bacpacPath, string connectionString, IProgress<string> logProgress, CancellationToken token);

    Task<IReadOnlyList<string>> GetUserDatabasesAsync(string server, string username, string password, CancellationToken token);
}
