using System.Numerics;
using Clipmark.Core.Editor;

namespace Clipmark.Core.TextActions.Ocr;

/// <summary>A recognized line: trimmed text, box in image pixels and words.</summary>
public sealed record TextLine(string Text, TextBox Box, IReadOnlyList<OcrWord> Words)
{
    /// <summary>A line read from a rotated crop: one word covering the whole box.</summary>
    public static TextLine Rotated(string text, TextBox box) =>
        new(text, box, [new OcrWord(text, new FloatRect(new Vector2((float)box.X0, (float)box.Y0), new Vector2((float)box.X1, (float)box.Y1)))]);
}

/// <summary>Line filtering and same-row merging after recognition.</summary>
public static class TextLines
{
    /// <summary>
    /// Keeps a line when its trimmed text is not empty and confidence &gt;= minConfidence; texts of at most 2 characters
    /// (toolbar icons read as letters) also need shortConfidence.
    /// </summary>
    public static bool Keep(string text, double confidence, double minConfidence = 0.5, double shortConfidence = 0.9)
    {
        var trimmed = text.Trim();
        if (confidence < minConfidence || trimmed.Length == 0) return false;
        return trimmed.EnumerateRunes().Count() > 2 || confidence >= shortConfidence;
    }

    /// <summary>
    /// Left to right, a line joins the first earlier line it overlaps vertically by &gt;= 60% of the smaller height, with
    /// heights within 1.3x and a horizontal gap of 0..gap*smaller height; the result is in reading order.
    /// </summary>
    public static List<TextLine> Merge(IEnumerable<TextLine> lines, double gap)
    {
        var merged = new List<TextLine>();
        foreach (var l in lines.OrderBy(l => l.Box.X0))
        {
            var (x0, y0, x1, y1) = l.Box;
            double h = y1 - y0;
            int target = merged.FindIndex(m =>
            {
                var (a0, b0, a1, b1) = m.Box;
                double mh = b1 - b0, low = Math.Min(h, mh), overlap = Math.Min(y1, b1) - Math.Max(y0, b0);
                return overlap >= 0.6 * low && Math.Max(h, mh) < 1.3 * low && x0 - a1 >= 0 && x0 - a1 <= gap * low;
            });
            if (target < 0) { merged.Add(l); continue; }
            var m = merged[target];
            merged[target] = new TextLine(m.Text + " " + l.Text, new TextBox(m.Box.X0, Math.Min(y0, m.Box.Y0), x1, Math.Max(y1, m.Box.Y1)),
                [.. m.Words, .. l.Words]);
        }
        return [.. TextBox.InReadingOrder(merged, l => l.Box)];
    }
}
