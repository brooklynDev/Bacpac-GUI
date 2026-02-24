using System.Threading;
using System.Threading.Tasks;

namespace BacpacGUI.Desktop.Services;

public interface IFilePickerService
{
    Task<string?> PickBacpacFileAsync(CancellationToken token);
}
