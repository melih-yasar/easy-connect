# EasyConnect

A Windows Bluetooth manager built with **C#, .NET 8, WPF, and MVVM**. View paired devices, discover nearby devices, manage supported audio connections, and display device artwork or your own product photos.

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
- A selected-device preview with Windows-provided artwork. **Choose photo** saves your own product image for that device.
- **Find nearby** searches both Classic and BLE devices for 12 seconds. **Stop** cancels the search. Nearby devices expose **Pair in Windows**, which opens Windows Bluetooth Settings to complete pairing.
- **Connect / Disconnect** for Bluetooth audio drivers exposing Windows' one-shot connection controls. Other devices show **Manage in Windows** instead of an unsupported action.
- Loading, empty, partial-result, and error states. A failed refresh preserves the last successful list and its timestamp.
- Automatic Windows light/dark theme detection, a dark title bar on Windows 11, and high-contrast color resources.
- Asynchronous device enumeration, timeouts, refresh concurrency protection, and cancellation when the window closes.

The native WPF design is Apple-inspired: neutral light/dark surfaces, a large device preview, grouped rows, generous spacing, and restrained blue actions. It retains the semantic theme structure established from HeroUI v3. React/CSS components are not embedded, and the app keeps Windows' native window controls and Segoe UI font.

## Structure

```text
EasyConnect/
  Models/         Device data, connection status, scan result
  Services/       Enumeration, discovery, audio controls, image storage, abstractions
  ViewModels/     Device list and refresh/error/loading state
  Views/          Device rows/photos and light/dark/high-contrast dictionaries
  Helpers/        Observable base, async command, display converters, themes
  App.xaml        Shared styles and application composition
  MainWindow.xaml Main layout
EasyConnect.Tests/ Refresh/discovery lifecycle, actions, command, property tests
```

The view model receives `IBluetoothService` and `IDeviceActionsService` through its constructor. Bluetooth code stays in the service. Code-behind only initializes views and forwards lifecycle events. The app has no third-party runtime libraries; the Windows target framework supplies the Windows SDK projections. xUnit is used only by the test project.

## Windows Bluetooth limitations

The service uses Windows' built-in paired-device selectors for both Bluetooth transports. These selectors suppress radio inquiry during refresh. It requests association endpoint metadata with `DeviceInformation.FindAllAsync`, then enriches it with matching local device/container properties. Only **Find nearby** starts discovery; only an explicit connection button sends a connection request. No GATT sessions or device-specific protocols are used.

- Connection state uses **System.Devices.Aep.IsConnected**. Pairing or presence alone is never treated as a connection. Values are snapshots and may lag behind a device change; select Refresh to read again.
- Battery uses the documented **System.Devices.BatteryLife** property on the endpoint or a Windows-linked device/container. Only integral values from 0 to 100 are accepted. Windows' unknown sentinel (101 or higher), missing values, and unexpected types become `null`.
- Some devices and drivers never publish that battery property, even when Windows Settings or a manufacturer's app can show a charge level through another mechanism. Values can also be cached, especially for disconnected devices. This milestone does not add GATT polling, undocumented driver keys, or manufacturer-specific protocols.
- Classic and LE entries are merged only when Windows supplies a shared device identity. Two devices with the same name remain separate. Missing identity links can leave separate transport entries.
- Each enumeration phase is bounded to 10 seconds. A failed transport produces a partial-result warning; failure of both produces a retryable error. Optional metadata failures never intentionally remove paired devices.
- Audio controls are detected through Core Audio container identity and driver capability checks. The app uses the documented `KSPROPSETID_BtAudio` reconnect/disconnect requests on matching audio adapters. A request being accepted does **not** prove the connection changed: the UI refreshes Windows-reported state rather than changing it optimistically. A driver can refuse a request or reconnect automatically.
- These controls are not universal Bluetooth power switches. HID, generic BLE, phones, and unsupported audio profiles may need Windows Settings or their own power button. The app does not disable drivers, remove pairing, or disconnect unrelated devices.
- Discovery requires Bluetooth to be on and the remote device to be discoverable. Nearby results are a snapshot, not proof that a device remains within range. Pairing and PIN confirmation take place in Windows Settings.

## Device images

`DeviceInformation.GetThumbnailAsync` supplies Windows' available artwork. It may be an actual manufacturer image, a generic category picture, or missing entirely. The app does not guess an exact model or download product pictures based on a user-editable device name. A category glyph is the final fallback.

Select a paired device and use **Choose photo** to supply its real product image. PNG/JPEG/BMP input is resized and saved as PNG under `%LOCALAPPDATA%/EasyConnect/DeviceImages`, keyed by a hash of the Windows device identity. The photo persists across refreshes and restarts; selecting another photo replaces it. No image is uploaded anywhere.

In-app pairing/PIN dialogs, tray support, popup animations, separate earbud/case batteries, ANC controls, and auto-start remain outside this version.

Windows API references: [device information properties](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/device-information-properties), [device identity kinds](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformationkind), [battery property](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-batterylife), and [paired Bluetooth selectors](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdevice.getdeviceselectorfrompairingstate).

Connection and image references: [reconnect request](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-reconnect), [disconnect request](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-disconnect), [Core Audio / IKsControl](https://learn.microsoft.com/en-us/windows/win32/coreaudio/using-the-ikscontrol-interface-to-access-audio-properties), and [Windows device thumbnails](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformation.getthumbnailasync).
