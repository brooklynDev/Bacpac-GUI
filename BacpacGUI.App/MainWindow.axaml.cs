using Avalonia;
using Avalonia.Controls;
using BacpacGUI.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BacpacGUI.App;

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
