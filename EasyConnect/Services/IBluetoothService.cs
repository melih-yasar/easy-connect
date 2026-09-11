using EasyConnect.Models;

namespace EasyConnect.Services;

public interface IBluetoothService
{
    Task<BluetoothScanResult> GetPairedDevicesAsync(CancellationToken cancellationToken = default);
}
