using System.Threading;
using System.Threading.Tasks;

namespace BacpacGUI.App.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(CancellationToken token);
}
