using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;

namespace BacpacGUI.Desktop.Services;

public sealed class SqlPackageService : ISqlPackageService
{
    public async Task ExportAsync(string connectionString, string outputPath, IProgress<string> logProgress, CancellationToken token)
    {
        var sourceDatabase = GetDatabaseName(connectionString);
        var normalizedOutputPath = NormalizeExportPath(outputPath, sourceDatabase);
        var dacServices = CreateDacServices(connectionString, logProgress);

        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            logProgress.Report($"Exporting database '{sourceDatabase}'...");
            dacServices.ExportBacpac(normalizedOutputPath, sourceDatabase, cancellationToken: token);
            token.ThrowIfCancellationRequested();
        }, token).ConfigureAwait(false);
    }

    public async Task ImportAsync(string bacpacPath, string connectionString, IProgress<string> logProgress, CancellationToken token)
    {
        var normalizedBacpacPath = NormalizeImportPath(bacpacPath);
        var targetDatabase = GetDatabaseName(connectionString);
        var dacServices = CreateDacServices(connectionString, logProgress);

        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            logProgress.Report($"Importing bacpac into '{targetDatabase}'...");
            using var package = BacPackage.Load(normalizedBacpacPath);
            dacServices.ImportBacpac(package, targetDatabase, cancellationToken: token);
            token.ThrowIfCancellationRequested();
        }, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetUserDatabasesAsync(string server, string username, string password, CancellationToken token)
    {
        var connectionString = BuildSqlAuthConnectionString(server, username, password, "master");
        const string query = "SELECT [name] FROM sys.databases WHERE [database_id] > 4 AND [state] = 0 ORDER BY [name];";

        var databases = new List<string>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);

        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0))
            {
                var dbName = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(dbName))
                {
                    databases.Add(dbName);
                }
            }
        }

        return databases;
    }

    public async Task<BacpacPreviewResult> PreviewAsync(string bacpacPath, CancellationToken token)
    {
        var normalizedBacpacPath = NormalizeImportPath(bacpacPath);

        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var archive = ZipFile.OpenRead(normalizedBacpacPath);

            var modelEntry = archive.Entries.FirstOrDefault(entry =>
                entry.FullName.EndsWith("model.xml", StringComparison.OrdinalIgnoreCase));
            if (modelEntry is null)
            {
                throw new InvalidOperationException("The bacpac does not contain a model.xml file.");
            }

            XDocument modelDocument;
            using (var modelStream = modelEntry.Open())
            {
                modelDocument = XDocument.Load(modelStream, LoadOptions.None);
            }

            var tableCount = CountModelObjects(modelDocument, "SqlTable");
            var viewCount = CountModelObjects(modelDocument, "SqlView");
            var procedureCount = CountModelObjects(modelDocument, "SqlProcedure");
            var databaseName = GetDatabaseNameFromModel(modelDocument);

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                var originEntry = archive.Entries.FirstOrDefault(entry =>
                    entry.FullName.EndsWith("origin.xml", StringComparison.OrdinalIgnoreCase));

                if (originEntry is not null)
                {
                    using var originStream = originEntry.Open();
                    var originDocument = XDocument.Load(originStream, LoadOptions.None);
                    databaseName = GetDatabaseNameFromOrigin(originDocument);
                }
            }

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                databaseName = Path.GetFileNameWithoutExtension(normalizedBacpacPath);
            }

            var fileInfo = new FileInfo(normalizedBacpacPath);
            return new BacpacPreviewResult(
                normalizedBacpacPath,
                fileInfo.Name,
                databaseName,
                fileInfo.Length,
                tableCount,
                viewCount,
                procedureCount);
        }, token).ConfigureAwait(false);
    }

    public static string BuildSqlAuthConnectionString(string server, string username, string password, string database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            UserID = username,
            Password = password,
            InitialCatalog = database,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 30
        };

        return builder.ConnectionString;
    }

    private static DacServices CreateDacServices(string connectionString, IProgress<string> logProgress)
    {
        var services = new DacServices(connectionString);

        services.Message += (_, e) =>
        {
            var messageText = e.Message?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(messageText))
            {
                logProgress.Report($"Message: {messageText}");
            }
        };

        services.ProgressChanged += (_, e) =>
        {
            var status = e.Status.ToString();

            var messageText = e.Message?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(messageText))
            {
                logProgress.Report($"{status}: {messageText}");
                return;
            }

            logProgress.Report(status);
        };

        return services;
    }

    private static string GetDatabaseName(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        if (TryGetValue(builder, "Initial Catalog", out var initialCatalog) && !string.IsNullOrWhiteSpace(initialCatalog))
        {
            return initialCatalog;
        }

        if (TryGetValue(builder, "Database", out var database) && !string.IsNullOrWhiteSpace(database))
        {
            return database;
        }

        throw new InvalidOperationException("Connection string must include 'Initial Catalog' or 'Database'.");
    }

    private static bool TryGetValue(DbConnectionStringBuilder builder, string key, out string value)
    {
        if (builder.TryGetValue(key, out var rawValue) && rawValue is not null)
        {
            value = rawValue.ToString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string NormalizeExportPath(string outputPath, string sourceDatabase)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new InvalidOperationException("Backup output path is required.");
        }

        var trimmedPath = outputPath.Trim();
        if (Directory.Exists(trimmedPath) || IsDirectoryPathHint(trimmedPath))
        {
            trimmedPath = Path.Combine(trimmedPath, BuildDefaultBacpacName(sourceDatabase));
        }
        else if (string.IsNullOrWhiteSpace(Path.GetExtension(trimmedPath)))
        {
            trimmedPath = $"{trimmedPath}.bacpac";
        }

        var fullPath = Path.GetFullPath(trimmedPath);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Backup output path must include a file name.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Backup output path must include a valid directory.");
        }

        Directory.CreateDirectory(directory);
        return fullPath;
    }

    private static string NormalizeImportPath(string bacpacPath)
    {
        if (string.IsNullOrWhiteSpace(bacpacPath))
        {
            throw new InvalidOperationException("Bacpac file path is required.");
        }

        var fullPath = Path.GetFullPath(bacpacPath.Trim());
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Bacpac file was not found.", fullPath);
        }

        return fullPath;
    }

    private static bool IsDirectoryPathHint(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar);
    }

    private static string BuildDefaultBacpacName(string databaseName)
    {
        var safeDatabaseName = SanitizeFileName(string.IsNullOrWhiteSpace(databaseName) ? "database" : databaseName);
        return $"{safeDatabaseName}-{DateTime.Now:yyyyMMdd-HHmm}.bacpac";
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Create(value.Length, (value, invalidChars), static (buffer, state) =>
        {
            for (var i = 0; i < state.value.Length; i++)
            {
                var ch = state.value[i];
                buffer[i] = Array.IndexOf(state.invalidChars, ch) >= 0 ? '_' : ch;
            }
        });
    }

    private static int CountModelObjects(XDocument modelDocument, string objectType)
    {
        return modelDocument.Descendants()
            .Where(element => element.Name.LocalName.Equals("Element", StringComparison.OrdinalIgnoreCase))
            .Count(element =>
            {
                var typeAttribute = element.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Type", StringComparison.OrdinalIgnoreCase));

                return typeAttribute is not null &&
                       typeAttribute.Value.Contains(objectType, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static string GetDatabaseNameFromModel(XDocument modelDocument)
    {
        var databaseElement = modelDocument.Descendants()
            .Where(element => element.Name.LocalName.Equals("Element", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(element =>
            {
                var typeAttribute = element.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Type", StringComparison.OrdinalIgnoreCase));

                return typeAttribute is not null &&
                       typeAttribute.Value.Contains("SqlDatabase", StringComparison.OrdinalIgnoreCase);
            });

        if (databaseElement is null)
        {
            return string.Empty;
        }

        var nameAttribute = databaseElement.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase));
        if (nameAttribute is null)
        {
            return string.Empty;
        }

        return NormalizeBracketedSqlName(nameAttribute.Value);
    }

    private static string GetDatabaseNameFromOrigin(XDocument originDocument)
    {
        var nameElement = originDocument.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(element.Value));

        return nameElement?.Value.Trim() ?? string.Empty;
    }

    private static string NormalizeBracketedSqlName(string rawName)
    {
        var trimmed = rawName.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length >= 2)
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }
}
