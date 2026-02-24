using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;

namespace BacpacGUI.App.Services;

public sealed class SqlPackageService : ISqlPackageService
{
    public async Task ExportAsync(string connectionString, string outputPath, IProgress<string> logProgress, CancellationToken token)
    {
        var normalizedOutputPath = Path.GetFullPath(outputPath);
        var sourceDatabase = GetDatabaseName(connectionString);
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
        var normalizedBacpacPath = Path.GetFullPath(bacpacPath);
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
        string? lastStatus = null;

        services.ProgressChanged += (_, e) =>
        {
            var status = e.Status.ToString();
            if (string.Equals(status, lastStatus, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lastStatus = status;

            var messageText = e.Message?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(messageText))
            {
                logProgress.Report($"{status}: {Shorten(messageText)}");
                return;
            }

            logProgress.Report(status);
        };

        return services;
    }

    private static string Shorten(string value)
    {
        return value.Length > 120 ? $"{value[..120]}..." : value;
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
}
