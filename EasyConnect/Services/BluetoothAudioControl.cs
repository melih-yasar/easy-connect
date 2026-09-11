using System.Runtime.InteropServices;

namespace EasyConnect.Services;

// Documented Core Audio topology + KSPROPSETID_BtAudio one-shot requests.
// No driver disabling, unpairing, or manufacturer-specific Bluetooth traffic.
internal static class BluetoothAudioControl
{
    private static readonly Guid BluetoothProperties = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");

    public static Dictionary<Guid, IReadOnlyList<string>> FindTargets(CancellationToken token)
    {
        var result = new Dictionary<Guid, IReadOnlyList<string>>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(2, 0xF, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            for (uint index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                IMMDevice? endpoint = null;
                IPropertyStore? properties = null;
                object? topologyObject = null;
                IConnector? connector = null;
                IMMDevice? adapter = null;
                object? controlObject = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(index, out endpoint));
                    Marshal.ThrowExceptionForHR(endpoint.OpenPropertyStore(0, out properties));
                    var key = new PropertyKey { Format = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), Id = 2 };
                    Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out var value));
                    Guid? container;
                    try { container = value.Type == 72 && value.Pointer != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(value.Pointer) : null; }
                    finally { PropVariantClear(ref value); }
                    if (container is null || container == Guid.Empty) continue;
                    var topologyId = typeof(IDeviceTopology).GUID;
                    Marshal.ThrowExceptionForHR(endpoint.Activate(ref topologyId, 23, IntPtr.Zero, out topologyObject));
                    Marshal.ThrowExceptionForHR(((IDeviceTopology)topologyObject).GetConnector(0, out connector));
                    Marshal.ThrowExceptionForHR(connector.GetDeviceIdConnectedTo(out var adapterId));
                    Marshal.ThrowExceptionForHR(enumerator.GetDevice(adapterId, out adapter));
                    var controlId = typeof(IKsControl).GUID;
                    Marshal.ThrowExceptionForHR(adapter.Activate(ref controlId, 23, IntPtr.Zero, out controlObject));
                    var control = (IKsControl)controlObject;
                    if (!Supports(control, 0) || !Supports(control, 1)) continue;
                    result[container.Value] = result.GetValueOrDefault(container.Value, []).Append(adapterId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                { /* Not every driver exposes controls; disconnected adapters may have no active path. */ }
                finally
                {
                    Release(controlObject); Release(adapter); Release(connector);
                    Release(topologyObject); Release(properties); Release(endpoint);
                }
            }
        }
        catch (COMException) { /* Audio service unavailable; retain devices with a Settings fallback. */ }
        finally { Release(collection); Release(enumerator); }
        return result;
    }

    public static int Request(IReadOnlyList<string> targets, bool connect, CancellationToken token)
    {
        var accepted = 0;
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            foreach (var target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                IMMDevice? adapter = null;
                object? controlObject = null;
                try
                {
                    Marshal.ThrowExceptionForHR(enumerator.GetDevice(target, out adapter));
                    var id = typeof(IKsControl).GUID;
                    Marshal.ThrowExceptionForHR(adapter.Activate(ref id, 23, IntPtr.Zero, out controlObject));
                    var property = new KsProperty { Set = BluetoothProperties, Id = connect ? 0u : 1u, Flags = 1 };
                    token.ThrowIfCancellationRequested();
                    // A successful request means an attempt, not a confirmed connection change.
                    if (((IKsControl)controlObject).KsProperty(ref property, 24, IntPtr.Zero, 0, out _) >= 0) accepted++;
                }
                catch (Exception exception) when (exception is not OperationCanceledException) { }
                finally { Release(controlObject); Release(adapter); }
            }
        }
        finally { Release(enumerator); }
        return accepted;
    }

    private static bool Supports(IKsControl control, uint id)
    {
        var property = new KsProperty { Set = BluetoothProperties, Id = id, Flags = 0x200 };
        var buffer = Marshal.AllocCoTaskMem(4);
        try
        {
            Marshal.WriteInt32(buffer, 0);
            return control.KsProperty(ref property, 24, buffer, 4, out _) >= 0 && (Marshal.ReadInt32(buffer) & 1) != 0;
        }
        finally { Marshal.FreeCoTaskMem(buffer); }
    }

    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [StructLayout(LayoutKind.Sequential)] private struct KsProperty { public Guid Set; public uint Id; public uint Flags; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint mode, out IPropertyStore store);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
    }
    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        [PreserveSig] int GetConnectorType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector other);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector other);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength, out uint returned);
    }
}
