using System.Globalization;
using System.Numerics;
using Clipmark.Core.Editor;

namespace Clipmark.Core.TextActions;

/// <summary>A position between characters: UTF-16 offset in the line text that <see cref="OcrText.Join"/> builds.</summary>
public readonly record struct OcrCaret(int Line, int Offset) : IComparable<OcrCaret>
{
    public int CompareTo(OcrCaret other) => Line != other.Line ? Line.CompareTo(other.Line) : Offset.CompareTo(other.Offset);
}

/// <summary>Character selection in recognized text; character positions are estimated by splitting word boxes evenly.</summary>
public static class OcrSelection
{
    /// <summary>Text between two carets in either order: line parts joined by newlines, as in <see cref="OcrText.Join"/>.</summary>
    public static string Text(IReadOnlyList<OcrLine> lines, OcrCaret a, OcrCaret b)
    {
        var (from, to) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        var parts = new List<string>();
        for (int i = from.Line; i <= to.Line; i++)
        {
            var text = LineText(lines[i]);
            int start = i == from.Line ? from.Offset : 0, end = i == to.Line ? to.Offset : text.Length;
            parts.Add(text[start..end]);
        }
        return string.Join(Environment.NewLine, parts);
    }

    /// <summary>
    /// Caret at the character boundary nearest to a point on a line box grown by slack px, or null off the text; between
    /// words the nearer word edge.
    /// </summary>
    public static OcrCaret? HitTest(IReadOnlyList<OcrLine> lines, Vector2 point, float slack) => Closest(lines, point, slack);

    /// <summary>Caret for a dragged point anywhere: on the closest line, at the nearest character boundary; null without words.</summary>
    public static OcrCaret? Nearest(IReadOnlyList<OcrLine> lines, Vector2 point) => Closest(lines, point, float.PositiveInfinity);

    /// <summary>Start and end of the word that holds the caret (a caret at a word edge belongs to that word).</summary>
    public static (OcrCaret Start, OcrCaret End) Word(IReadOnlyList<OcrLine> lines, OcrCaret caret)
    {
        int start = 0;
        foreach (var word in lines[caret.Line].Words)
        {
            if (caret.Offset <= start + word.Text.Length)
                return (caret with { Offset = start }, caret with { Offset = start + word.Text.Length });
            start += word.Text.Length + 1;
        }
        return (caret, caret);
    }

    /// <summary>Start of the first line and end of the last.</summary>
    public static (OcrCaret Start, OcrCaret End) All(IReadOnlyList<OcrLine> lines) =>
        lines.Count == 0 ? (default, default) : (default, new OcrCaret(lines.Count - 1, LineText(lines[^1]).Length));

    /// <summary>One box per line part between two carets in either order, as tall as the line; none for an empty range.</summary>
    public static IReadOnlyList<FloatRect> Boxes(IReadOnlyList<OcrLine> lines, OcrCaret a, OcrCaret b)
    {
        var (from, to) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        var boxes = new List<FloatRect>();
        for (int i = from.Line; i <= to.Line; i++)
        {
            var line = lines[i];
            int start = i == from.Line ? from.Offset : 0, end = i == to.Line ? to.Offset : LineText(line).Length;
            if (start >= end || line.Words.Count == 0)
                continue;
            var bounds = Bounds(line);
            boxes.Add(new FloatRect(new(XAt(line, start), bounds.Min.Y), new(XAt(line, end), bounds.Max.Y)));
        }
        return boxes;
    }

    private static OcrCaret? Closest(IReadOnlyList<OcrLine> lines, Vector2 point, float slack)
    {
        int best = -1;
        (float Dy, float Dx) nearest = (float.MaxValue, float.MaxValue);
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Words.Count == 0)
                continue;
            var distance = Distance(Bounds(lines[i]), point);
            if (distance.Dy <= slack && distance.Dx <= slack && distance.CompareTo(nearest) < 0)
                (best, nearest) = (i, distance);
        }
        return best < 0 ? null : new OcrCaret(best, OffsetAt(lines[best], point.X));
    }

    private static string LineText(OcrLine line) => string.Join(' ', line.Words.Select(w => w.Text));

    private static FloatRect Bounds(OcrLine line) => line.Words.Select(w => w.Box).Aggregate((a, b) => a.Union(b));

    // Vertical first, so a point inside a row picks its line over a closer column next to it.
    private static (float Dy, float Dx) Distance(FloatRect box, Vector2 p) =>
        (Math.Max(0, Math.Max(box.Min.Y - p.Y, p.Y - box.Max.Y)), Math.Max(0, Math.Max(box.Min.X - p.X, p.X - box.Max.X)));

    private static int OffsetAt(OcrLine line, float x)
    {
        int start = 0, previousEnd = 0;
        for (int i = 0; i < line.Words.Count; i++)
        {
            var (text, box) = line.Words[i];
            if (x < box.Min.X)
                return i == 0 || x - line.Words[i - 1].Box.Max.X < box.Min.X - x ? previousEnd : start;
            if (x <= box.Max.X)
            {
                var elements = StringInfo.ParseCombiningCharacters(text);
                float width = box.Max.X - box.Min.X;
                int k = width <= 0 ? 0 : Math.Clamp((int)MathF.Round((x - box.Min.X) / width * elements.Length, MidpointRounding.AwayFromZero), 0, elements.Length);
                return start + (k < elements.Length ? elements[k] : text.Length);
            }
            previousEnd = start + text.Length;
            start = previousEnd + 1;
        }
        return previousEnd;
    }

    private static float XAt(OcrLine line, int offset)
    {
        int start = 0;
        foreach (var (text, box) in line.Words)
        {
            if (offset <= start + text.Length)
            {
                var elements = StringInfo.ParseCombiningCharacters(text);
                int k = elements.Count(e => e < offset - start);
                return elements.Length == 0 ? box.Min.X : box.Min.X + (box.Max.X - box.Min.X) * k / elements.Length;
            }
            start += text.Length + 1;
        }
        return line.Words[^1].Box.Max.X;
    }
}
