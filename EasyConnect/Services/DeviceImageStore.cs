using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using EasyConnect.Models;

namespace EasyConnect.Services;

public sealed class DeviceImageStore
{
    private static string PathFor(string key) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyConnect", "DeviceImages",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".png");

    public async Task<byte[]?> ReadAsync(string key, CancellationToken token)
    {
        try
        {
            var path = PathFor(key);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, token).ConfigureAwait(false) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public async Task<byte[]?> ChooseAsync(BluetoothDeviceInfo device)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Choose a photo for {device.DeviceName}",
            Filter = "Device images|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true
        };
        if (picker.ShowDialog() != true) return null;
        return await Task.Run(() =>
        {
            if (new FileInfo(picker.FileName).Length > 10 * 1024 * 1024)
                throw new InvalidOperationException("Choose an image smaller than 10 MB.");
            // Decode and resize before saving; never retain an arbitrary external file reference.
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 320;
            bitmap.UriSource = new Uri(picker.FileName);
            bitmap.EndInit();
            bitmap.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            var bytes = stream.ToArray();
            var path = PathFor(device.ImageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return bytes;
        });
    }
}
