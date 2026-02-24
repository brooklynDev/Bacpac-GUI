using CommunityToolkit.Mvvm.ComponentModel;

namespace BacpacGUI.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public BackupViewModel Backup { get; }

    public RestoreViewModel Restore { get; }

    public MainWindowViewModel(BackupViewModel backup, RestoreViewModel restore)
    {
        Backup = backup;
        Restore = restore;
    }
}
