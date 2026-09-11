using EasyConnect.Models;
using EasyConnect.Services;
using EasyConnect.ViewModels;
using Xunit;

namespace EasyConnect.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task EmptySuccessfulScanShowsEmptyState()
    {
        using var viewModel = new MainViewModel(new StubBluetoothService(_ => Task.FromResult(Scan())));

        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.HasDevices);
        Assert.False(viewModel.HasError);
        Assert.StartsWith("Updated ", viewModel.LastUpdatedText);
    }

    [Fact]
    public async Task FailedInitialScanShowsErrorInsteadOfEmptyStateAndCanRetry()
    {
        using var viewModel = new MainViewModel(new StubBluetoothService(_ =>
            Task.FromException<BluetoothScanResult>(new InvalidOperationException("Unavailable"))));

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        Assert.Equal("Refresh to check your devices", viewModel.LastUpdatedText);
    }

    [Fact]
    public async Task RefreshFailurePreservesLastSuccessfulDevicesAndTimestamp()
    {
        var service = new StubBluetoothService(_ => Task.FromResult(Scan(Device("Headphones"))));
        using var viewModel = new MainViewModel(service);
        await viewModel.RefreshAsync();
        var previousTimestamp = viewModel.LastUpdatedText;
        service.Handler = _ => Task.FromException<BluetoothScanResult>(new UnauthorizedAccessException());

        await viewModel.RefreshAsync();

        Assert.Equal("Headphones", Assert.Single(viewModel.Devices).DeviceName);
        Assert.True(viewModel.HasDevices);
        Assert.True(viewModel.HasError);
        Assert.Contains("last successful results", viewModel.ErrorMessage!);
        Assert.Equal(previousTimestamp, viewModel.LastUpdatedText);
    }

    [Fact]
    public async Task SuccessfulRetryClearsErrorAndReplacesPreviousResults()
    {
        var service = new StubBluetoothService(_ =>
            Task.FromException<BluetoothScanResult>(new InvalidOperationException()));
        using var viewModel = new MainViewModel(service);
        await viewModel.RefreshAsync();
        service.Handler = _ => Task.FromResult(Scan(Device("Mouse")));

        await viewModel.RefreshAsync();

        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.HasError);
        Assert.Equal("Mouse", Assert.Single(viewModel.Devices).DeviceName);
    }

    [Fact]
    public async Task InFlightRefreshDisablesCommandAndIgnoresConcurrentRefresh()
    {
        var completion = new TaskCompletionSource<BluetoothScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubBluetoothService(_ => completion.Task);
        using var viewModel = new MainViewModel(service);

        var pendingRefresh = viewModel.RefreshAsync();
        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        await viewModel.RefreshAsync();
        await viewModel.RefreshCommand.ExecuteAsync();
        Assert.Equal(1, service.CallCount);

        completion.SetResult(Scan());
        await pendingRefresh;
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task DisposalCancelsScanAndIgnoresLateResultsWithoutNotifyingBindings()
    {
        var completion = new TaskCompletionSource<BluetoothScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubBluetoothService(_ => completion.Task);
        using var viewModel = new MainViewModel(service);
        var pendingRefresh = viewModel.RefreshAsync();
        viewModel.Dispose();
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        Assert.True(service.LastCancellationToken.IsCancellationRequested);
        completion.SetResult(Scan(Device("Late device")));
        await pendingRefresh;
        await viewModel.RefreshAsync();

        Assert.Empty(viewModel.Devices);
        Assert.Empty(notifications);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public async Task CancellationOnWindowCloseDoesNotProduceAnError()
    {
        var service = new StubBluetoothService(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Scan();
        });
        using var viewModel = new MainViewModel(service);
        var pendingRefresh = viewModel.RefreshAsync();

        viewModel.Dispose();
        await pendingRefresh;

        Assert.False(viewModel.HasError);
        Assert.Empty(viewModel.Devices);
    }

    [Fact]
    public async Task SummaryOnlyCountsConfirmedConnectionsAndKeepsMissingBatteryDevices()
    {
        using var viewModel = new MainViewModel(new StubBluetoothService(_ => Task.FromResult(Scan(
            Device("Connected", DeviceConnectionStatus.Connected, 82),
            Device("Disconnected", DeviceConnectionStatus.Disconnected),
            Device("Unknown", DeviceConnectionStatus.Unknown)))));

        await viewModel.RefreshAsync();

        Assert.Equal("3 paired devices · 1 connected", viewModel.SummaryText);
        Assert.Equal(3, viewModel.Devices.Count);
        Assert.Null(viewModel.Devices[2].BatteryPercentage);
    }

    [Fact]
    public async Task IncompleteScanDoesNotClaimThereAreNoPairedDevices()
    {
        using var viewModel = new MainViewModel(new StubBluetoothService(_ => Task.FromResult(
            new BluetoothScanResult([], "Windows could not read all Bluetooth devices."))));

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasWarning);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task PartialScanWarningIsShownAlongsideDevicesAndClearsOnCleanRefresh()
    {
        var service = new StubBluetoothService(_ => Task.FromResult(
            new BluetoothScanResult([Device("Keyboard")], "Some devices could not be inspected.")));
        using var viewModel = new MainViewModel(service);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasDevices);
        Assert.True(viewModel.HasWarning);
        Assert.False(viewModel.HasError);

        service.Handler = _ => Task.FromResult(Scan(Device("Keyboard")));
        await viewModel.RefreshAsync();
        Assert.False(viewModel.HasWarning);
    }

    private static BluetoothScanResult Scan(params BluetoothDeviceInfo[] devices) => new(devices);

    private static BluetoothDeviceInfo Device(
        string name,
        DeviceConnectionStatus connectionStatus = DeviceConnectionStatus.Connected,
        int? battery = null) => new(name, name, connectionStatus, battery);

    private sealed class StubBluetoothService(Func<CancellationToken, Task<BluetoothScanResult>> handler) : IBluetoothService
    {
        public Func<CancellationToken, Task<BluetoothScanResult>> Handler { get; set; } = handler;
        public int CallCount { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<BluetoothScanResult> GetPairedDevicesAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCancellationToken = cancellationToken;
            return Handler(cancellationToken);
        }
    }
}
