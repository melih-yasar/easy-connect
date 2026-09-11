using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.UI.ViewManagement;

namespace EasyConnect.Helpers;

public sealed class ThemeManager : IDisposable
{
    private readonly Application _application;
    private readonly UISettings? _settings;
    private string? _currentTheme;
    private bool _disposed;

    public ThemeManager(Application application)
    {
        _application = application;
        try
        {
            // UISettings follows Windows' app color preference and reports changes without polling.
            _settings = new UISettings();
            _settings.ColorValuesChanged += OnColorValuesChanged;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Windows theme preference unavailable: {exception.GetType().Name}");
        }

        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        ApplyTheme();
    }

    public void UpdateTitleBar(Window window)
    {
        // The documented immersive dark title bar attribute is supported on Windows 11.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var useDark = _currentTheme == "Dark" ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref useDark, sizeof(int));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_settings is not null) _settings.ColorValuesChanged -= OnColorValuesChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }

    private void OnColorValuesChanged(UISettings sender, object args) => ScheduleUpdate();

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SystemParameters.HighContrast)) ScheduleUpdate();
    }

    private void ScheduleUpdate()
    {
        if (_disposed || _application.Dispatcher.HasShutdownStarted) return;
        _application.Dispatcher.BeginInvoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        if (_disposed) return;
        var theme = SystemParameters.HighContrast ? "HighContrast" : GetColorTheme();
        if (theme == _currentTheme) return;
        _currentTheme = theme;
        _application.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/EasyConnect;component/Views/Themes/{theme}.xaml")
        };
        foreach (Window window in _application.Windows) UpdateTitleBar(window);
    }

    private string GetColorTheme()
    {
        try
        {
            if (_settings is not null)
            {
                var background = _settings.GetColorValue(UIColorType.Background);
                return background.R * 0.299 + background.G * 0.587 + background.B * 0.114 < 128 ? "Dark" : "Light";
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Windows theme colors unavailable: {exception.GetType().Name}");
        }

        return "Light";
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
