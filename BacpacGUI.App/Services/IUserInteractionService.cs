using System.Threading;
using System.Threading.Tasks;

namespace BacpacGUI.App.Services;

public interface IUserInteractionService
{
    Task CopyToClipboardAsync(string text, CancellationToken token);

    Task OpenPathInFileManagerAsync(string path, CancellationToken token);
}
