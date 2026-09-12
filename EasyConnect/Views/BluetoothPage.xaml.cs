namespace EasyConnect.Views;

public partial class BluetoothPage : System.Windows.Controls.UserControl
{
    public BluetoothPage() => InitializeComponent();

    private void FocusNameEditor(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox editor || e.NewValue is not true) return;
        editor.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!editor.IsVisible) return;
            editor.Focus();
            editor.SelectAll();
        }));
    }

    // Also expose the hover information to keyboard and touch users.
    private void ShowDeviceInformation(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { ToolTip: System.Windows.Controls.ToolTip tooltip } button)
        {
            tooltip.PlacementTarget = button;
            tooltip.IsOpen = true;
        }
    }
}
