using Avalonia;
using Avalonia.Controls;
using BacpacGUI.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BacpacGUI.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (Application.Current is App app)
        {
            DataContext = app.Services.GetRequiredService<MainWindowViewModel>();
        }
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
