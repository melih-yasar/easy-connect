using System.IO;
using EasyConnect.Models;
using EasyConnect.Services;
using EasyConnect.ViewModels;
using Xunit;

namespace EasyConnect.Tests;

public sealed class SettingsAndNavigationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EasyConnect-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeService _service = new();
    private MainViewModel Create() => new(_service, devicePreferences: new UserDevicePreferences(_folder), settingsStore: new AppSettingsStore(_folder));

    [Fact]
    public async Task SettingsPersistImmediatelyAndCompactFilteringSurvivesRelaunch()
    {
        using (var vm = Create())
        {
            await vm.RefreshAsync();
            vm.CompactMode = true;
            Assert.Single(vm.VisibleDevices);
            vm.CompactConnectedOnly = false;
            Assert.Equal(2, vm.VisibleDevices.Count);
            vm.CompactConnectedOnly = true;
            vm.StartMinimized = true;
            vm.AutomaticRefresh = false;
            vm.Theme = AppTheme.Dark;
            vm.ShowConnectionPopup = false;
            vm.ShowDisconnectPopup = true;
            vm.LowBatteryNotifications = false;
            vm.RefreshIntervalSeconds = 45;
            vm.PopupDurationSeconds = 8;
            vm.LowBatteryThreshold = 15;
        }
        using var restored = Create();
        await restored.RefreshAsync();
        Assert.True(restored.CompactMode);
        Assert.True(restored.CompactConnectedOnly);
        Assert.True(restored.StartMinimized);
        Assert.False(restored.AutomaticRefresh);
        Assert.Equal(AppTheme.Dark, restored.Theme);
        Assert.False(restored.ShowConnectionPopup);
        Assert.True(restored.ShowDisconnectPopup);
        Assert.False(restored.LowBatteryNotifications);
        Assert.Equal(45, restored.RefreshIntervalSeconds);
        Assert.Equal(8, restored.PopupDurationSeconds);
        Assert.Equal(15, restored.LowBatteryThreshold);
        Assert.Single(restored.VisibleDevices);
        restored.CompactMode = false;
        Assert.Equal(2, restored.VisibleDevices.Count);
    }

    [Fact]
    public async Task BackNavigationPreservesSearchAndSelection()
    {
        using var vm = Create();
        await vm.RefreshAsync();
        vm.SearchText = "Communication";
        var selected = vm.SelectedDevice;
        Assert.False(vm.ShowSettings);
        await vm.ToggleSettingsCommand.ExecuteAsync();
        Assert.True(vm.ShowSettings);
        await vm.ToggleSettingsCommand.ExecuteAsync();
        Assert.False(vm.ShowSettings);
        Assert.Equal("Communication", vm.SearchText);
        Assert.Equal(selected, vm.SelectedDevice);
        Assert.Single(vm.VisibleDevices);
    }

    [Fact]
    public async Task NotificationsRespectPreferencesAndConfirmedTransitions()
    {
        using var vm = Create();
        var notifications = new List<AppNotification>();
        vm.NotificationRequested += (_, n) => notifications.Add(n);
        await vm.RefreshAsync();
        Assert.Empty(notifications);
        vm.ShowDisconnectPopup = true;
        _service.Connected = false;
        await vm.RefreshAsync();
        Assert.Equal("Disconnected", Assert.Single(notifications).Title);
        notifications.Clear();
        vm.ShowConnectionPopup = false;
        vm.LowBatteryNotifications = false;
        _service.Connected = true;
        _service.Battery = 10;
        await vm.RefreshAsync();
        Assert.Empty(notifications);
        vm.LowBatteryNotifications = true;
        await vm.RefreshAsync();
        Assert.Equal("Low battery", Assert.Single(notifications).Title);
        await vm.RefreshAsync();
        Assert.Single(notifications);
    }

    [Fact]
    public async Task PencilEditorSavesNameAndCancelLeavesOriginalUntouched()
    {
        using var vm = Create();
        await vm.RefreshAsync();
        await vm.EditDeviceCommand.ExecuteAsync();
        vm.EditDeviceName = "Desk headphones";
        await vm.RefreshAsync();
        Assert.Equal("Desk headphones", vm.EditDeviceName);
        await vm.SaveDeviceEditCommand.ExecuteAsync();
        Assert.False(vm.IsEditingDevice);
        Assert.Equal("Desk headphones", vm.SelectedDevice!.DisplayName);
        await vm.EditDeviceCommand.ExecuteAsync();
        vm.EditDeviceName = "Discard this";
        await vm.CancelEditCommand.ExecuteAsync();
        Assert.Equal("Desk headphones", vm.SelectedDevice.DisplayName);
        using var restored = Create();
        await restored.RefreshAsync();
        Assert.Equal("Desk headphones", restored.Devices[0].DisplayName);
    }

    [Fact]
    public void InvalidSavedValuesAreNormalized()
    {
        var store = new AppSettingsStore(_folder);
        store.Save(new AppSettings { RefreshIntervalSeconds = int.MinValue, Theme = (AppTheme)99 });
        using var vm = Create();
        Assert.Equal(5, vm.RefreshIntervalSeconds);
        Assert.Equal(AppTheme.System, vm.Theme);
    }

    [Theory]
    [InlineData(-45, "Excellent")]
    [InlineData(-60, "Good")]
    [InlineData(-75, "Fair")]
    [InlineData(-90, "Weak")]
    [InlineData(null, "Signal unavailable")]
    public void DeviceLabelsAreReadable(int? signal, string label)
    {
        var device = new BluetoothDeviceInfo("Phone", "phone", DeviceConnectionStatus.Connected, null, "Communication.Phone") { SignalStrength = signal };
        Assert.Equal("Phone", device.CategoryLabel);
        Assert.Equal(label, device.SignalLabel);
        Assert.Equal("Battery unavailable", device.BatteryLabel);
        Assert.False(device.HasBattery);
    }

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

    private sealed class FakeService : IBluetoothService
    {
        public bool Connected { get; set; } = true;
        public int Battery { get; set; } = 80;
        public Task<BluetoothScanResult> GetPairedDevicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BluetoothScanResult([
                new("Headphones", "headphones", Connected ? DeviceConnectionStatus.Connected : DeviceConnectionStatus.Disconnected, Battery),
                new("Phone", "phone", DeviceConnectionStatus.Disconnected, null, "Communication.Phone")
            ]));
    }
}
