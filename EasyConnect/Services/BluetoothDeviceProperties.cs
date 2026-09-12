namespace EasyConnect.Services;

internal static class BluetoothDeviceProperties
{
    internal const string AepId = "System.Devices.Aep.AepId";
    internal const string AepContainerId = "System.Devices.Aep.ContainerId";
    internal const string ContainerId = "System.Devices.ContainerId";
    internal const string IsConnected = "System.Devices.Aep.IsConnected";
    internal const string BatteryLife = "System.Devices.BatteryLife";
    internal const string AepCategory = "System.Devices.Aep.Category";
    internal const string Category = "System.Devices.Category";
    internal const string SignalStrength = "System.Devices.Aep.SignalStrength";

    internal static object? Get(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) ? value : null;

    internal static string? ReadString(object? value) =>
        value is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    internal static Guid? ReadContainerId(object? value)
    {
        var id = value is Guid guid ? guid : Guid.TryParse(value as string, out guid) ? guid : Guid.Empty;
        // Windows' local-machine container is not an identity for a Bluetooth peripheral.
        return id == Guid.Empty || id == new Guid("00000000-0000-0000-ffff-ffffffffffff") ? null : id;
    }

    internal static int? ReadBattery(object? value)
    {
        // BatteryLife is normally a byte. Values above 100 are unknown, not percentages.
        return value switch
        {
            byte number when number <= 100 => number,
            sbyte number when number is >= 0 and <= 100 => number,
            short number when number is >= 0 and <= 100 => number,
            ushort number when number <= 100 => number,
            int number when number is >= 0 and <= 100 => number,
            uint number when number <= 100 => (int)number,
            long number when number is >= 0 and <= 100 => (int)number,
            ulong number when number <= 100 => (int)number,
            _ => null
        };
    }

    internal static int? ReadSignalStrength(object? value) => value switch
    {
        sbyte number => number,
        short number => number,
        int number => number,
        long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
        byte number => number,
        ushort number => number,
        uint number when number <= int.MaxValue => (int)number,
        ulong number when number <= int.MaxValue => (int)number,
        _ => null
    };

    internal static string? ReadCategory(object? value) => value switch
    {
        string text => ReadString(text),
        IEnumerable<string> categories => categories.Select(ReadString).FirstOrDefault(text => text is not null),
        _ => null
    };
}
