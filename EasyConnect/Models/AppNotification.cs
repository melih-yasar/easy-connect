namespace EasyConnect.Models;

public sealed record AppNotification(
    string Title,
    BluetoothDeviceInfo Device,
    bool IsLowBattery = false);
