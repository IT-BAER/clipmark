using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Clipmark.App.Capture;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;
using Clipmark.Core.TextActions;
using Windows.Graphics.Imaging;
using OcrEngine = Windows.Media.Ocr.OcrEngine;
using ZXing;
using ZXing.Common;

namespace Clipmark.App.Editor;

/// <summary>A decoded QR code and its box in image pixels.</summary>
internal sealed record QrCode(string Text, FloatRect Box);

/// <summary>OCR (Windows.Media.Ocr, user profile language) and QR decode (ZXing.Net) of an image; boxes come back in image pixels.</summary>
internal static class TextRecognizer
{
    /// <summary>Recognized lines, or null when no OCR language is installed. Images above the OCR size limit are shrunk first.</summary>
    public static async Task<IReadOnlyList<OcrLine>?> RecognizeAsync(CapturedImage image, Vector2 origin)
    {
        if (OcrEngine.TryCreateFromUserProfileLanguages() is not { } engine)
            return null;
        var bgra = Opaque(image);
        int max = (int)OcrEngine.MaxImageDimension, factor = 1;
        while ((Math.Max(image.Width, image.Height) + factor - 1) / factor > max)
            factor++;
        var (pixels, width, height) = factor == 1 ? (bgra, image.Width, image.Height) : Pixels.Shrink(bgra, image.Width, image.Height, factor);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return [.. result.Lines.Select(line => new OcrLine([.. line.Words.Select(w =>
        {
            var r = w.BoundingRect;
            var min = new Vector2((float)r.X, (float)r.Y) * factor + origin;
            return new OcrWord(w.Text, new FloatRect(min, min + new Vector2((float)r.Width, (float)r.Height) * factor));
        })]))];
    }

    /// <summary>Every QR code in the image.</summary>
    public static IReadOnlyList<QrCode> DecodeQr(CapturedImage image, Vector2 origin)
    {
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] },
        };
        var source = new RGBLuminanceSource(Opaque(image), image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        return [.. (reader.DecodeMultiple(source) ?? []).Where(r => r.ResultPoints.Length > 0).Select(r =>
        {
            // The points are finder pattern centers, 3.5 modules inside the code: grow the box to roughly cover it.
            var points = r.ResultPoints.Select(p => new Vector2(p.X, p.Y) + origin).ToArray();
            var (min, max) = (points.Aggregate(Vector2.Min), points.Aggregate(Vector2.Max));
            var grow = (max - min) * 0.2f;
            return new QrCode(r.Text, new FloatRect(min - grow, max + grow));
        })];
    }

    // Transparent parts (freeform captures) read as white paper.
    private static byte[] Opaque(CapturedImage image)
    {
        if (!image.HasTransparency)
            return image.Bgra;
        var bgra = (byte[])image.Bgra.Clone();
        Pixels.OverWhite(bgra);
        return bgra;
    }
}
