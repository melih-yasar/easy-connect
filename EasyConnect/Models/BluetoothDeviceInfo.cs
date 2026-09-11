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
    public Guid? PnpContainerId { get; init; }
    public byte[]? ImageBytes { get; init; }
    public IReadOnlyList<string> ConnectionTargetIds { get; init; } = [];
    public bool CanControlConnection => IsPaired && ConnectionTargetIds.Count > 0;
    public string ImageKey => PnpContainerId?.ToString() ?? DeviceId;
}
