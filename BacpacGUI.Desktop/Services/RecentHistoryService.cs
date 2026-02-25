using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BacpacGUI.Desktop.Services;

public sealed class RecentHistoryService : IRecentHistoryService
{
    private const int MaxItems = 12;

    private readonly object _gate = new();
    private readonly string _storagePath;
    private RecentHistoryState _state;

    public RecentHistoryService()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(root, "BacpacGUI");
        _storagePath = Path.Combine(appDir, "recent-history.json");
        _state = LoadState(_storagePath);
    }

    public IReadOnlyList<string> GetRecentServers()
    {
        lock (_gate)
        {
            return _state.RecentServers.ToArray();
        }
    }

    public IReadOnlyList<string> GetRecentBacpacFiles()
    {
        lock (_gate)
        {
            return _state.RecentBacpacFiles.ToArray();
        }
    }

    public void AddRecentServer(string server)
    {
        var normalized = Normalize(server);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        lock (_gate)
        {
            Upsert(_state.RecentServers, normalized, StringComparer.OrdinalIgnoreCase);
            SaveState(_storagePath, _state);
        }
    }

    public void AddRecentBacpacFile(string bacpacPath)
    {
        var normalized = Normalize(bacpacPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        lock (_gate)
        {
            Upsert(_state.RecentBacpacFiles, normalized, StringComparer.OrdinalIgnoreCase);
            SaveState(_storagePath, _state);
        }
    }

    private static void Upsert(List<string> list, string value, IEqualityComparer<string> comparer)
    {
        var index = list.FindIndex(entry => comparer.Equals(entry, value));
        if (index >= 0)
        {
            list.RemoveAt(index);
        }

        list.Insert(0, value);
        if (list.Count > MaxItems)
        {
            list.RemoveRange(MaxItems, list.Count - MaxItems);
        }
    }

    private static string Normalize(string value) => value.Trim();

    private static RecentHistoryState LoadState(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new RecentHistoryState();
            }

            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<RecentHistoryState>(json);
            if (state is null)
            {
                return new RecentHistoryState();
            }

            state.RecentServers = state.RecentServers
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxItems)
                .ToList();

            state.RecentBacpacFiles = state.RecentBacpacFiles
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxItems)
                .ToList();

            return state;
        }
        catch
        {
            return new RecentHistoryState();
        }
    }

    private static void SaveState(string path, RecentHistoryState state)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(path, json);
    }

    private sealed class RecentHistoryState
    {
        public List<string> RecentServers { get; set; } = new();

        public List<string> RecentBacpacFiles { get; set; } = new();
    }
}
