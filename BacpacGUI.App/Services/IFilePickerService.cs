using System.Threading;
using System.Threading.Tasks;

namespace BacpacGUI.App.Services;

public interface IFilePickerService
{
    Task<string?> PickBacpacFileAsync(CancellationToken token);
}
