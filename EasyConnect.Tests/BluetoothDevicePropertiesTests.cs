using EasyConnect.Services;
using Xunit;

namespace EasyConnect.Tests;

public sealed class BluetoothDevicePropertiesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(82, 82)]
    [InlineData(101, null)]
    [InlineData(255, null)]
    [InlineData(-1, null)]
    public void BatterySentinelsNeverBecomePercentages(int raw, int? expected) =>
        Assert.Equal(expected, BluetoothDeviceProperties.ReadBattery(raw));

    [Fact]
    public void BatteryRequiresIntegralWindowsPropertyValue()
    {
        Assert.Equal(65, BluetoothDeviceProperties.ReadBattery((byte)65));
        Assert.Equal(0, BluetoothDeviceProperties.ReadBattery((uint)0));
        Assert.Null(BluetoothDeviceProperties.ReadBattery(null));
        Assert.Null(BluetoothDeviceProperties.ReadBattery("65"));
        Assert.Null(BluetoothDeviceProperties.ReadBattery(65.0));
        Assert.Null(BluetoothDeviceProperties.ReadBattery(true));
        Assert.Null(BluetoothDeviceProperties.ReadBattery(ulong.MaxValue));
    }

    [Fact]
    public void ContainerIdentityRejectsHostEmptyAndMalformedIds()
    {
        Assert.Null(BluetoothDeviceProperties.ReadContainerId(Guid.Empty));
        Assert.Null(BluetoothDeviceProperties.ReadContainerId("{00000000-0000-0000-ffff-ffffffffffff}"));
        Assert.Null(BluetoothDeviceProperties.ReadContainerId("not a container"));
        var id = Guid.NewGuid();
        Assert.Equal(id, BluetoothDeviceProperties.ReadContainerId(id.ToString("B")));
        Assert.Equal(id, BluetoothDeviceProperties.ReadContainerId(id));
    }
}
