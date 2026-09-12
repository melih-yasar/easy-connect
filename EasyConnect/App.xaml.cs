using System.Windows;
using EasyConnect.Helpers;
using EasyConnect.Models;
using EasyConnect.Services;
using EasyConnect.ViewModels;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace EasyConnect;

public partial class App : System.Windows.Application
{
    private ThemeManager? _themeManager;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private Forms.NotifyIcon? _trayIcon;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _themeManager = new ThemeManager(this);
        _viewModel = new MainViewModel(new BluetoothService());
        _themeManager.SetTheme(_viewModel.Theme);
        _viewModel.NotificationRequested += OnNotificationRequested;
        _viewModel.SettingsSaved += OnSettingsSaved;

        _window = new MainWindow(_viewModel, () => _isExiting);
        MainWindow = _window;
        _window.SourceInitialized += (_, _) => _themeManager.UpdateTitleBar(_window);
        CreateTrayIcon();
        if (_viewModel.StartMinimized)
        {
            // Keep the dispatcher and tray alive even before the main window is first shown.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = _viewModel.RefreshAsync();
        }
        else _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isExiting = true;
        if (_viewModel is not null)
        {
            _viewModel.NotificationRequested -= OnNotificationRequested;
            _viewModel.SettingsSaved -= OnSettingsSaved;
        }

        _trayIcon?.Dispose();
        _viewModel?.Dispose();
        _themeManager?.Dispose();
        base.OnExit(e);
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "EasyConnect",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _trayIcon.ContextMenuStrip.Items.Add("Open", null, (_, _) => ShowMainWindow());
        _trayIcon.ContextMenuStrip.Items.Add("Refresh", null, async (_, _) =>
        {
            ShowMainWindow();
            if (_viewModel is not null) await _viewModel.RefreshAsync();
        });
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => ExitApplication());
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Shutdown();
    }

    private void OnNotificationRequested(object? sender, AppNotification notification)
    {
        Dispatcher.Invoke(() =>
        {
            if (_viewModel is null) return;
            // Standalone popups also work when starting in the tray before the main window is shown.
            var popup = new ConnectionPopupWindow(notification, _viewModel.PopupDurationSeconds);
            popup.Show();
        });
    }

    private void OnSettingsSaved(object? sender, AppSettings settings)
    {
        _themeManager?.SetTheme(settings.Theme);
        ApplyStartupSetting(settings.StartWithWindows);
        if (_window is not null) _themeManager?.UpdateTitleBar(_window);
    }

    private static void ApplyStartupSetting(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;
            if (enabled)
            {
                key.SetValue("EasyConnect", $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue("EasyConnect", throwOnMissingValue: false);
            }
        }
        catch
        {
            // Startup registration is a convenience setting; the app keeps running if Windows blocks it.
        }
    }
}
