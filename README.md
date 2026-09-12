# EasyConnect

A Windows Bluetooth manager built with C#, .NET 8, WPF, and MVVM. It helps you review paired devices, discover nearby hardware, manage supported audio connections, and display either Windows artwork or your own product photos.

## Overview

EasyConnect is designed for Windows 10 and later and focuses on a clean Bluetooth device experience without third-party runtime dependencies. The app surfaces connection state, battery details, remote discovery, and supported audio actions in a native WPF interface.

## Features

- Paired-device list that loads on startup and refreshes with the refresh button or F5.
- Connection status states including connected, disconnected, and Status unavailable when Windows does not report a value.
- Nullable battery percentages where 0% is valid and -- represents unavailable data.
- Selected-device preview with Windows-provided artwork and a Choose photo option for custom product images.
- Find nearby support for both Classic and BLE devices for up to 12 seconds.
- Pair in Windows flow for nearby devices that require Windows to complete pairing.
- Connect / Disconnect actions for compatible Bluetooth audio drivers.
- Manage in Windows fallback for devices that do not support inline controls.
- Loading, empty, partial-result, and error states with the last successful result preserved on refresh failure.
- Automatic Windows light/dark theme detection, high-contrast support, and a native Windows appearance.
- Async enumeration, refresh protection, timeouts, and cancellation when the window closes.

The native WPF design is inspired by Apple-style product surfaces: neutral light/dark colors, generous spacing, large preview panes, grouped rows, and restrained blue actions. It keeps Windows-native controls and the Segoe UI font while preserving the semantic theme structure established from HeroUI v3.

## Requirements

- Windows 10 version 2004 (build 19041) or later
- .NET 8 SDK (8.0.4xx or a later .NET 8 feature band)
- Visual Studio with the .NET desktop development workload

## Build and run

```powershell
dotnet restore EasyConnect.sln
dotnet build EasyConnect.sln --no-restore -c Release
dotnet test EasyConnect.sln --no-build --no-restore -c Release
dotnet run --project EasyConnect/EasyConnect.csproj -c Release
```

You can also open `EasyConnect.sln` in Visual Studio and run the `EasyConnect` project. The built executable is:

`EasyConnect/bin/Release/net8.0-windows10.0.19041.0/EasyConnect.exe`

Running the app on another PC requires the .NET 8 Windows Desktop Runtime. Administrator privileges and application packaging are not required.

## Project structure

```text
EasyConnect/
  Models/         Device data, status metadata, and scan results
  Services/       Enumeration, discovery, audio controls, image storage, and abstractions
  ViewModels/     Device list state, refresh lifecycle, and error/loading handling
  Views/          Device rows, photos, and light/dark/high-contrast dictionaries
  Helpers/        Observable base, async command, display converters, and theme helpers
  App.xaml        Shared styles and application composition
  MainWindow.xaml Main application layout
EasyConnect.Tests/
  Refresh, discovery lifecycle, actions, command, and property tests
```

The view model receives `IBluetoothService` and `IDeviceActionsService` through its constructor. Bluetooth logic remains in the service layer, while code-behind only initializes views and forwards lifecycle events. The app has no third-party runtime libraries; it relies on the Windows target framework and the Windows SDK projections. xUnit is used only in the test project.

## Windows Bluetooth limitations

The service uses Windows' built-in paired-device selectors for both Bluetooth transports. These selectors suppress radio inquiry during refresh. It requests association endpoint metadata with `DeviceInformation.FindAllAsync` and enriches it with matching local device and container properties. Only Find nearby starts discovery, and only an explicit connection action sends a device connection request. No GATT sessions or device-specific protocols are used.

- Connection state uses System.Devices.Aep.IsConnected. Pairing or presence alone is never treated as a connection. Values are snapshots and may lag behind a device change; choose Refresh to read again.
- Battery data comes from the documented System.Devices.BatteryLife property on the endpoint or a Windows-linked device or container. Only integral values from 0 to 100 are accepted. Windows sentinel values such as 101 or higher, missing values, and unexpected types become null.
- Some devices and drivers do not publish a battery property even when Windows Settings or a manufacturer app can display a charge level through another path. Values can also be cached, especially for disconnected devices.
- Classic and BLE entries are merged only when Windows supplies a shared device identity. Two devices with the same name remain separate, and missing identity links can leave separate transport entries.
- Each enumeration phase is bounded to 10 seconds. A failed transport produces a partial-result warning; failure of both transports produces a retryable error. Optional metadata failures never intentionally remove paired devices.
- Audio controls are detected through Core Audio container identity and driver capability checks. The app uses the documented `KSPROPSETID_BtAudio` reconnect/disconnect requests on matching audio adapters. A request being accepted does not guarantee the connection changed; the UI refreshes the Windows-reported state rather than assuming a successful change.
- These controls are not universal Bluetooth power switches. HID devices, generic BLE devices, phones, and unsupported audio profiles may still require Windows Settings or their own power controls.
- Discovery requires Bluetooth to be enabled and the remote device to be discoverable. Nearby results are a snapshot rather than proof that a device remains in range. Pairing and PIN confirmation still occur in Windows Settings.

## Device images

`DeviceInformation.GetThumbnailAsync` supplies Windows' available artwork. It may be an actual manufacturer image, a generic category picture, or entirely missing. The app does not guess an exact model or download product images based on a user-editable device name; a category glyph is the final fallback.

Select a paired device and use Choose photo to provide its real product image. PNG, JPEG, and BMP inputs are resized and saved as PNG under `%LOCALAPPDATA%/EasyConnect/DeviceImages`, keyed by a hash of the Windows device identity. The photo persists across refreshes and restarts, and selecting another photo replaces the existing one. No image is uploaded anywhere.

The app currently does not include in-app pairing or PIN dialogs, tray support, popup animations, separate earbud or case batteries, ANC controls, or auto-start behavior.

## References

Windows API references:

- [Device information properties](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/device-information-properties)
- [Device identity kinds](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformationkind)
- [Battery property](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-batterylife)
- [Paired Bluetooth selectors](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdevice.getdeviceselectorfrompairingstate)

Connection and image references:

- [Reconnect request](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-reconnect)
- [Disconnect request](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-disconnect)
- [Core Audio / IKsControl](https://learn.microsoft.com/en-us/windows/win32/coreaudio/using-the-ikscontrol-interface-to-access-audio-properties)
- [Windows device thumbnails](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformation.getthumbnailasync)
