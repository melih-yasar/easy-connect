using System.Diagnostics;
using EasyConnect.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using static EasyConnect.Services.BluetoothDeviceProperties;

namespace EasyConnect.Services;

public sealed partial class BluetoothService : IBluetoothService, IDeviceActionsService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

    private static readonly string[] EndpointProperties =
    [
        IsConnected, AepContainerId, AepCategory, BatteryLife
    ];

    private static readonly string[] NodeProperties = [AepId, ContainerId, BatteryLife];
    private static readonly string[] ContainerProperties = [BatteryLife, Category];

    public Task<BluetoothScanResult> GetPairedDevicesAsync(CancellationToken cancellationToken = default)
    {
        // Even WinRT activation and property projection can do synchronous work. Keep it off WPF's dispatcher.
        return Task.Run(() => ScanAsync(cancellationToken), cancellationToken);
    }

    private async Task<BluetoothScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        // Windows' paired selectors also suppress radio inquiry. A hand-built paired-only
        // AQS filter can wait for discovery even when the paired list is already available.
        var queries = await Task.WhenAll(
            QueryAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), EndpointProperties,
                DeviceInformationKind.AssociationEndpoint, cancellationToken),
            QueryAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), EndpointProperties,
                DeviceInformationKind.AssociationEndpoint, cancellationToken)).ConfigureAwait(false);

        if (queries.All(query => query.Failed))
        {
            throw new InvalidOperationException("Windows could not read paired Bluetooth devices. Check Bluetooth in Windows Settings and try again.");
        }

        var warnings = new List<string>();
        if (queries.Any(query => query.Failed))
        {
            warnings.Add("Windows could not read all Bluetooth devices. Some devices may be missing.");
        }

        var endpoints = new List<Endpoint>();
        foreach (var device in queries.SelectMany(query => query.Devices))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                endpoints.Add(ReadEndpoint(device));
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Bluetooth endpoint properties unavailable: {exception.GetType().Name}");
                // A bad optional property must not hide an otherwise enumerated paired device.
                endpoints.Add(new Endpoint(
                    new BluetoothDeviceInfo(device.Name, device.Id, DeviceConnectionStatus.Unknown, null),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "endpoint:" + device.Id }));
            }
        }

        if (endpoints.Count > 0)
        {
            await EnrichAsync(endpoints, warnings, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var devices = MergeEndpoints(endpoints)
            .OrderByDescending(device => device.ConnectionStatus == DeviceConnectionStatus.Connected)
            .ThenBy(device => device.DeviceName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        await DecorateAsync(devices, cancellationToken).ConfigureAwait(false);

        return new BluetoothScanResult(devices, warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    private static Endpoint ReadEndpoint(DeviceInformation device)
    {
        var properties = device.Properties;
        var status = Get(properties, IsConnected) is bool connected
            ? connected ? DeviceConnectionStatus.Connected : DeviceConnectionStatus.Disconnected
            : DeviceConnectionStatus.Unknown;
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "endpoint:" + device.Id };

        if (ReadContainerId(Get(properties, AepContainerId)) is { } containerId)
        {
            identities.Add("aep-container:" + containerId);
        }

        // Presence and pairing do not imply a live Bluetooth connection.
        return new Endpoint(new BluetoothDeviceInfo(
            ReadString(device.Name) ?? "Unnamed Bluetooth device",
            device.Id,
            status,
            ReadBattery(Get(properties, BatteryLife)),
            ReadCategory(Get(properties, AepCategory))), identities);
    }

    private static async Task EnrichAsync(
        List<Endpoint> endpoints, List<string> warnings, CancellationToken cancellationToken)
    {
        // Read local Windows metadata only. Opening a Bluetooth/GATT device can trigger consent or a connection.
        var queries = await Task.WhenAll(
            QueryAsync(string.Empty, NodeProperties, DeviceInformationKind.Device, cancellationToken),
            QueryAsync(string.Empty, ContainerProperties, DeviceInformationKind.DeviceContainer, cancellationToken))
            .ConfigureAwait(false);

        if (queries.Any(query => query.Failed))
        {
            warnings.Add("Some optional battery and device details are unavailable.");
        }

        var nodes = new List<NodeMetadata>();
        var containers = new Dictionary<Guid, ContainerMetadata>();
        foreach (var node in queries[0].Devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                nodes.Add(new NodeMetadata(
                    ReadString(Get(node.Properties, AepId)),
                    ReadContainerId(Get(node.Properties, ContainerId)),
                    ReadBattery(Get(node.Properties, BatteryLife))));
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Optional Bluetooth device properties unavailable: {exception.GetType().Name}");
            }
        }

        foreach (var container in queries[1].Devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (ReadContainerId(container.Id) is { } id)
                {
                    containers[id] = new ContainerMetadata(
                        ReadBattery(Get(container.Properties, BatteryLife)),
                        ReadCategory(Get(container.Properties, Category)));
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Optional Bluetooth container properties unavailable: {exception.GetType().Name}");
            }
        }

        var nodesByAep = nodes.Where(node => node.AepId is not null)
            .ToLookup(node => node.AepId!, StringComparer.OrdinalIgnoreCase);
        var nodesByContainer = nodes.Where(node => node.ContainerId.HasValue).ToLookup(node => node.ContainerId!.Value);

        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var battery = endpoint.Device.BatteryPercentage;
            var category = endpoint.Device.Category;
            foreach (var node in nodesByAep[endpoint.Device.DeviceId])
            {
                battery ??= node.Battery;
                if (node.ContainerId is not { } id)
                {
                    continue;
                }

                // AEP containers and PnP containers have different IDs; link through the devnode's AepId.
                endpoint.Identities.Add("pnp-container:" + id);
                endpoint.Device = endpoint.Device with { PnpContainerId = id };
                if (containers.TryGetValue(id, out var container))
                {
                    battery ??= container.Battery;
                    category ??= container.Category;
                }

                battery ??= nodesByContainer[id].Select(child => child.Battery).FirstOrDefault(value => value.HasValue);
            }

            endpoint.Device = endpoint.Device with { BatteryPercentage = battery, Category = category };
        }
    }

    private static IEnumerable<BluetoothDeviceInfo> MergeEndpoints(IEnumerable<Endpoint> endpoints)
    {
        var groups = new List<Endpoint>();
        foreach (var endpoint in endpoints)
        {
            // Only Windows identity links merge endpoints. Equal names can belong to different physical devices.
            var matches = groups.Where(group => group.Identities.Overlaps(endpoint.Identities)).ToArray();
            foreach (var match in matches)
            {
                endpoint.Identities.UnionWith(match.Identities);
                endpoint.Device = MergeDeviceInfo(endpoint.Device, match.Device);
                groups.Remove(match);
            }

            groups.Add(endpoint);
        }

        return groups.Select(group => group.Device);
    }

    private static BluetoothDeviceInfo MergeDeviceInfo(BluetoothDeviceInfo first, BluetoothDeviceInfo second)
    {
        var status = first.ConnectionStatus == DeviceConnectionStatus.Connected || second.ConnectionStatus == DeviceConnectionStatus.Connected
            ? DeviceConnectionStatus.Connected
            : first.ConnectionStatus == DeviceConnectionStatus.Unknown || second.ConnectionStatus == DeviceConnectionStatus.Unknown
                ? DeviceConnectionStatus.Unknown
                : DeviceConnectionStatus.Disconnected;
        var preferred = second.ConnectionStatus == DeviceConnectionStatus.Connected ? second : first;
        var other = ReferenceEquals(preferred, second) ? first : second;
        return preferred with
        {
            ConnectionStatus = status,
            BatteryPercentage = preferred.BatteryPercentage ?? other.BatteryPercentage,
            Category = preferred.Category ?? other.Category
            ,PnpContainerId = preferred.PnpContainerId ?? other.PnpContainerId
        };
    }

    private static async Task<QueryResult> QueryAsync(
        string selector, string[] properties, DeviceInformationKind kind, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QueryTimeout);
        try
        {
            var operation = DeviceInformation.FindAllAsync(selector, properties, kind).AsTask(timeout.Token);
            // WaitAsync also bounds the wait if a Windows provider is slow to honor cancellation.
            var devices = await operation.WaitAsync(QueryTimeout, cancellationToken).ConfigureAwait(false);
            return new QueryResult(devices.ToArray(), false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.TraceWarning($"Windows device enumeration ({kind}) failed: {exception.GetType().Name} (0x{exception.HResult:X8}): {exception.Message}");
            return new QueryResult([], true);
        }
        finally
        {
            timeout.Cancel();
        }
    }

    private sealed class Endpoint(BluetoothDeviceInfo device, HashSet<string> identities)
    {
        public BluetoothDeviceInfo Device { get; set; } = device;
        public HashSet<string> Identities { get; } = identities;
    }

    private sealed record QueryResult(DeviceInformation[] Devices, bool Failed);
    private sealed record NodeMetadata(string? AepId, Guid? ContainerId, int? Battery);
    private sealed record ContainerMetadata(int? Battery, string? Category);
}
