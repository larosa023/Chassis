using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Chassis.App.Services;

/// <summary>Reads the text in a picture with Windows' built-in text recognition. Runs on this PC; nothing is uploaded.</summary>
public static class SpecReader
{
    public static async Task<string> ReadAsync(byte[] image)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(image.AsBuffer());
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        double w = decoder.PixelWidth, h = decoder.PixelHeight;
        // Small screenshots read better enlarged; big photos must fit the engine's limit.
        var scale = Math.Min(OcrEngine.MaxImageDimension / Math.Max(w, h), Math.Max(w, h) < 1400 ? 2.0 : 1.0);
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(w * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(h * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
            ?? throw new InvalidOperationException("Windows text recognition isn't installed. Add English under Settings › Time & language › Language & region, then try again.");
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join("\n", result.Lines.Select(l => l.Text));
    }

    public static async Task<byte[]> BytesAsync(IRandomAccessStream s)
    {
        s.Seek(0);
        var bytes = new byte[s.Size];
        using var reader = new DataReader(s.GetInputStreamAt(0));
        await reader.LoadAsync((uint)s.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
