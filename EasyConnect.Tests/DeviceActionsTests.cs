using EasyConnect.Models;
using EasyConnect.Services;
using EasyConnect.ViewModels;
using Xunit;

namespace EasyConnect.Tests;

public sealed class DeviceActionsTests
{
    [Fact]
    public async Task DiscoveryDoesNotMixPairedDevicesIntoNearbyList()
    {
        var service = new FakeService
        {
            Discover = _ => Task.FromResult(new BluetoothScanResult([Device("Paired"), Device("Nearby") with { IsPaired = false }]))
        };
        using var vm = new MainViewModel(service, service);
        await vm.DiscoverAsync();
        Assert.Equal("Nearby", Assert.Single(vm.NearbyDevices).DeviceName);
        Assert.False(vm.IsDiscovering);
        Assert.True(vm.DiscoverCommand.CanExecute(null));
    }

    [Fact]
    public async Task StopSearchCancelsAndDoesNotReplacePreviousResults()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeService { Discover = async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return new([]); } };
        using var vm = new MainViewModel(service, service);
        vm.NearbyDevices.Add(Device("Previous") with { IsPaired = false });
        var scan = vm.DiscoverAsync();
        await started.Task;
        await vm.DiscoverAsync();
        Assert.Equal(1, service.DiscoveryCalls);
        await vm.StopDiscoveryCommand.ExecuteAsync();
        await scan;
        Assert.False(vm.IsDiscovering);
        Assert.Equal("Previous", Assert.Single(vm.NearbyDevices).DeviceName);
        Assert.Contains("stopped", vm.DiscoveryMessage);
    }

    [Fact]
    public async Task FailedDiscoveryReenablesSearchAndReportsFailure()
    {
        var service = new FakeService { Discover = _ => Task.FromException<BluetoothScanResult>(new InvalidOperationException()) };
        using var vm = new MainViewModel(service, service);
        await vm.DiscoverAsync();
        Assert.False(vm.IsDiscovering);
        Assert.Contains("Couldn't search", vm.DiscoveryMessage);
        Assert.True(vm.DiscoverCommand.CanExecute(null));
        Assert.False(vm.ShowNearbyEmpty);
    }

    [Fact]
    public async Task ClosingWindowIgnoresLateDiscoveryResults()
    {
        var pending = new TaskCompletionSource<BluetoothScanResult>();
        var service = new FakeService { Discover = _ => pending.Task };
        using var vm = new MainViewModel(service, service);
        var scan = vm.DiscoverAsync();
        vm.Dispose();
        pending.SetResult(new([Device("Late") with { IsPaired = false }]));
        await scan;
        Assert.Empty(vm.NearbyDevices);
    }

    [Fact]
    public async Task UnsupportedDeviceUsesExplicitSettingsFallback()
    {
        var service = new FakeService();
        using var vm = new MainViewModel(service, service);
        await vm.DeviceActionCommand.ExecuteAsync(Device("Keyboard"));
        Assert.Equal(1, service.SettingsCalls);
        Assert.Equal(0, service.ConnectionCalls);
        Assert.Contains("Windows", vm.OperationMessage!);
    }

    [Theory]
    [InlineData(DeviceConnectionStatus.Connected, false)]
    [InlineData(DeviceConnectionStatus.Disconnected, true)]
    public async Task ConnectionRequestsUseDeviceStateButDoNotInventSuccess(DeviceConnectionStatus state, bool expectedConnect)
    {
        var device = Device("Headphones") with { ConnectionStatus = state, ConnectionTargetIds = ["adapter-id"] };
        var service = new FakeService { Paired = new([device]) };
        using var vm = new MainViewModel(service, service);
        var action = vm.DeviceActionCommand.ExecuteAsync(device);
        Assert.True(vm.IsWorking);
        Assert.False(vm.DeviceActionCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null));
        await action;
        Assert.Equal(expectedConnect, service.RequestedConnect);
        Assert.Equal(state, Assert.Single(vm.Devices).ConnectionStatus);
        Assert.False(vm.IsWorking);
    }

    [Fact]
    public async Task ChosenImageUpdatesSelectedDeviceAndRow()
    {
        var device = Device("Headphones");
        var service = new FakeService { Paired = new([device]), Image = [1, 2, 3] };
        using var vm = new MainViewModel(service, service);
        await vm.RefreshAsync();
        await vm.ChooseImageCommand.ExecuteAsync(vm.SelectedDevice);
        Assert.Same(service.Image, vm.SelectedDevice!.ImageBytes);
        Assert.Same(service.Image, Assert.Single(vm.Devices).ImageBytes);
    }

    private static BluetoothDeviceInfo Device(string name) => new(name, name, DeviceConnectionStatus.Unknown, null);

    private sealed class FakeService : IBluetoothService, IDeviceActionsService
    {
        public Func<CancellationToken, Task<BluetoothScanResult>> Discover { get; init; } = _ => Task.FromResult(new BluetoothScanResult([]));
        public BluetoothScanResult Paired { get; init; } = new([]);
        public byte[]? Image { get; init; }
        public int DiscoveryCalls { get; private set; }
        public int SettingsCalls { get; private set; }
        public int ConnectionCalls { get; private set; }
        public bool RequestedConnect { get; private set; }
        public Task<BluetoothScanResult> GetPairedDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Paired);
        public Task<BluetoothScanResult> DiscoverNearbyAsync(CancellationToken token) { DiscoveryCalls++; return Discover(token); }
        public Task<string> SetConnectionAsync(BluetoothDeviceInfo device, bool connect, CancellationToken token) { ConnectionCalls++; RequestedConnect = connect; return Task.FromResult("Request sent."); }
        public Task OpenBluetoothSettingsAsync() { SettingsCalls++; return Task.CompletedTask; }
        public Task<byte[]?> ChooseImageAsync(BluetoothDeviceInfo device) => Task.FromResult(Image);
    }
}
