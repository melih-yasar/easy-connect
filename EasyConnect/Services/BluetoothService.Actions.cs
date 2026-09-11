using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using EasyConnect.Models;

namespace EasyConnect.Services;

public sealed partial class BluetoothService
{
    private readonly DeviceImageStore _images = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    public Task<BluetoothScanResult> DiscoverNearbyAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var scans = await Task.WhenAll(
            DiscoverTransportAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(false), cancellationToken),
            DiscoverTransportAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(false), cancellationToken)).ConfigureAwait(false);
        if (scans.All(scan => scan.Failed)) throw new InvalidOperationException("Discovery is unavailable. Check that Bluetooth is on in Windows Settings.");
        var devices = MergeEndpoints(scans.SelectMany(scan => scan.Devices).Select(ReadEndpoint))
            .Select(device => device with { IsPaired = false })
            .OrderBy(device => device.DeviceName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        await LoadImagesAsync(devices, cancellationToken).ConfigureAwait(false);
        return new BluetoothScanResult(devices, scans.Any(scan => scan.Failed) ? "Windows could not complete part of the nearby search. Try again." : null);
    }, cancellationToken);

    public async Task<string> SetConnectionAsync(BluetoothDeviceInfo device, bool connect, CancellationToken cancellationToken)
    {
        if (!device.CanControlConnection) throw new NotSupportedException("Use Windows Bluetooth Settings to manage this device's connection.");
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        // Keep the gate until the synchronous driver request actually returns, even after a UI timeout.
        var work = Task.Run(() =>
        {
            try { return BluetoothAudioControl.Request(device.ConnectionTargetIds, connect, timeout.Token); }
            finally { _connectionGate.Release(); }
        });
        int accepted;
        try { accepted = await work.WaitAsync(TimeSpan.FromSeconds(16), cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { throw new InvalidOperationException("The audio driver has not responded yet. Refresh to check the device before trying again."); }
        if (accepted == 0) throw new InvalidOperationException("The device's audio driver did not accept the request. Try Windows Bluetooth Settings.");
        return connect ? "Connection requested. The status below is reported by Windows." : "Disconnection requested. The status below is reported by Windows.";
    }

    public Task OpenBluetoothSettingsAsync()
    {
        Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
        return Task.CompletedTask;
    }

    public Task<byte[]?> ChooseImageAsync(BluetoothDeviceInfo device) => _images.ChooseAsync(device);

    private async Task DecorateAsync(BluetoothDeviceInfo[] devices, CancellationToken token)
    {
        try
        {
            var targets = await Task.Run(() => BluetoothAudioControl.FindTargets(token), token)
                .WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            for (var index = 0; index < devices.Length; index++)
                if (devices[index].PnpContainerId is { } container && targets.TryGetValue(container, out var ids))
                    devices[index] = devices[index] with { ConnectionTargetIds = ids };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { Trace.TraceWarning($"Audio controls unavailable: {exception.GetType().Name}"); }
        await LoadImagesAsync(devices, token).ConfigureAwait(false);
    }

    private async Task LoadImagesAsync(BluetoothDeviceInfo[] devices, CancellationToken token)
    {
        await Parallel.ForEachAsync(Enumerable.Range(0, devices.Length), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (index, ct) =>
        {
            var device = devices[index];
            var bytes = await _images.ReadAsync(device.ImageKey, ct).ConfigureAwait(false);
            if (bytes is null && device.PnpContainerId is { } container)
                bytes = await ReadWindowsImageAsync(container.ToString("B"), DeviceInformationKind.DeviceContainer, ct).ConfigureAwait(false);
            bytes ??= await ReadWindowsImageAsync(device.DeviceId, DeviceInformationKind.AssociationEndpoint, ct).ConfigureAwait(false);
            devices[index] = device with { ImageBytes = bytes };
        }).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadWindowsImageAsync(string id, DeviceInformationKind kind, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(id, [], kind).AsTask(timeout.Token).ConfigureAwait(false);
            using var thumbnail = await device.GetThumbnailAsync().AsTask(timeout.Token).ConfigureAwait(false);
            if (thumbnail.Size == 0 || thumbnail.Size > 2 * 1024 * 1024) return null;
            using var reader = new DataReader(thumbnail.GetInputStreamAt(0));
            await reader.LoadAsync((uint)thumbnail.Size).AsTask(timeout.Token).ConfigureAwait(false);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return null; } // Missing manufacturer artwork is normal.
    }

    private static async Task<QueryResult> DiscoverTransportAsync(string selector, CancellationToken token)
    {
        var watcher = DeviceInformation.CreateWatcher(selector,
            EndpointProperties.Append("System.Devices.Aep.IsPresent"), DeviceInformationKind.AssociationEndpoint);
        var devices = new Dictionary<string, DeviceInformation>(StringComparer.OrdinalIgnoreCase);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Added(DeviceWatcher sender, DeviceInformation device) { lock (devices) devices[device.Id] = device; }
        void Updated(DeviceWatcher sender, DeviceInformationUpdate update) { lock (devices) { if (devices.TryGetValue(update.Id, out var device)) device.Update(update); } }
        void Removed(DeviceWatcher sender, DeviceInformationUpdate update) { lock (devices) devices.Remove(update.Id); }
        void Stopped(DeviceWatcher sender, object args) => stopped.TrySetResult();
        watcher.Added += Added;
        watcher.Updated += Updated;
        watcher.Removed += Removed;
        watcher.Stopped += Stopped;
        try
        {
            watcher.Start();
            await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(12), token), stopped.Task).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (watcher.Status == DeviceWatcherStatus.Aborted) return new([], true);
            lock (devices)
                return new(devices.Values.Where(device => !device.Pairing.IsPaired &&
                    !(device.Properties.TryGetValue("System.Devices.Aep.IsPresent", out var present) && present is false)).ToArray(), false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            Trace.TraceWarning($"Nearby Bluetooth search failed: {exception.GetType().Name}");
            return new([], true);
        }
        finally
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
            watcher.Added -= Added;
            watcher.Updated -= Updated;
            watcher.Removed -= Removed;
            watcher.Stopped -= Stopped;
        }
    }
}
