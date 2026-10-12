using Microsoft.ML.OnnxRuntime;

namespace Clipmark.Core.TextActions.Ocr;

/// <summary>
/// Local OCR on PP-OCR ONNX models: DB text detection, then CTC recognition per line crop. Thread-safe; Recognize may
/// run concurrently, Dispose must not.
/// </summary>
public sealed class OcrEngine : IDisposable
{
    private const int MaxSide = 3200, MinSide = 736, LineHeight = 48;
    private const float Threshold = 0.3f, BoxThreshold = 0.5f;
    private const double Unclip = 1.6, MinRatio = 6.6667, ExtraRatio = 2, RotateRatio = 1.5, MergeGap = 1.0;

    private readonly InferenceSession _det, _rec;
    private readonly string[] _labels;

    public OcrEngine(string detModelPath, string recModelPath)
    {
        // No arena for the detector: its input reaches millions of pixels and the arena would keep that peak (hundreds of
        // MB). The recognizer's 48 px line inputs are small and run many times per image, so it keeps the arena.
        using var detOptions = new SessionOptions { EnableCpuMemArena = false, EnableMemoryPattern = false };
        _det = new InferenceSession(detModelPath, detOptions);
        try
        {
            _rec = new InferenceSession(recModelPath);
            // Classes: blank, the model's dictionary, space.
            _labels = ["", .. _rec.ModelMetadata.CustomMetadataMap["character"].Split('\n'), " "];
        }
        catch
        {
            _det.Dispose();
            _rec?.Dispose();
            throw;
        }
    }

    /// <summary>Lines in reading order with word boxes in image pixels, from opaque BGRA8 pixels.</summary>
    public IReadOnlyList<OcrLine> Recognize(byte[] bgra, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(bgra.Length, width * height * 4, nameof(bgra));
        var lines = new List<TextLine>();
        foreach (var box in Detect(bgra, width, height))
            if (ReadLine(bgra, width, box) is { } line)
                lines.Add(line);
        return [.. TextLines.Merge(lines, MergeGap).Select(l => new OcrLine(l.Words))];
    }

    private List<TextBox> Detect(byte[] bgra, int width, int height)
    {
        var (rw, rh) = TextDetection.InputSize(width, height, MaxSide, MinSide);
        var resized = OcrImage.Resize(bgra, width, height, 4, rw, rh);
        var input = new float[3 * rw * rh];
        OcrImage.Normalize(resized, rw, rh, input, 0, rw);
        using var outputs = Run(_det, input, [1, 3, rh, rw]);
        var prob = outputs[0].GetTensorDataAsSpan<float>();
        var mask = TextDetection.Mask(prob, rw, rh, Threshold, dilate: true);
        return TextDetection.Boxes(prob, rw, rh, TextDetection.Components(mask, rw, rh), width, height, BoxThreshold, Unclip);
    }

    private TextLine? ReadLine(byte[] bgra, int width, TextBox box)
    {
        int x0 = (int)box.X0, y0 = (int)box.Y0, x1 = (int)Math.Ceiling(box.X1), y1 = (int)Math.Ceiling(box.Y1);
        if (x1 <= x0 || y1 <= y0) return null;
        bool rotated = (double)(y1 - y0) / Math.Max(1, x1 - x0) >= RotateRatio;
        var crop = OcrImage.Crop(bgra, width, x0, y0, x1, y1, rotated);
        var (cw, ch) = rotated ? (y1 - y0, x1 - x0) : (x1 - x0, y1 - y0);
        int canvas = (int)(LineHeight * Math.Max(MinRatio, (double)cw / ch + ExtraRatio));
        int tw = Math.Min(canvas, (int)Math.Ceiling(LineHeight * (double)cw / ch));
        var input = new float[3 * LineHeight * canvas];
        OcrImage.Normalize(OcrImage.Resize(crop, cw, ch, 4, tw, LineHeight), tw, LineHeight, input, 0, canvas);
        using var outputs = Run(_rec, input, [1, 3, LineHeight, canvas]);
        var shape = outputs[0].GetTensorTypeAndShape().Shape;
        int steps = (int)shape[1], classes = (int)shape[2];
        if (classes != _labels.Length)
            throw new InvalidDataException($"Recognizer has {classes} classes, its dictionary {_labels.Length}.");
        var result = CtcDecoder.Decode(outputs[0].GetTensorDataAsSpan<float>(), steps, classes, _labels);
        if (!TextLines.Keep(result.Text, result.Confidence)) return null;
        var text = result.Text.Trim();
        if (rotated) return TextLine.Rotated(text, box);
        double step = (double)canvas / steps * cw / tw;
        return new TextLine(text, box, CtcDecoder.Words(result.Chars, step, box));
    }

    private static IDisposableReadOnlyCollection<OrtValue> Run(InferenceSession session, float[] input, long[] shape)
    {
        using var value = OrtValue.CreateTensorValueFromMemory(input, shape);
        using var options = new RunOptions();
        return session.Run(options, [session.InputNames[0]], [value], [session.OutputNames[0]]);
    }

    public void Dispose()
    {
        _det.Dispose();
        _rec.Dispose();
    }
}
