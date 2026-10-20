using Avalonia.Controls;
using PS3Butler.ViewModels;

namespace PS3Butler.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainViewModel(this);
        DataContext = viewModel;
        _ = new WindowShutdownBehavior(this, viewModel.StopAsync,
            ex => viewModel.SettingsStatus = $"Shutdown failed: {ex.Message}");
    }
}
