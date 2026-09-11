using System.Windows;
using EasyConnect.Helpers;
using EasyConnect.Services;
using EasyConnect.ViewModels;

namespace EasyConnect;

public partial class App : Application
{
    private ThemeManager? _themeManager;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _themeManager = new ThemeManager(this);
        _viewModel = new MainViewModel(new BluetoothService());
        var window = new MainWindow(_viewModel);
        MainWindow = window;
        window.SourceInitialized += (_, _) => _themeManager.UpdateTitleBar(window);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _themeManager?.Dispose();
        base.OnExit(e);
    }
}
