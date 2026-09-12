using EasyConnect.Models;

namespace EasyConnect.Services;

public interface IDeviceActionsService
{
    Task<BluetoothScanResult> DiscoverNearbyAsync(CancellationToken cancellationToken);
    Task<string> SetConnectionAsync(BluetoothDeviceInfo device, bool connect, CancellationToken cancellationToken);
    Task<string> PairAsync(BluetoothDeviceInfo device, CancellationToken cancellationToken);
    Task OpenBluetoothSettingsAsync();
    Task<byte[]?> ChooseImageAsync(BluetoothDeviceInfo device);
}
