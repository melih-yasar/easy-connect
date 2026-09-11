using System.Collections.ObjectModel;
using System.Collections.Specialized;
using EasyConnect.Helpers;
using EasyConnect.Models;
using EasyConnect.Services;

namespace EasyConnect.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IBluetoothService _bluetoothService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _isLoading;
    private bool _isDisposed;
    private int _refreshInProgress;
    private string? _errorMessage;
    private string? _warningMessage;
    private DateTimeOffset? _lastUpdated;
    private readonly IDeviceActionsService? _actions;
    private CancellationTokenSource? _discoveryCancellation;
    private bool _isDiscovering;
    private bool _isWorking;
    private bool _hasSearched;
    private string _discoveryMessage = "Find headphones, speakers, and other devices around you.";
    private string? _operationMessage;
    private BluetoothDeviceInfo? _selectedDevice;

    public MainViewModel(IBluetoothService bluetoothService, IDeviceActionsService? actions = null)
    {
        ArgumentNullException.ThrowIfNull(bluetoothService);
        _bluetoothService = bluetoothService;
        _actions = actions ?? bluetoothService as IDeviceActionsService;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, HandleRefreshError, () => !IsLoading && !IsWorking && !_isDisposed);
        DiscoverCommand = new AsyncRelayCommand(DiscoverAsync, HandleActionError, () => _actions is not null && !IsDiscovering && !_isDisposed);
        StopDiscoveryCommand = new AsyncRelayCommand(() => { _discoveryCancellation?.Cancel(); return Task.CompletedTask; }, HandleActionError, () => IsDiscovering);
        DeviceActionCommand = new AsyncRelayCommand(ActOnDeviceAsync, HandleActionError, CanAct);
        ChooseImageCommand = new AsyncRelayCommand(ChooseImageAsync, HandleActionError, CanAct);
        SettingsCommand = new AsyncRelayCommand(() => _actions!.OpenBluetoothSettingsAsync(), HandleActionError, CanAct);
        Devices.CollectionChanged += OnDevicesChanged;
    }

    public ObservableCollection<BluetoothDeviceInfo> Devices { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand DiscoverCommand { get; }
    public AsyncRelayCommand StopDiscoveryCommand { get; }
    public AsyncRelayCommand DeviceActionCommand { get; }
    public AsyncRelayCommand ChooseImageCommand { get; }
    public AsyncRelayCommand SettingsCommand { get; }
    public ObservableCollection<BluetoothDeviceInfo> NearbyDevices { get; } = [];
    public BluetoothDeviceInfo? SelectedDevice { get => _selectedDevice; set { if (SetProperty(ref _selectedDevice, value)) OnPropertyChanged(nameof(HasSelectedDevice)); } }
    public bool HasSelectedDevice => SelectedDevice is not null;
    public bool IsWorking { get => _isWorking; private set { if (SetProperty(ref _isWorking, value)) NotifyActionCommands(); } }
    public bool IsDiscovering
    {
        get => _isDiscovering;
        private set
        {
            if (!SetProperty(ref _isDiscovering, value)) return;
            DiscoverCommand.NotifyCanExecuteChanged();
            StopDiscoveryCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ShowNearbyEmpty));
        }
    }
    public string DiscoveryMessage { get => _discoveryMessage; private set => SetProperty(ref _discoveryMessage, value); }
    public bool ShowNearbyEmpty => !IsDiscovering && _hasSearched && NearbyDevices.Count == 0;
    public string? OperationMessage { get => _operationMessage; private set { if (SetProperty(ref _operationMessage, value)) OnPropertyChanged(nameof(HasOperationMessage)); } }
    public bool HasOperationMessage => !string.IsNullOrEmpty(OperationMessage);

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                RefreshCommand.NotifyCanExecuteChanged();
                NotifyActionCommands();
            }
        }
    }

    public bool HasDevices => Devices.Count > 0;

    public bool IsEmpty => !IsLoading && !HasError && !HasWarning && !HasDevices;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? WarningMessage
    {
        get => _warningMessage;
        private set
        {
            if (SetProperty(ref _warningMessage, value))
            {
                OnPropertyChanged(nameof(HasWarning));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningMessage);

    public string SummaryText =>
        $"{Devices.Count} paired {(Devices.Count == 1 ? "device" : "devices")} · " +
        $"{Devices.Count(device => device.ConnectionStatus == DeviceConnectionStatus.Connected)} connected";

    public string LastUpdatedText => _lastUpdated is { } updated
        ? $"Updated {updated.LocalDateTime:t}"
        : "Refresh to check your devices";

    public async Task RefreshAsync()
    {
        if (_isDisposed || Interlocked.CompareExchange(ref _refreshInProgress, 1, 0) != 0)
        {
            return;
        }

        var cancellationToken = _lifetimeCancellation.Token;
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            WarningMessage = null;

            // Await on the calling UI context; only the service performs device work.
            var result = await _bluetoothService.GetPairedDevicesAsync(cancellationToken);
            if (_isDisposed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var selectedId = SelectedDevice?.DeviceId;
            Devices.Clear();
            foreach (var device in result.Devices)
            {
                Devices.Add(device);
            }
            SelectedDevice = Devices.FirstOrDefault(device => device.DeviceId == selectedId) ?? Devices.FirstOrDefault();

            WarningMessage = result.Warning;
            _lastUpdated = DateTimeOffset.Now;
            OnPropertyChanged(nameof(LastUpdatedText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing the window cancels the scan without presenting an error.
        }
        catch (Exception exception)
        {
            HandleRefreshError(exception);
        }
        finally
        {
            if (!_isDisposed)
            {
                IsLoading = false;
            }

            Interlocked.Exchange(ref _refreshInProgress, 0);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Devices.CollectionChanged -= OnDevicesChanged;
        _lifetimeCancellation.Cancel();
        _discoveryCancellation?.Cancel();
        _lifetimeCancellation.Dispose();
        RefreshCommand.NotifyCanExecuteChanged();
        NotifyActionCommands();
    }

    public async Task DiscoverAsync()
    {
        if (_actions is null || IsDiscovering || _isDisposed) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _discoveryCancellation = cancellation;
        IsDiscovering = true;
        DiscoveryMessage = "Searching nearby… Put your device in pairing mode.";
        try
        {
            var result = await _actions.DiscoverNearbyAsync(cancellation.Token);
            if (_isDisposed || cancellation.IsCancellationRequested) return;
            NearbyDevices.Clear();
            foreach (var device in result.Devices.Where(device => !device.IsPaired && !Devices.Any(paired => paired.DeviceId == device.DeviceId))) NearbyDevices.Add(device);
            _hasSearched = result.Warning is null;
            DiscoveryMessage = result.Warning ?? (NearbyDevices.Count == 0 ? "No discoverable devices found. Move closer and try pairing mode." : $"{NearbyDevices.Count} nearby. Pair a device in Windows to add it here.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { if (!_isDisposed) DiscoveryMessage = "Search stopped. Previous results are kept until the next search."; }
        catch (Exception) { if (!_isDisposed) DiscoveryMessage = "Couldn't search nearby. Check Bluetooth in Windows Settings and try again."; }
        finally
        {
            _discoveryCancellation = null;
            if (!_isDisposed) IsDiscovering = false;
        }
    }

    private bool CanAct() => _actions is not null && !IsLoading && !IsWorking && !_isDisposed;

    private async Task ActOnDeviceAsync(object? parameter)
    {
        if (parameter is not BluetoothDeviceInfo device || !CanAct()) return;
        IsWorking = true;
        OperationMessage = null;
        try
        {
            if (!device.CanControlConnection)
            {
                await _actions!.OpenBluetoothSettingsAsync();
                if (!_isDisposed) OperationMessage = device.IsPaired ? "Manage this device in Windows, then refresh the list." : "Complete pairing in Windows, then refresh your devices.";
                return;
            }
            var token = _lifetimeCancellation.Token;
            var message = await _actions!.SetConnectionAsync(device, device.ConnectionStatus != DeviceConnectionStatus.Connected, token);
            await Task.Delay(1000, token);
            if (_isDisposed) return;
            await RefreshAsync();
            if (!_isDisposed) OperationMessage = message;
        }
        catch (OperationCanceledException) when (_isDisposed) { }
        catch (Exception exception) { HandleActionError(exception); }
        finally { if (!_isDisposed) IsWorking = false; }
    }

    private async Task ChooseImageAsync(object? parameter)
    {
        if (parameter is not BluetoothDeviceInfo device || !CanAct()) return;
        IsWorking = true;
        try
        {
            var image = await _actions!.ChooseImageAsync(device);
            if (image is null || _isDisposed) return;
            var updated = device with { ImageBytes = image };
            var index = Devices.IndexOf(device);
            if (index >= 0) Devices[index] = updated;
            if (SelectedDevice?.DeviceId == device.DeviceId) SelectedDevice = updated;
        }
        catch (Exception exception) { HandleActionError(exception); }
        finally { if (!_isDisposed) IsWorking = false; }
    }

    private void HandleActionError(Exception exception)
    {
        if (!_isDisposed) OperationMessage = exception is NotSupportedException or InvalidOperationException ? exception.Message : "The action couldn't be completed. Try again or use Windows Bluetooth Settings.";
    }

    private void NotifyActionCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        DeviceActionCommand.NotifyCanExecuteChanged();
        ChooseImageCommand.NotifyCanExecuteChanged();
        SettingsCommand.NotifyCanExecuteChanged();
    }

    private void HandleRefreshError(Exception exception)
    {
        if (_isDisposed)
        {
            return;
        }

        System.Diagnostics.Debug.WriteLine($"Bluetooth refresh failed: {exception}");
        ErrorMessage = HasDevices
            ? "Couldn't refresh your devices. Showing the last successful results. Try refreshing again."
            : "Couldn't load your Bluetooth devices. Try refreshing again.";
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SummaryText));
    }
}
