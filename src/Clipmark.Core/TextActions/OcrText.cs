using System.Text;
using System.Text.RegularExpressions;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;

namespace Clipmark.Core.TextActions;

/// <summary>A recognized word and its box in image pixels.</summary>
public readonly record struct OcrWord(string Text, FloatRect Box);

public sealed record OcrLine(IReadOnlyList<OcrWord> Words);

public static partial class OcrText
{
    /// <summary>Words joined by spaces, lines by newlines.</summary>
    public static string Join(IEnumerable<OcrLine> lines) =>
        string.Join(Environment.NewLine, lines.Select(l => string.Join(' ', l.Words.Select(w => w.Text))));

    /// <summary>
    /// Boxes over email addresses and phone numbers: the union of the words each match touches, rounded outward and
    /// grown by pad px. Dates and IPv4 addresses are masked first so they never count as phone digits.
    /// </summary>
    public static IReadOnlyList<PixelRect> Sensitive(IEnumerable<OcrLine> lines, int pad)
    {
        var found = new List<PixelRect>();
        foreach (var line in lines)
        {
            var text = new StringBuilder();
            var spans = new List<(int Start, int End)>();
            foreach (var word in line.Words)
            {
                if (text.Length > 0) text.Append(' ');
                spans.Add((text.Length, text.Length + word.Text.Length));
                text.Append(word.Text);
            }
            var masked = text.ToString();
            var matches = new List<(int Start, int End, bool Phone)>();
            foreach (Match m in Email().Matches(masked))
                matches.Add((m.Index, m.Index + m.Length, false));
            masked = Mask(masked, Email());
            masked = Mask(masked, DateOrAddress());
            foreach (Match m in Phone().Matches(masked))
            {
                int digits = m.Value.Count(char.IsAsciiDigit);
                if (digits is >= 7 and <= 15) matches.Add((m.Index, m.Index + m.Length, true));
            }
            foreach (var (start, end, phone) in matches)
            {
                int first = spans.FindIndex(s => s.Start < end && start < s.End);
                int last = spans.FindLastIndex(s => s.Start < end && start < s.End);
                if (first < 0) continue;
                var r = line.Words[first].Box;
                for (int i = first + 1; i <= last; i++)
                    r = r.Union(line.Words[i].Box);
                float minX = r.Min.X - pad, maxX = r.Max.X + pad;
                // OCR drops words of phone numbers (measured: "+43 660" missing between "Tel." and "1234567"): a gap
                // wider than the line height next to the number is taken as unread digits, up to the neighboring word.
                float height = r.Max.Y - r.Min.Y;
                if (phone && first > 0 && r.Min.X - line.Words[first - 1].Box.Max.X > height)
                    minX = line.Words[first - 1].Box.Max.X + pad;
                if (phone && last < spans.Count - 1 && line.Words[last + 1].Box.Min.X - r.Max.X > height)
                    maxX = line.Words[last + 1].Box.Min.X - pad;
                found.Add(PixelRect.FromCorners((int)MathF.Floor(minX), (int)MathF.Floor(r.Min.Y) - pad,
                    (int)MathF.Ceiling(maxX), (int)MathF.Ceiling(r.Max.Y) + pad));
            }
        }
        return found;
    }

    // Same length, so word offsets still apply.
    private static string Mask(string text, Regex regex) => regex.Replace(text, m => new string('x', m.Length));

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<![0-9A-Za-z.])(?:[0-9]{1,2}\.[0-9]{1,2}\.[0-9]{2,4}|[0-9]{1,3}(?:\.[0-9]{1,3}){3})(?![0-9A-Za-z])")]
    private static partial Regex DateOrAddress();

    [GeneratedRegex(@"(?<![\w@.])\+?[0-9(][0-9 ./()-]*[0-9](?![\w@])")]
    private static partial Regex Phone();
}
