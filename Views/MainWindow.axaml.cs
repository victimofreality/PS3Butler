using Avalonia.Controls;
using PS3Butler.ViewModels;

namespace PS3Butler.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DataContext = new MainViewModel(this);
    }
}