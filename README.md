# EasyConnect

A small Windows Bluetooth device viewer built with **C#, .NET 8, WPF, and MVVM**. It lists paired Bluetooth Classic and Bluetooth Low Energy devices, their Windows-reported connection state, and battery percentage when available.

## Build and run

Requirements: Windows 10 version 2004 (build 19041) or later, and the .NET 8 SDK (8.0.4xx or a later .NET 8 feature band). Visual Studio users need the **.NET desktop development** workload.

```powershell
dotnet restore EasyConnect.sln
dotnet build EasyConnect.sln --no-restore -c Release
dotnet test EasyConnect.sln --no-build --no-restore -c Release
dotnet run --project EasyConnect/EasyConnect.csproj -c Release
```

Or open `EasyConnect.sln` in Visual Studio and run the `EasyConnect` project. The built executable is `EasyConnect/bin/Release/net8.0-windows10.0.19041.0/EasyConnect.exe`; running it on another PC requires the .NET 8 Windows Desktop Runtime. Administrator privileges and application packaging are not required.

## Included in this milestone

- A paired-device list that loads on startup and refreshes with the button or **F5**.
- Connected, disconnected, or **Status unavailable** when Windows does not report a connection state.
- Nullable battery percentages: **0%** is a valid reading; **--** means unavailable.
- Device category icons with a generic Bluetooth fallback.
- Loading, empty, partial-result, and error states. A failed refresh preserves the last successful list and its timestamp.
- Automatic Windows light/dark theme detection, a dark title bar on Windows 11, and high-contrast color resources.
- Asynchronous device enumeration, timeouts, refresh concurrency protection, and cancellation when the window closes.

The native WPF design takes its visual direction from [HeroUI v3](https://heroui.com/en/docs/react/getting-started/colors): semantic color roles, rounded cards and primary buttons, soft status chips, and restrained spacing. HeroUI's React/CSS components are not embedded in this desktop application; their design is adapted into XAML styles and theme resources.

## Structure

```text
EasyConnect/
  Models/         Device data, connection status, scan result
  Services/       IBluetoothService, Windows enumeration, property parsing
  ViewModels/     Device list and refresh/error/loading state
  Views/          Device card and light/dark/high-contrast dictionaries
  Helpers/        Observable base, async command, display converters, themes
  App.xaml        Shared styles and application composition
  MainWindow.xaml Main layout
EasyConnect.Tests/ Refresh lifecycle, command, and Windows-property tests
```

The view model receives `IBluetoothService` through its constructor. Bluetooth code stays in the service. Code-behind only initializes views and forwards lifecycle events. The app has no third-party runtime libraries; the Windows target framework supplies the Windows SDK projections. xUnit is used only by the test project.

## Windows Bluetooth limitations

The service uses Windows' built-in paired-device selectors for both Bluetooth transports. These selectors suppress radio inquiry, avoiding the delays caused by a hand-written pairing filter. It requests association endpoint metadata with `DeviceInformation.FindAllAsync`, then enriches it with matching local device/container properties. It does not open GATT sessions or initiate Bluetooth connections.

- Connection state uses **System.Devices.Aep.IsConnected**. Pairing or presence alone is never treated as a connection. Values are snapshots and may lag behind a device change; select Refresh to read again.
- Battery uses the documented **System.Devices.BatteryLife** property on the endpoint or a Windows-linked device/container. Only integral values from 0 to 100 are accepted. Windows' unknown sentinel (101 or higher), missing values, and unexpected types become `null`.
- Some devices and drivers never publish that battery property, even when Windows Settings or a manufacturer's app can show a charge level through another mechanism. Values can also be cached, especially for disconnected devices. This milestone does not add GATT polling, undocumented driver keys, or manufacturer-specific protocols.
- Classic and LE entries are merged only when Windows supplies a shared device identity. Two devices with the same name remain separate. Missing identity links can leave separate transport entries.
- Each enumeration phase is bounded to 10 seconds. A failed transport produces a partial-result warning; failure of both produces a retryable error. Optional metadata failures never intentionally remove paired devices.

Pairing, connect/disconnect actions, tray support, popup animations, separate earbud/case batteries, ANC controls, and auto-start are outside this milestone.

Windows API references: [device information properties](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/device-information-properties), [device identity kinds](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformationkind), [battery property](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-batterylife), and [paired Bluetooth selectors](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdevice.getdeviceselectorfrompairingstate).
