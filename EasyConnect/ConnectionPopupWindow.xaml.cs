using System.Windows;
using System.Windows.Threading;
using EasyConnect.Models;

namespace EasyConnect;

public partial class ConnectionPopupWindow : Window
{
    private readonly DispatcherTimer _closeTimer;

    public ConnectionPopupWindow(AppNotification notification, int durationSeconds)
    {
        InitializeComponent();
        DataContext = notification;
        Loaded += OnLoaded;
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(durationSeconds, 2, 15)) };
        _closeTimer.Tick += (_, _) => Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 18;
        Top = workArea.Bottom - Height - 18;
        _closeTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closeTimer.Stop();
        base.OnClosed(e);
    }
}
