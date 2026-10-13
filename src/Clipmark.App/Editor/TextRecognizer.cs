using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Clipmark.App.Capture;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;
using Clipmark.Core.TextActions;
using Windows.Graphics.Imaging;
using OcrEngine = Windows.Media.Ocr.OcrEngine;
using LocalOcrEngine = Clipmark.Core.TextActions.Ocr.OcrEngine;
using ZXing;
using ZXing.Common;

namespace Clipmark.App.Editor;

/// <summary>A decoded QR code and its box in image pixels.</summary>
internal sealed record QrCode(string Text, FloatRect Box);

/// <summary>
/// OCR (bundled ONNX models, else Windows.Media.Ocr in the user profile language) and QR decode (ZXing.Net) of an image;
/// boxes come back in image pixels.
/// </summary>
internal static class TextRecognizer
{
    private static readonly string ModelDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Ocr");
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    // Guards the cached engine: recognition, lazy load and idle disposal never overlap.
    private static readonly Lock EngineGate = new();
    private static LocalOcrEngine? s_engine;
    private static Timer? s_idle;

    /// <summary>
    /// Recognized lines. Uses the bundled models when present; on their absence or failure falls back to Windows OCR,
    /// returning null when no OCR language is installed. Images above the Windows OCR size limit are shrunk first.
    /// </summary>
    public static async Task<IReadOnlyList<OcrLine>?> RecognizeAsync(CapturedImage image, Vector2 origin)
    {
        var bgra = Opaque(image);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if ((engine is null || LatinScript(engine.RecognizerLanguage.LanguageTag))
            && await Task.Run(() => RecognizeLocal(bgra, image.Width, image.Height)) is { } local)
            return [.. local.Select(line => new OcrLine([.. line.Words.Select(w => w with { Box = new FloatRect(w.Box.Min + origin, w.Box.Max + origin) })]))];
        if (engine is null)
            return null;
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

    // The bundled recognizer reads Latin script only; other OCR languages keep Windows OCR.
    private static bool LatinScript(string tag)
    {
        var parts = tag.ToLowerInvariant().Split('-');
        return parts[0] == "sr" ? parts.Contains("latn") : LatinLanguages.Contains(parts[0]);
    }

    private static readonly HashSet<string> LatinLanguages =
    [
        "af", "az", "bs", "ca", "cs", "cy", "da", "de", "en", "es", "et", "eu", "fi", "fil", "fr", "ga", "gl", "hr", "hu",
        "id", "is", "it", "ku", "la", "lb", "lt", "lv", "mi", "ms", "mt", "nb", "nl", "nn", "no", "oc", "pl", "pt", "qu",
        "rm", "ro", "sk", "sl", "sq", "sv", "sw", "tl", "tr", "uz", "vi",
    ];

    // Null when the models are missing or the engine fails; the engine is disposed after IdleTimeout without use.
    private static IReadOnlyList<OcrLine>? RecognizeLocal(byte[] bgra, int width, int height)
    {
        string det = Path.Combine(ModelDir, "text-det.onnx"), rec = Path.Combine(ModelDir, "text-rec.onnx");
        if (!File.Exists(det) || !File.Exists(rec))
            return null;
        lock (EngineGate)
        {
            try
            {
                s_engine ??= new LocalOcrEngine(det, rec);
                return s_engine.Recognize(bgra, width, height);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                (s_idle ??= new Timer(_ => ReleaseEngine())).Change(IdleTimeout, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private static void ReleaseEngine()
    {
        lock (EngineGate)
        {
            s_engine?.Dispose();
            s_engine = null;
        }
        // The detector's input and output buffers sit on the large object heap until a full GC; give them back now.
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
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
