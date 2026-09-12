namespace EasyConnect.Models;

public enum DeviceConnectionStatus
{
    Unknown,
    Disconnected,
    Connected
}

public sealed record BluetoothDeviceInfo(
    string DeviceName,
    string DeviceId,
    DeviceConnectionStatus ConnectionStatus,
    int? BatteryPercentage,
    string? Category = null)
{
    public bool IsPaired { get; init; } = true;
    public bool IsFavorite { get; init; }
    public string? Alias { get; init; }
    public int? SignalStrength { get; init; }
    public Guid? PnpContainerId { get; init; }
    public byte[]? ImageBytes { get; init; }
    public IReadOnlyList<string> ConnectionTargetIds { get; init; } = [];
    public bool CanControlConnection => IsPaired && ConnectionTargetIds.Count > 0;
    public string ImageKey => PnpContainerId?.ToString() ?? DeviceId;
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? DeviceName : Alias.Trim();
    public bool HasBattery => BatteryPercentage is >= 0 and <= 100;
    public string BatteryLabel => HasBattery ? $"{BatteryPercentage}%" : "Battery unavailable";
    public string CategoryLabel => string.IsNullOrWhiteSpace(Category) ? "Other device" :
        System.Text.RegularExpressions.Regex.Replace(Category.Split('.').Last(), "(?<=[a-z])([A-Z])", " $1");
    // RSSI is approximate and hardware-dependent; these labels indicate relative reception.
    public string SignalLabel => SignalStrength switch
    {
        >= -50 => "Excellent", >= -65 => "Good", >= -80 => "Fair",
        not null => "Weak", _ => "Signal unavailable"
    };
    public string SignalDetail => SignalStrength is { } signal ? $"{signal} dBm" : string.Empty;
}
