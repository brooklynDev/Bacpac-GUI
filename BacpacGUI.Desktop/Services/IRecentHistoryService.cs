using System.Collections.Generic;

namespace BacpacGUI.Desktop.Services;

public interface IRecentHistoryService
{
    IReadOnlyList<string> GetRecentServers();

    IReadOnlyList<string> GetRecentBacpacFiles();

    void AddRecentServer(string server);

    void AddRecentBacpacFile(string bacpacPath);
}
