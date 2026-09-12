using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Threading;
using EasyConnect.Helpers;
using EasyConnect.Models;
using EasyConnect.Services;

namespace EasyConnect.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IBluetoothService _bluetoothService;
    private readonly IDeviceActionsService? _actions;
    private readonly UserDevicePreferences _devicePreferences;
    private readonly AppSettingsStore _settingsStore;
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly HashSet<string> _lowBatteryNotified = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoading;
    private bool _isDisposed;
    private int _refreshInProgress;
    private string? _errorMessage;
    private string? _warningMessage;
    private DateTimeOffset? _lastUpdated;
    private CancellationTokenSource? _discoveryCancellation;
    private bool _isDiscovering;
    private bool _isWorking;
    private bool _hasSearched;
    private string _discoveryMessage = "Find headphones, speakers, and other devices around you.";
    private string? _operationMessage;
    private BluetoothDeviceInfo? _selectedDevice;
    private string _searchText = string.Empty;
    private string _selectedAlias = string.Empty;
    private bool _showSettings;
    private bool _isEditingDevice;
    private string _editDeviceName = string.Empty;
    private string? _editingDeviceId;
    private AppSettings _settings;
    private readonly EventHandler _refreshTickHandler;

    public MainViewModel(
        IBluetoothService bluetoothService,
        IDeviceActionsService? actions = null,
        UserDevicePreferences? devicePreferences = null,
        AppSettingsStore? settingsStore = null)
    {
        ArgumentNullException.ThrowIfNull(bluetoothService);
        _bluetoothService = bluetoothService;
        _actions = actions ?? bluetoothService as IDeviceActionsService;
        _devicePreferences = devicePreferences ?? new UserDevicePreferences();
        _settingsStore = settingsStore ?? new AppSettingsStore();
        _settings = NormalizeSettings(_settingsStore.Load());

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, HandleRefreshError, () => !IsLoading && !IsWorking && !_isDisposed);
        DiscoverCommand = new AsyncRelayCommand(DiscoverAsync, HandleActionError, () => _actions is not null && !IsDiscovering && !_isDisposed);
        StopDiscoveryCommand = new AsyncRelayCommand(() => { _discoveryCancellation?.Cancel(); return Task.CompletedTask; }, HandleActionError, () => IsDiscovering);
        DeviceActionCommand = new AsyncRelayCommand(ActOnDeviceAsync, HandleActionError, CanAct);
        PairDeviceCommand = new AsyncRelayCommand(PairDeviceAsync, HandleActionError, CanAct);
        ChooseImageCommand = new AsyncRelayCommand(ChooseImageAsync, HandleActionError, CanAct);
        SettingsCommand = new AsyncRelayCommand(() => _actions!.OpenBluetoothSettingsAsync(), HandleActionError, CanAct);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, HandleActionError, () => SelectedDevice is not null && !_isDisposed);
        SaveAliasCommand = new AsyncRelayCommand(SaveAliasAsync, HandleActionError, () => SelectedDevice is not null && !_isDisposed);
        ToggleSettingsCommand = new AsyncRelayCommand(() => { ShowSettings = !ShowSettings; return Task.CompletedTask; }, HandleActionError);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, HandleActionError, () => !_isDisposed);
        EditDeviceCommand = new AsyncRelayCommand(() =>
        {
            _editingDeviceId = SelectedDevice?.DeviceId;
            EditDeviceName = SelectedDevice?.DisplayName ?? string.Empty;
            IsEditingDevice = _editingDeviceId is not null;
            return Task.CompletedTask;
        }, HandleActionError);
        CancelEditCommand = new AsyncRelayCommand(() => { IsEditingDevice = false; return Task.CompletedTask; }, HandleActionError);
        SaveDeviceEditCommand = new AsyncRelayCommand(() =>
        {
            var device = Devices.FirstOrDefault(item => item.DeviceId == _editingDeviceId);
            if (device is null) { IsEditingDevice = false; return Task.CompletedTask; }
            var name = EditDeviceName.Trim();
            var alias = name.Length == 0 || name == device.DeviceName ? null : name;
            _devicePreferences.SetAlias(device, alias);
            ReplaceDevice(device, device with { Alias = alias });
            IsEditingDevice = false;
            return Task.CompletedTask;
        }, HandleActionError);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds) };
        _refreshTickHandler = async (_, _) => await RefreshAsync();
        _refreshTimer.Tick += _refreshTickHandler;
        if (AutomaticRefresh) _refreshTimer.Start();
        Devices.CollectionChanged += OnDevicesChanged;
    }

    public event EventHandler<AppNotification>? NotificationRequested;
    public event EventHandler<AppSettings>? SettingsSaved;

    public ObservableCollection<BluetoothDeviceInfo> Devices { get; } = [];
    public ObservableCollection<BluetoothDeviceInfo> NearbyDevices { get; } = [];
    public ObservableCollection<BluetoothDeviceInfo> VisibleDevices { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand DiscoverCommand { get; }
    public AsyncRelayCommand StopDiscoveryCommand { get; }
    public AsyncRelayCommand DeviceActionCommand { get; }
    public AsyncRelayCommand PairDeviceCommand { get; }
    public AsyncRelayCommand ChooseImageCommand { get; }
    public AsyncRelayCommand SettingsCommand { get; }
    public AsyncRelayCommand ToggleFavoriteCommand { get; }
    public AsyncRelayCommand SaveAliasCommand { get; }
    public AsyncRelayCommand ToggleSettingsCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand EditDeviceCommand { get; }
    public AsyncRelayCommand CancelEditCommand { get; }
    public AsyncRelayCommand SaveDeviceEditCommand { get; }
    public bool IsEditingDevice { get => _isEditingDevice; private set => SetProperty(ref _isEditingDevice, value); }
    public string EditDeviceName { get => _editDeviceName; set => SetProperty(ref _editDeviceName, value); }

    public BluetoothDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value)) return;
            SelectedAlias = value?.Alias ?? string.Empty;
            OnPropertyChanged(nameof(HasSelectedDevice));
            OnPropertyChanged(nameof(SelectedDeviceCategoryText));
            OnPropertyChanged(nameof(SelectedDeviceSignalText));
            OnPropertyChanged(nameof(SelectedDeviceIdText));
            ToggleFavoriteCommand.NotifyCanExecuteChanged();
            SaveAliasCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasSelectedDevice => SelectedDevice is not null;
    public string SelectedDeviceCategoryText => SelectedDevice?.CategoryLabel ?? "Other device";
    public string SelectedDeviceSignalText => SelectedDevice?.SignalLabel ?? "Signal unavailable";
    public string SelectedDeviceIdText => SelectedDevice?.DeviceId ?? string.Empty;

    public string SelectedAlias
    {
        get => _selectedAlias;
        set => SetProperty(ref _selectedAlias, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            RefreshVisibleDevices();
            OnPropertyChanged(nameof(FilteredDeviceCountText));
        }
    }

    // Navigation state belongs to the view model; the window selects the matching view.
    public bool ShowSettings { get => _showSettings; set => SetProperty(ref _showSettings, value); }
    public string SettingsStatus { get; private set; } = "Changes are saved automatically.";
    public string MonitoringText => AutomaticRefresh ? $"Monitoring \u00B7 every {RefreshIntervalSeconds}s" : "Monitoring paused";
    public bool ShowConnectedOnly { get => CompactMode; set => CompactMode = value; }
    public AppTheme Theme
    {
        get => _settings.Theme;
        set { var next = value; if (_settings.Theme == next) return; _settings.Theme = next; OnPropertyChanged(); ApplySettings(); }
    }
    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set { var next = value; if (_settings.StartWithWindows == next) return; _settings.StartWithWindows = next; OnPropertyChanged(); ApplySettings(); }
    }
    public bool StartMinimized
    {
        get => _settings.StartMinimized;
        set { var next = value; if (_settings.StartMinimized == next) return; _settings.StartMinimized = next; OnPropertyChanged(); ApplySettings(); }
    }
    public bool CompactMode
    {
        get => _settings.CompactMode;
        set { var next = value; if (_settings.CompactMode == next) return; _settings.CompactMode = next; OnPropertyChanged(); ApplySettings(); }
    }
    public bool CompactConnectedOnly
    {
        get => _settings.CompactConnectedOnly;
        set { var next = value; if (_settings.CompactConnectedOnly == next) return; _settings.CompactConnectedOnly = next; OnPropertyChanged(); ApplySettings(); }
    }
    public bool ShowConnectionPopup
    {
        get => _settings.ShowConnectionPopup;
        set { var next = value; if (_settings.ShowConnectionPopup == next) return; _settings.ShowConnectionPopup = next; OnPropertyChanged(); ApplySettings(); }
    }
    public bool ShowDisconnectPopup
    {
        get => _settings.ShowDisconnectPopup;
        set { var next = value; if (_settings.ShowDisconnectPopup == next) return; _settings.ShowDisconnectPopup = next; OnPropertyChanged(); ApplySettings(); }
    }
    public bool LowBatteryNotifications
    {
        get => _settings.LowBatteryNotifications;
        set { var next = value; if (_settings.LowBatteryNotifications == next) return; _settings.LowBatteryNotifications = next; OnPropertyChanged(); ApplySettings(); }
    }
    public bool AutomaticRefresh
    {
        get => _settings.AutomaticRefresh;
        set { var next = value; if (_settings.AutomaticRefresh == next) return; _settings.AutomaticRefresh = next; OnPropertyChanged(); ApplySettings(); }
    }
    public int PopupDurationSeconds
    {
        get => _settings.PopupDurationSeconds;
        set { var next = Math.Clamp(value, 2, 15); if (_settings.PopupDurationSeconds == next) return; _settings.PopupDurationSeconds = next; OnPropertyChanged(); ApplySettings(); }
    }
    public int RefreshIntervalSeconds
    {
        get => _settings.RefreshIntervalSeconds;
        set { var next = Math.Clamp(value, 5, 300); if (_settings.RefreshIntervalSeconds == next) return; _settings.RefreshIntervalSeconds = next; OnPropertyChanged(); ApplySettings(); }
    }
    public int LowBatteryThreshold
    {
        get => _settings.LowBatteryThreshold;
        set { var next = Math.Clamp(value, 5, 50); if (_settings.LowBatteryThreshold == next) return; _settings.LowBatteryThreshold = next; OnPropertyChanged(); ApplySettings(); }
    }

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
            OnPropertyChanged(nameof(ShowNearbyPlaceholder));
        }
    }
    public string DiscoveryMessage { get => _discoveryMessage; private set => SetProperty(ref _discoveryMessage, value); }
    public bool ShowNearbyPlaceholder => !IsDiscovering && NearbyDevices.Count == 0;
    public bool ShowNearbyEmpty => !IsDiscovering && _hasSearched && NearbyDevices.Count == 0;
    public string? OperationMessage { get => _operationMessage; private set { if (SetProperty(ref _operationMessage, value)) OnPropertyChanged(nameof(HasOperationMessage)); } }
    public bool HasOperationMessage => !string.IsNullOrEmpty(OperationMessage);

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            OnPropertyChanged(nameof(IsEmpty));
            RefreshCommand.NotifyCanExecuteChanged();
            NotifyActionCommands();
        }
    }

    public bool HasDevices => Devices.Count > 0;
    public bool IsEmpty => !IsLoading && !HasError && !HasWarning && !HasDevices;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value)) return;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? WarningMessage
    {
        get => _warningMessage;
        private set
        {
            if (!SetProperty(ref _warningMessage, value)) return;
            OnPropertyChanged(nameof(HasWarning));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningMessage);

    public string SummaryText =>
        $"{Devices.Count} paired {(Devices.Count == 1 ? "device" : "devices")} \u00B7 " +
        $"{Devices.Count(device => device.ConnectionStatus == DeviceConnectionStatus.Connected)} connected";

    public bool HasNoFilterMatches => HasDevices && VisibleDevices.Count == 0;

    public string FilteredDeviceCountText => $"{VisibleDevices.Count} shown";

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

            var previous = Devices.ToDictionary(device => device.DeviceId, StringComparer.OrdinalIgnoreCase);
            var result = await _bluetoothService.GetPairedDevicesAsync(cancellationToken);
            if (_isDisposed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var selectedId = SelectedDevice?.DeviceId;
            Devices.Clear();
            foreach (var device in result.Devices.Select(_devicePreferences.Apply))
            {
                Devices.Add(device);
            }
            SelectedDevice = VisibleDevices.FirstOrDefault(device => device.DeviceId == selectedId) ?? VisibleDevices.FirstOrDefault();

            WarningMessage = result.Warning;
            _lastUpdated = DateTimeOffset.Now;
            OnPropertyChanged(nameof(LastUpdatedText));
            OnPropertyChanged(nameof(FilteredDeviceCountText));
            RaiseNotifications(previous);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
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

    public async Task DiscoverAsync()
    {
        if (_actions is null || IsDiscovering || _isDisposed) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _discoveryCancellation = cancellation;
        IsDiscovering = true;
        DiscoveryMessage = "Searching nearby... Put your device in pairing mode.";
        try
        {
            var result = await _actions.DiscoverNearbyAsync(cancellation.Token);
            if (_isDisposed || cancellation.IsCancellationRequested) return;
            NearbyDevices.Clear();
            foreach (var device in result.Devices
                         .Where(device => !device.IsPaired && !Devices.Any(paired => paired.DeviceId == device.DeviceId))
                         .Select(_devicePreferences.Apply))
            {
                NearbyDevices.Add(device);
            }
            _hasSearched = result.Warning is null;
            DiscoveryMessage = result.Warning ?? (NearbyDevices.Count == 0
                ? "No discoverable devices found. Move closer and try pairing mode."
                : $"{NearbyDevices.Count} nearby. Select Pair to request pairing through Windows.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_isDisposed) DiscoveryMessage = "Search stopped. Previous results are kept until the next search.";
        }
        catch (Exception)
        {
            if (!_isDisposed) DiscoveryMessage = "Couldn't search nearby. Check Bluetooth in Windows Settings and try again.";
        }
        finally
        {
            _discoveryCancellation = null;
            if (!_isDisposed) IsDiscovering = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= _refreshTickHandler;
        Devices.CollectionChanged -= OnDevicesChanged;
        _lifetimeCancellation.Cancel();
        _discoveryCancellation?.Cancel();
        _lifetimeCancellation.Dispose();
        RefreshCommand.NotifyCanExecuteChanged();
        NotifyActionCommands();
    }

    private bool FilterDevice(BluetoothDeviceInfo device)
    {
        if (CompactMode && CompactConnectedOnly && device.ConnectionStatus != DeviceConnectionStatus.Connected)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return device.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
               device.DeviceName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
               (device.Category?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false);
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
                OperationMessage = device.IsPaired
                    ? "Manage this device in Windows, then refresh the list."
                    : "Complete pairing in Windows, then refresh your devices.";
                return;
            }

            var token = _lifetimeCancellation.Token;
            var message = await _actions!.SetConnectionAsync(device, device.ConnectionStatus != DeviceConnectionStatus.Connected, token);
            await Task.Delay(1000, token);
            if (_isDisposed) return;
            await RefreshAsync();
            OperationMessage = message;
        }
        catch (OperationCanceledException) when (_isDisposed) { }
        catch (Exception exception) { HandleActionError(exception); }
        finally { if (!_isDisposed) IsWorking = false; }
    }

    private async Task PairDeviceAsync(object? parameter)
    {
        if (parameter is not BluetoothDeviceInfo device || _actions is null || !CanAct()) return;
        IsWorking = true;
        OperationMessage = null;
        try
        {
            OperationMessage = await _actions.PairAsync(device, _lifetimeCancellation.Token);
            await Task.Delay(1000, _lifetimeCancellation.Token);
            await RefreshAsync();
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
            ReplaceDevice(device, device with { ImageBytes = image });
        }
        catch (Exception exception) { HandleActionError(exception); }
        finally { if (!_isDisposed) IsWorking = false; }
    }

    private Task ToggleFavoriteAsync(object? parameter)
    {
        var device = parameter as BluetoothDeviceInfo ?? SelectedDevice;
        if (device is null) return Task.CompletedTask;
        var updated = device with { IsFavorite = !device.IsFavorite };
        _devicePreferences.SetFavorite(updated, updated.IsFavorite);
        ReplaceDevice(device, updated);
        OperationMessage = updated.IsFavorite ? "Device pinned." : "Device unpinned.";
        return Task.CompletedTask;
    }

    private Task SaveAliasAsync(object? parameter)
    {
        var device = parameter as BluetoothDeviceInfo ?? SelectedDevice;
        if (device is null) return Task.CompletedTask;
        var alias = string.IsNullOrWhiteSpace(SelectedAlias) ? null : SelectedAlias.Trim();
        _devicePreferences.SetAlias(device, alias);
        ReplaceDevice(device, device with { Alias = alias });
        OperationMessage = alias is null ? "Alias cleared." : "Alias saved.";
        return Task.CompletedTask;
    }

    private Task SaveSettingsAsync()
    {
        ApplySettings();
        return Task.CompletedTask;
    }

    private void ApplySettings()
    {
        _settings = NormalizeSettings(_settings);
        _refreshTimer.Interval = TimeSpan.FromSeconds(RefreshIntervalSeconds);
        if (AutomaticRefresh && !_isDisposed) _refreshTimer.Start(); else _refreshTimer.Stop();
        RefreshVisibleDevices();
        OnPropertyChanged(nameof(FilteredDeviceCountText));
        OnPropertyChanged(nameof(MonitoringText));
        OnPropertyChanged(nameof(ShowConnectedOnly));
        try
        {
            _settingsStore.Save(_settings);
            SettingsSaved?.Invoke(this, _settings);
            SettingsStatus = "Changes saved.";
        }
        catch (Exception)
        {
            SettingsStatus = "Changes applied, but couldn't save them. Check access to your local app data folder.";
        }
        OnPropertyChanged(nameof(SettingsStatus));
    }

    private void ReplaceDevice(BluetoothDeviceInfo oldDevice, BluetoothDeviceInfo newDevice)
    {
        var index = Devices.IndexOf(oldDevice);
        if (index >= 0)
        {
            Devices[index] = newDevice;
        }

        var nearbyIndex = NearbyDevices.IndexOf(oldDevice);
        if (nearbyIndex >= 0)
        {
            NearbyDevices[nearbyIndex] = newDevice;
        }

        if (SelectedDevice?.DeviceId == oldDevice.DeviceId)
        {
            SelectedDevice = newDevice;
        }

        RefreshVisibleDevices();
        OnPropertyChanged(nameof(FilteredDeviceCountText));
    }

    private void RaiseNotifications(IReadOnlyDictionary<string, BluetoothDeviceInfo> previous)
    {
        foreach (var device in Devices)
        {
            // Only confirmed transitions trigger popups, not the initial scan or unknown states.
            if (previous.TryGetValue(device.DeviceId, out var old))
            {
                if (ShowConnectionPopup && device.ConnectionStatus == DeviceConnectionStatus.Connected &&
                    old.ConnectionStatus == DeviceConnectionStatus.Disconnected)
                    NotificationRequested?.Invoke(this, new AppNotification("Connected", device));
                if (ShowDisconnectPopup && device.ConnectionStatus == DeviceConnectionStatus.Disconnected &&
                    old.ConnectionStatus == DeviceConnectionStatus.Connected)
                    NotificationRequested?.Invoke(this, new AppNotification("Disconnected", device));
            }
            if (device.BatteryPercentage > LowBatteryThreshold) _lowBatteryNotified.Remove(device.DeviceId);
            if (LowBatteryNotifications && device.ConnectionStatus == DeviceConnectionStatus.Connected &&
                device.BatteryPercentage is { } battery && battery <= LowBatteryThreshold && _lowBatteryNotified.Add(device.DeviceId))
                NotificationRequested?.Invoke(this, new AppNotification("Low battery", device, true));
        }
    }

    private void HandleActionError(Exception exception)
    {
        if (!_isDisposed)
        {
            OperationMessage = exception is NotSupportedException or InvalidOperationException
                ? exception.Message
                : "The action couldn't be completed. Try again or use Windows Bluetooth Settings.";
        }
    }

    private void NotifyActionCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        DeviceActionCommand.NotifyCanExecuteChanged();
        PairDeviceCommand.NotifyCanExecuteChanged();
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
        RefreshVisibleDevices();
        OnPropertyChanged(nameof(FilteredDeviceCountText));
    }

    private void RefreshVisibleDevices()
    {
        var selection = SelectedDevice;
        var selected = selection?.DeviceId;
        var devices = Devices
            .Where(FilterDevice)
            .OrderByDescending(device => device.IsFavorite)
            .ThenByDescending(device => device.ConnectionStatus == DeviceConnectionStatus.Connected)
            .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        VisibleDevices.Clear();
        foreach (var device in devices)
        {
            VisibleDevices.Add(device);
        }

        SelectedDevice = devices.FirstOrDefault(device => device.DeviceId == selected) ?? devices.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoFilterMatches));
    }

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme)) settings.Theme = AppTheme.System;
        settings.PopupDurationSeconds = Math.Clamp(settings.PopupDurationSeconds, 2, 15);
        settings.RefreshIntervalSeconds = Math.Clamp(settings.RefreshIntervalSeconds, 5, 300);
        settings.LowBatteryThreshold = Math.Clamp(settings.LowBatteryThreshold, 5, 50);
        return settings;
    }
}
