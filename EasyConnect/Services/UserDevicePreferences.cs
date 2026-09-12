using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using EasyConnect.Models;

namespace EasyConnect.Services;

public sealed class UserDevicePreferences
{
    private readonly string _filePath;
    private readonly Dictionary<string, DevicePreference> _preferences = new(StringComparer.OrdinalIgnoreCase);

    public UserDevicePreferences(string? basePath = null)
    {
        var root = basePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyConnect");
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "devices.json");
        Load();
    }

    public BluetoothDeviceInfo Apply(BluetoothDeviceInfo device)
    {
        var key = GetKey(device);
        return _preferences.TryGetValue(key, out var preference)
            ? device with { Alias = preference.Alias, IsFavorite = preference.IsFavorite }
            : device;
    }

    public void SetFavorite(BluetoothDeviceInfo device, bool isFavorite)
    {
        var preference = GetOrCreate(device);
        preference.IsFavorite = isFavorite;
        Save();
    }

    public void SetAlias(BluetoothDeviceInfo device, string? alias)
    {
        var preference = GetOrCreate(device);
        preference.Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        Save();
    }

    private DevicePreference GetOrCreate(BluetoothDeviceInfo device)
    {
        var key = GetKey(device);
        if (!_preferences.TryGetValue(key, out var preference))
        {
            preference = new DevicePreference();
            _preferences[key] = preference;
        }

        return preference;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var items = JsonSerializer.Deserialize<Dictionary<string, DevicePreference>>(
                File.ReadAllText(_filePath));
            if (items is null)
            {
                return;
            }

            foreach (var item in items)
            {
                _preferences[item.Key] = item.Value;
            }
        }
        catch
        {
            _preferences.Clear();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_preferences, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private static string GetKey(BluetoothDeviceInfo device)
    {
        var identity = device.PnpContainerId?.ToString("N") ?? device.DeviceId;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(bytes);
    }

    private sealed class DevicePreference
    {
        public string? Alias { get; set; }
        public bool IsFavorite { get; set; }
    }
}
