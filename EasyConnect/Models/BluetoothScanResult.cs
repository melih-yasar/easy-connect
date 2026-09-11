namespace EasyConnect.Models;

public sealed record BluetoothScanResult(
    IReadOnlyList<BluetoothDeviceInfo> Devices,
    string? Warning = null);
