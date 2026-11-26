using Dhara.Extensions.WinUI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace FoodHub.Services;

/// <summary>Windows-owned image selection and normalization; Core never depends on WinUI imaging types.</summary>
public static class ProfileImages
{
    /// <summary>Selects, decodes, bounds-checks, and scales an image into a portable PNG.</summary>
    /// <returns>Normalized bytes, or null when the picker is cancelled.</returns>
    public static async Task<byte[]?> PickAsync(Window owner)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg");
        owner.InitializeOwnedUI(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return null;
        if ((await file.GetBasicPropertiesAsync()).Size > 5 * 1024 * 1024)
            throw new ArgumentException("Choose a PNG or JPEG no larger than 5 MB.");
        using var input = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(input);
        if (decoder.DecoderInformation.CodecId != BitmapDecoder.PngDecoderId && decoder.DecoderInformation.CodecId != BitmapDecoder.JpegDecoderId)
            throw new ArgumentException("Choose a valid PNG or JPEG image.");
        if (decoder.PixelWidth is 0 or > 4096 || decoder.PixelHeight is 0 or > 4096)
            throw new ArgumentException("Choose an image no larger than 4096 by 4096 pixels.");
        // Decode only after checking dimensions; normalize orientation before scaling and saving.
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        var scale = Math.Min(1, 512.0 / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        encoder.BitmapTransform.ScaledWidth = (uint)Math.Max(1, Math.Round(bitmap.PixelWidth * scale));
        encoder.BitmapTransform.ScaledHeight = (uint)Math.Max(1, Math.Round(bitmap.PixelHeight * scale));
        await encoder.FlushAsync();
        using var reader = new DataReader(output.GetInputStreamAt(0));
        await reader.LoadAsync((uint)output.Size);
        var bytes = new byte[(int)output.Size]; reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>Creates a XAML image from saved PNG bytes, falling back to initials on corrupt data.</summary>
    public static async Task<BitmapImage?> FromBytesAsync(byte[]? photo)
    {
        if (photo is null || photo.Length == 0) return null;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            { writer.WriteBytes(photo); await writer.StoreAsync(); }
            stream.Seek(0);
            var image = new BitmapImage(); await image.SetSourceAsync(stream); return image;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        { return null; }
    }
}