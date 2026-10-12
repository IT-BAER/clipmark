using System.Numerics;
using System.Text;
using Clipmark.Core.Editor;

namespace Clipmark.Core.TextActions.Ocr;

/// <summary>Greedy CTC result: text, mean probability of the kept steps, and each kept character with its timestep.</summary>
public sealed record CtcResult(string Text, double Confidence, IReadOnlyList<(string Char, int Step)> Chars);

/// <summary>Greedy CTC decoding of the recognizer output; class 0 is the blank.</summary>
public static class CtcDecoder
{
    /// <summary>Argmax per step (first maximum wins), dropping blanks and repeats of the previous step's class.</summary>
    public static CtcResult Decode(ReadOnlySpan<float> probs, int steps, int classes, IReadOnlyList<string> labels)
    {
        var text = new StringBuilder();
        var chars = new List<(string, int)>();
        double sum = 0;
        int previous = -1;
        for (int t = 0; t < steps; t++)
        {
            var row = probs.Slice(t * classes, classes);
            int best = 0;
            for (int c = 1; c < classes; c++)
                if (row[c] > row[best]) best = c;
            if (best != 0 && best != previous)
            {
                text.Append(labels[best]);
                chars.Add((labels[best], t));
                sum += row[best];
            }
            previous = best;
        }
        return new CtcResult(text.ToString(), chars.Count > 0 ? sum / chars.Count : 0, chars);
    }

    /// <summary>
    /// Words split at spaces. The line box is cut at the center of each space run (timestep t spans t*step to (t+1)*step,
    /// step in image pixels), so the words tile the box and cover all of its ink; every word has the full box height.
    /// </summary>
    public static List<OcrWord> Words(IReadOnlyList<(string Char, int Step)> chars, double step, TextBox box)
    {
        var words = new List<(string Text, double Start)>();
        var text = new StringBuilder();
        double start = box.X0, spaceFrom = -1;
        foreach (var (ch, t) in chars)
        {
            if (ch == " ")
            {
                if (spaceFrom < 0) spaceFrom = t * step;
                if (text.Length > 0) { words.Add((text.ToString(), start)); text.Clear(); }
                start = box.X0 + (spaceFrom + (t + 1) * step) / 2;
                continue;
            }
            spaceFrom = -1;
            text.Append(ch);
        }
        if (text.Length > 0) words.Add((text.ToString(), start));
        var result = new List<OcrWord>(words.Count);
        for (int i = 0; i < words.Count; i++)
        {
            double end = i + 1 < words.Count ? words[i + 1].Start : box.X1;
            double from = i == 0 ? box.X0 : words[i].Start;
            result.Add(new OcrWord(words[i].Text, new FloatRect(new Vector2((float)from, (float)box.Y0), new Vector2((float)end, (float)box.Y1))));
        }
        return result;
    }
}
