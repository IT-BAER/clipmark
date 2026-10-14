using System.Numerics;
using Clipmark.Core.Capture;

namespace Clipmark.Core.Editor;

/// <summary>Red pen strokes drawn on the frozen frame in the capture overlay, turned into editor objects of the cut.</summary>
public static class QuickMarkup
{
    /// <summary>The editor's default pen width.</summary>
    public const float PenWidth = 4;

    public static readonly Rgba Red = new(255, 0, 0, 255);

    /// <summary>
    /// Pen strokes in the image pixel space of a cut at <paramref name="origin"/> (desktop px), shifted by <paramref name="border"/>
    /// px. A stroke with no point inside the width x height cut is dropped; one that touches it keeps every point.
    /// </summary>
    public static IReadOnlyList<Stroke> ToStrokes(IEnumerable<IReadOnlyList<PixelPoint>> polylines, PixelPoint origin, int width, int height, int border = 0)
    {
        var cut = new PixelRect(origin.X, origin.Y, width, height);
        var strokes = new List<Stroke>();
        foreach (var line in polylines)
            if (line.Any(cut.Contains))
                strokes.Add(new Stroke(strokes.Count + 1, StrokeKind.Pen, Red, PenWidth,
                    [.. line.Select(p => new Vector2(p.X - origin.X + border + 0.5f, p.Y - origin.Y + border + 0.5f))]));
        return strokes;
    }

    /// <summary>A document of the image size holding the strokes; reaching past the image it is cropped back to it.</summary>
    public static EditorDocument Document(int width, int height, IReadOnlyList<Stroke> strokes)
    {
        var doc = new EditorDocument(width, height);
        foreach (var s in strokes)
            doc.Add(s);
        var image = new PixelRect(0, 0, width, height);
        if (doc.Bounds != image)
            doc.SetCrop(image);
        return doc;
    }
}
