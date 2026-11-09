using System.Net.Http;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace CineLibraryEssentials.Utilities;

/// <summary>
/// Writes artwork chosen in the NFO editor. Always goes through a temp file
/// and then swaps it in, so a failed download or a bad image never leaves a
/// half-written poster behind.
/// </summary>
public static class ImageFiles
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// Copies a picked image to <paramref name="target"/>, converting it when the
    /// formats differ (a PNG picked as a poster becomes JPG; a clear logo stays PNG
    /// so its transparency survives).
    /// </summary>
    public static async Task SaveAsAsync(string source, string target)
    {
        var tmp = target + ".tmp";
        var wantPng = target.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        var ext = Path.GetExtension(source).ToLowerInvariant();
        var sameFormat = wantPng ? ext == ".png" : ext is ".jpg" or ".jpeg";

        if (sameFormat)
        {
            File.Copy(source, tmp, overwrite: true);
        }
        else
        {
            var file = await StorageFile.GetFileFromPathAsync(source);
            using var input = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(input);
            using var pixels = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, wantPng ? BitmapAlphaMode.Premultiplied : BitmapAlphaMode.Ignore);
            using (var fs = File.Create(tmp))
            using (var output = fs.AsRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(
                    wantPng ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId, output);
                encoder.SetSoftwareBitmap(pixels);
                await encoder.FlushAsync();
            }
        }
        File.Move(tmp, target, overwrite: true);
    }

    /// <summary>Downloads an image (e.g. a TMDb original) to <paramref name="target"/>.</summary>
    public static async Task DownloadAsync(string url, string target)
    {
        var tmp = target + ".tmp";
        var bytes = await Http.GetByteArrayAsync(url);
        if (bytes.Length == 0) throw new IOException("The picture came back empty.");
        await File.WriteAllBytesAsync(tmp, bytes);
        File.Move(tmp, target, overwrite: true);
    }

    /// <summary>A TMDb picture URL at the given size ("w342", "original"...).</summary>
    public static string Tmdb(string filePath, string size) => $"https://image.tmdb.org/t/p/{size}{filePath}";

    /// <summary>
    /// The TMDb size to download for a Settings choice, and the width to shrink the
    /// picture to afterwards (0 = keep). TMDb has no 1920 or 640 wide sizes, so
    /// "max1920" and "max640" come from the original.
    /// </summary>
    public static (string TmdbSize, int ShrinkTo) Resolve(string setting)
        => setting.StartsWith("max", StringComparison.Ordinal) && int.TryParse(setting[3..], out var width)
            ? ("original", width)
            : (setting, 0);

    /// <summary>
    /// Scales a JPG down to <paramref name="maxWidth"/> pixels wide, keeping its shape,
    /// when it's wider. If anything goes wrong the full-size picture stays.
    /// </summary>
    public static async Task ShrinkToWidthAsync(string path, int maxWidth)
    {
        if (maxWidth <= 0 || !File.Exists(path)) return;
        var tmp = path + ".tmp";
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using (var input = await file.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(input);
                if (decoder.PixelWidth <= maxWidth) return;
                var height = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * (double)maxWidth / decoder.PixelWidth));
                using var pixels = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                using var fs = File.Create(tmp);
                using var output = fs.AsRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output,
                    new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(0.92f, Windows.Foundation.PropertyType.Single) });
                encoder.SetSoftwareBitmap(pixels);
                encoder.BitmapTransform.ScaledWidth = (uint)maxWidth;
                encoder.BitmapTransform.ScaledHeight = height;
                encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
                await encoder.FlushAsync();
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shrinking {path} failed: {ex.Message}");
            try { File.Delete(tmp); } catch { }
        }
    }
}
