using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProjectHub.Worker;

/// <summary>
/// Image-content observation independent of a multimodal AI model.
/// Detects empty/flat captures, not aesthetic superiority or gameplay parity.
/// It never modifies a scene on the basis of a screenshot alone.
/// </summary>
internal static class UnityVisualFoundryPixelAudit
{
    internal sealed record Observation(
        bool Readable, bool FlatFrame, int Width, int Height,
        double MeanBrightness, double LuminanceVariation,
        double Colorfulness, string Detail);

    internal static Observation Inspect(string? png)
    {
        if (string.IsNullOrWhiteSpace(png) || !File.Exists(png))
            return new(false, false, 0, 0, 0, 0, 0,
                "PIXEL_AUDIT_UNVERIFIED: missing capture");
        try
        {
            using var stream = File.OpenRead(png);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                return new(false, false, 0, 0, 0, 0, 0, "PIXEL_AUDIT_NO_FRAME");
            var original = decoder.Frames[0];
            var width = original.PixelWidth;
            var height = original.PixelHeight;
            if (width == 0 || height == 0)
                return new(false, false, width, height, 0, 0, 0,
                    "PIXEL_AUDIT_INVALID_DIMENSIONS");
            var scale = Math.Min(1, Math.Min(256d / width, 256d / height));
            BitmapSource scaled = new TransformedBitmap(original,
                new ScaleTransform(scale, scale));
            var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            var stride = checked(bgra.PixelWidth * 4);
            var pixelBytes = new byte[checked(stride * bgra.PixelHeight)];
            bgra.CopyPixels(pixelBytes, stride, 0);
            var count = checked(bgra.PixelWidth * bgra.PixelHeight);
            double sum = 0, sumSquares = 0, chroma = 0;
            var visible = 0;
            for (var i = 0; i < pixelBytes.Length; i += 4)
            {
                var alpha = pixelBytes[i+3] / 255d;
                if (alpha < .05) continue;
                visible++;
                var b = pixelBytes[i] / 255d;
                var g = pixelBytes[i+1] / 255d;
                var r = pixelBytes[i+2] / 255d;
                var luma = r*.2126 + g*.7152 + b*.0722;
                sum += luma;
                sumSquares += luma * luma;
                chroma += Math.Max(r,Math.Max(g,b)) -
                          Math.Min(r,Math.Min(g,b));
            }
            var n = Math.Max(1, visible);
            var avg = sum / n;
            var deviation = Math.Sqrt(Math.Max(0,sumSquares/n-avg*avg));
            var saturation = chroma / n;
            var flat = visible < count * .05 ||
                deviation < .022 && saturation < .07;
            var detail = "PIXEL_AUDIT_OBSERVED: " + width + "x" + height +
                "; luminance_variation=" + deviation.ToString("F3",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "; colorfulness=" + saturation.ToString("F3",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "; " + (flat ? "FLAT_OR_EMPTY_REVIEW_NEEDED" : "CONTENT_PRESENT_UNINSPECTED");
            return new(true, flat, width, height, Math.Round(avg,3),
                Math.Round(deviation,3), Math.Round(saturation,3),detail);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or
            InvalidOperationException or NotSupportedException or
            System.Runtime.InteropServices.COMException)
        {
            return new(false, false, 0, 0, 0, 0, 0,
                "PIXEL_AUDIT_UNVERIFIED: " + ex.Message);
        }
    }
}
