using System.Threading;
using System.Threading.Tasks;

namespace BacpacGUI.Desktop.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(CancellationToken token);
}
