using Avalonia.Controls;
using PS3Butler.ViewModels;
using PS3Butler.Services;

namespace PS3Butler.Views;

public partial class MainWindow : Window
{
    public MainWindow() : this(null)
    {
    }

    public MainWindow(IQueueStateStore? queueStateStore)
    {
        InitializeComponent();

        var viewModel = new MainViewModel(this, queueStateStore);
        DataContext = viewModel;
        _ = new WindowShutdownBehavior(this, viewModel.StopAsync,
            ex => viewModel.SettingsStatus = $"Shutdown failed: {ex.Message}");
    }
}
