using System.Windows;
using EasyConnect.ViewModels;

namespace EasyConnect;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Func<bool> _isApplicationExiting;

    public MainWindow(MainViewModel viewModel, Func<bool>? isApplicationExiting = null)
    {
        InitializeComponent();
        _isApplicationExiting = isApplicationExiting ?? (() => false);
        DataContext = _viewModel = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.RefreshAsync();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isApplicationExiting())
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
