using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using EasyConnect.Helpers;
using EasyConnect.Models;
using EasyConnect.Services;
using EasyConnect.ViewModels;
using EasyConnect.Views;
using Xunit;

namespace EasyConnect.Tests;

public sealed class WindowSmokeTests
{
    [Fact]
    public void PagesRenderNavigateAndApplyThemesAtSmallWindowSize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "EasyConnect-ui-tests", Guid.NewGuid().ToString("N"));
            var trace = new StringWriter();
            var listener = new TextWriterTraceListener(trace);
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            MainWindow? window = null;
            try
            {
                var app = new App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                using var theme = new ThemeManager(app);
                using var vm = new MainViewModel(new PreviewService(), devicePreferences: new UserDevicePreferences(folder), settingsStore: new AppSettingsStore(folder));
                vm.SettingsSaved += (_, settings) => theme.SetTheme(settings.Theme);
                window = new MainWindow(vm, () => true)
                {
                    Width = 740, Height = 540, Left = -10000, Top = -10000,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false, ShowInTaskbar = false
                };
                window.Show();
                Pump();
                var edit = Descendants<Button>(window).Single(button => Equals(button.ToolTip, "Edit device"));
                ((IInvokeProvider)new ButtonAutomationPeer(edit).GetPattern(PatternInterface.Invoke)).Invoke();
                Pump();
                Assert.True(vm.IsEditingDevice);
                var nameField = Descendants<TextBox>(window).Single(box => System.Windows.Automation.AutomationProperties.GetName(box) == "Device name");
                Assert.True(nameField.IsVisible);
                Assert.Equal(22, nameField.FontSize);
                Capture(window, "device-editor");
                vm.CancelEditCommand.ExecuteAsync().GetAwaiter().GetResult();
                var info = Descendants<Button>(window).Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Device information");
                ((IInvokeProvider)new ButtonAutomationPeer(info).GetPattern(PatternInterface.Invoke)).Invoke();
                Pump();
                var tooltip = Assert.IsType<ToolTip>(info.ToolTip);
                Assert.True(tooltip.IsOpen);
                Assert.Equal(vm.SelectedDevice, tooltip.DataContext);
                tooltip.IsOpen = false;
                var gear = new Typeface(new FontFamily("Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                Assert.True(gear.TryGetGlyphTypeface(out var glyphs));
                foreach (var code in new[] { 0xE713, 0xE72C, 0xE721, 0xE734, 0xE735, 0xE72B, 0xE70F, 0xE8EA })
                    Assert.True(glyphs.CharacterToGlyphMap.ContainsKey(code));

                foreach (var selectedTheme in new[] { AppTheme.Light, AppTheme.Dark, AppTheme.System })
                {
                    vm.Theme = selectedTheme;
                    Pump();
                    Assert.Single(Descendants<BluetoothPage>(window));
                    Assert.Empty(Descendants<SettingsPage>(window));
                    var dashboard = Descendants<BluetoothPage>(window).Single();
                    Assert.IsType<Grid>(dashboard.Content);
                    var panels = (FrameworkElement)dashboard.FindName("DevicePanels");
                    var footer = (FrameworkElement)dashboard.FindName("DashboardFooter");
                    Assert.True(panels.ActualHeight >= 100, $"Device panels too short: {panels.ActualHeight}");
                    Assert.True(footer.TransformToAncestor(dashboard).Transform(new Point(0, footer.ActualHeight)).Y <= dashboard.ActualHeight);
                    Capture(window, $"bluetooth-{selectedTheme}");
                    vm.ToggleSettingsCommand.ExecuteAsync().GetAwaiter().GetResult();
                    Pump();
                    Assert.Single(Descendants<SettingsPage>(window));
                    Assert.Empty(Descendants<BluetoothPage>(window));
                    Capture(window, $"settings-{selectedTheme}");
                    var compact = Descendants<CheckBox>(window).Single(check => Equals(check.Content, "Compact Mode"));
                    ((IToggleProvider)new ToggleButtonAutomationPeer(compact).GetPattern(PatternInterface.Toggle)).Toggle();
                    Assert.True(vm.CompactMode);
                    Assert.Equal(2, vm.VisibleDevices.Count);
                    var scroller = Descendants<ScrollViewer>(window).First();
                    scroller.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
                    {
                        RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent
                    });
                    Pump();
                    Assert.Equal(24, scroller.VerticalOffset);
                    scroller.ScrollToBottom();
                    Pump();
                    Capture(window, $"settings-bottom-{selectedTheme}");
                    var choice = Descendants<RadioButton>(window).Single(radio => Equals(radio.Content, selectedTheme.ToString()));
                    Assert.True(choice.IsChecked);
                    var back = Descendants<Button>(window).Single(button => Equals(button.ToolTip, "Back to Bluetooth"));
                    Assert.True(back.IsEnabled);
                    Assert.Contains(Descendants<TextBlock>(back), text => text.Text == "Back");
                    vm.ToggleSettingsCommand.ExecuteAsync().GetAwaiter().GetResult();
                    Pump();
                    var photos = Descendants<DeviceCard>(window).SelectMany(Descendants<DevicePhoto>).ToArray();
                    Assert.NotEmpty(photos);
                    Assert.All(photos, photo => Assert.Equal(32, photo.Width));
                    vm.CompactMode = false;
                }
                window.Width = 1120;
                window.Height = 860;
                Pump();
                Capture(window, "bluetooth-full");
                Assert.DoesNotContain("System.Windows.Data Error", trace.ToString());
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                window?.Close();
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                listener.Dispose();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Capture(Window window, string name)
    {
        // Set this optional directory for visual review; normal test runs create no screenshots.
        var folder = Environment.GetEnvironmentVariable("EASYCONNECT_UI_CAPTURE");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        var content = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
    }

    private sealed class PreviewService : IBluetoothService
    {
        public Task<BluetoothScanResult> GetPairedDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new BluetoothScanResult([
            new("Studio Headphones", "headphones", DeviceConnectionStatus.Connected, 82, "Audio.Headphones") { SignalStrength = -60, IsFavorite = true },
            new("Wireless Mouse", "mouse", DeviceConnectionStatus.Connected, null, "Input.Mouse"),
            new("Personal Phone", "phone", DeviceConnectionStatus.Disconnected, null, "Communication.Phone")
        ]));
    }
}
