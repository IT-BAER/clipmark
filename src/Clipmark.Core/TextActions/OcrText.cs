using System.Text;
using System.Text.RegularExpressions;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;

namespace Clipmark.Core.TextActions;

/// <summary>A recognized word and its box in image pixels.</summary>
public readonly record struct OcrWord(string Text, FloatRect Box);

public sealed record OcrLine(IReadOnlyList<OcrWord> Words);

public enum SensitiveKind { Email, Phone, IpAddress, Bank, Secret }

/// <summary>A box over a sensitive value (email, phone, IP/MAC, bank data, secret), in image pixels.</summary>
public readonly record struct SensitiveMatch(PixelRect Box, SensitiveKind Kind);

public static partial class OcrText
{
    /// <summary>Words joined by spaces, lines by newlines (or by spaces when removeLineBreaks).</summary>
    public static string Join(IEnumerable<OcrLine> lines, bool removeLineBreaks = false) =>
        string.Join(removeLineBreaks ? " " : Environment.NewLine, lines.Select(l => string.Join(' ', l.Words.Select(w => w.Text))));

    /// <summary>
    /// Boxes over secrets, email addresses, bank data, IP/MAC addresses and phone numbers: the union of the words each
    /// match touches, rounded outward and grown by pad px. Each detector masks its match before the next runs, so
    /// digits of an IBAN, token or address never count as a phone number.
    /// </summary>
    public static IReadOnlyList<SensitiveMatch> Sensitive(IEnumerable<OcrLine> lines, int pad)
    {
        var found = new List<SensitiveMatch>();
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
            var matches = new List<(int Start, int End, SensitiveKind Kind)>();
            masked = Collect(masked, TokenShape(), SensitiveKind.Secret, matches);
            masked = Collect(masked, LabeledSecret(), SensitiveKind.Secret, matches);
            masked = Collect(masked, Email(), SensitiveKind.Email, matches);
            masked = Collect(masked, IbanCandidate(), SensitiveKind.Bank, matches, g => IbanLength(g.Value));
            masked = Collect(masked, LabeledBic(), SensitiveKind.Bank, matches);
            masked = Collect(masked, CardCandidate(), SensitiveKind.Bank, matches, g => CardLength(g.Value));
            masked = Collect(masked, Ipv4(), SensitiveKind.IpAddress, matches);
            masked = Collect(masked, Ipv6(), SensitiveKind.IpAddress, matches);
            masked = Collect(masked, Mac(), SensitiveKind.IpAddress, matches);
            masked = Mask(masked, DateOrAddress());
            foreach (Match m in Phone().Matches(masked))
            {
                int digits = m.Value.Count(char.IsAsciiDigit);
                if (digits is >= 7 and <= 15) matches.Add((m.Index, m.Index + m.Length, SensitiveKind.Phone));
            }
            foreach (var (start, end, kind) in matches)
            {
                bool phone = kind == SensitiveKind.Phone;
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
                found.Add(new(PixelRect.FromCorners((int)MathF.Floor(minX), (int)MathF.Floor(r.Min.Y) - pad,
                    (int)MathF.Ceiling(maxX), (int)MathF.Ceiling(r.Max.Y) + pad), kind));
            }
        }
        return found;
    }

    // Same length, so word offsets still apply.
    private static string Mask(string text, Regex regex) => regex.Replace(text, m => new string('x', m.Length));

    // Records and masks each match (its "v" group when present); length overrides the matched length, 0 rejects.
    private static string Collect(string text, Regex regex, SensitiveKind kind, List<(int Start, int End, SensitiveKind Kind)> into,
        Func<Group, int>? length = null)
    {
        var chars = text.ToCharArray();
        foreach (Match m in regex.Matches(text))
        {
            var g = m.Groups["v"].Success ? m.Groups["v"] : m;
            int len = length?.Invoke(g) ?? g.Length;
            if (len <= 0) continue;
            into.Add((g.Index, g.Index + len, kind));
            Array.Fill(chars, 'x', g.Index, len);
        }
        return new string(chars);
    }

    // Total IBAN length per country; the checksum is not checked so a misread digit still gets covered.
    private static readonly Dictionary<string, int> IbanLengths = new()
    {
        ["AD"] = 24, ["AE"] = 23, ["AL"] = 28, ["AT"] = 20, ["BA"] = 20, ["BE"] = 16, ["BG"] = 22, ["BH"] = 22, ["BY"] = 28,
        ["CH"] = 21, ["CY"] = 28, ["CZ"] = 24, ["DE"] = 22, ["DK"] = 18, ["DO"] = 28, ["EE"] = 20, ["ES"] = 24, ["FI"] = 18,
        ["FO"] = 18, ["FR"] = 27, ["GB"] = 22, ["GE"] = 22, ["GI"] = 23, ["GL"] = 18, ["GR"] = 27, ["HR"] = 21, ["HU"] = 28,
        ["IE"] = 22, ["IL"] = 23, ["IS"] = 26, ["IT"] = 27, ["JO"] = 30, ["KW"] = 30, ["KZ"] = 20, ["LB"] = 28, ["LI"] = 21,
        ["LT"] = 20, ["LU"] = 20, ["LV"] = 21, ["MC"] = 27, ["MD"] = 24, ["ME"] = 22, ["MK"] = 19, ["MT"] = 31, ["NL"] = 18,
        ["NO"] = 15, ["PK"] = 24, ["PL"] = 28, ["PS"] = 29, ["PT"] = 25, ["QA"] = 29, ["RO"] = 24, ["RS"] = 22, ["SA"] = 24,
        ["SE"] = 24, ["SI"] = 19, ["SK"] = 24, ["SM"] = 27, ["TN"] = 24, ["TR"] = 26, ["UA"] = 29, ["VA"] = 22, ["VG"] = 24,
        ["XK"] = 20,
    };

    // Characters of the IBAN at the start of a candidate (single spaces allowed), 0 when it is not one. It ends at a word
    // end; one written as one word or in groups of 4 may be 2 characters off, so an OCR misread is still covered.
    private static int IbanLength(string candidate)
    {
        if (!IbanLengths.TryGetValue(candidate[..2].ToUpperInvariant(), out int total)) return 0;
        int count = 0, group = 0, best = 0, bestDiff = int.MaxValue;
        bool groupsOf4 = true;
        for (int i = 0; i <= candidate.Length && count <= total + 2; i++)
        {
            if (i < candidate.Length && candidate[i] != ' ') { count++; group++; continue; }
            int diff = Math.Abs(count - total);
            if ((diff == 0 || diff <= 2 && (count == group || groupsOf4 && group <= 4)) && diff < bestDiff)
                (best, bestDiff) = (i, diff);
            groupsOf4 &= group == 4;
            group = 0;
        }
        return best;
    }

    // Characters of the longest card number at the start of a candidate that ends at a digit-group end, 0 when none.
    private static int CardLength(string candidate)
    {
        int best = 0, digits = 0;
        for (int i = 0; i < candidate.Length; i++)
        {
            if (!char.IsAsciiDigit(candidate[i])) continue;
            digits++;
            bool groupEnd = i + 1 == candidate.Length || !char.IsAsciiDigit(candidate[i + 1]);
            if (groupEnd && digits is >= 13 and <= 19 && IsCard(candidate[..(i + 1)])) best = i + 1;
        }
        return best;
    }

    // A card number: grouped 4-4-4-4, or any digit run with a valid Luhn checksum.
    private static bool IsCard(string candidate)
    {
        if (GroupedCard().IsMatch(candidate)) return true;
        int sum = 0;
        bool dbl = false;
        for (int i = candidate.Length - 1; i >= 0; i--)
        {
            if (!char.IsAsciiDigit(candidate[i])) continue;
            int d = candidate[i] - '0';
            if (dbl && (d *= 2) > 9) d -= 9;
            sum += d;
            dbl = !dbl;
        }
        return sum % 10 == 0;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:sk-[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|(?:AKIA|ASIA)[0-9A-Z]{16}|xox[abprs]-[A-Za-z0-9-]{10,}|eyJ[A-Za-z0-9_-]{5,}\.eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*|-----BEGIN [A-Z ]*KEY-----)")]
    private static partial Regex TokenShape();

    [GeneratedRegex(@"(?i:(?<![A-Za-z0-9])(?:password|passwort|kennwort|pwd|pass|secret|token|api[ _-]?key|key|client[ _-]?secret)s?(?![A-Za-z0-9])[""']?\s*[:=]\s*)(?<v>\S.*)")]
    private static partial Regex LabeledSecret();

    [GeneratedRegex(@"(?i:(?<![A-Za-z0-9])(?:bic|swift)(?:[ -]?code)?[""']?\s*[:=]?\s*)(?<v>[A-Z]{6}[A-Z0-9]{2}(?:[A-Z0-9]{3})?)(?![A-Za-z0-9])")]
    private static partial Regex LabeledBic();

    // Lookaheads: every word start is tried, so a rejected candidate (a VAT ID) never hides the number after it.
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?=(?<v>[A-Za-z]{2}[0-9]{2}(?: ?[A-Za-z0-9]){11,30}))")]
    private static partial Regex IbanCandidate();

    [GeneratedRegex(@"(?<![0-9])(?=(?<v>[0-9](?:[ -]?[0-9]){12,18}))")]
    private static partial Regex CardCandidate();

    [GeneratedRegex(@"^[0-9]{4}([ -])[0-9]{4}\1[0-9]{4}\1[0-9]{4}$")]
    private static partial Regex GroupedCard();

    [GeneratedRegex(@"(?<![0-9A-Za-z.])(?:(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(?:/(?:3[0-2]|[12]?[0-9]))?(?::[0-9]{1,5})?(?![0-9A-Za-z]|\.[0-9])")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"(?<![0-9A-Za-z:])(?:(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,7}:|(?:[0-9A-Fa-f]{1,4}:){1,6}:[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,5}(?::[0-9A-Fa-f]{1,4}){1,2}|(?:[0-9A-Fa-f]{1,4}:){1,4}(?::[0-9A-Fa-f]{1,4}){1,3}|(?:[0-9A-Fa-f]{1,4}:){1,3}(?::[0-9A-Fa-f]{1,4}){1,4}|(?:[0-9A-Fa-f]{1,4}:){1,2}(?::[0-9A-Fa-f]{1,4}){1,5}|[0-9A-Fa-f]{1,4}:(?::[0-9A-Fa-f]{1,4}){1,6}|:(?::[0-9A-Fa-f]{1,4}){1,7})(?:/(?:12[0-8]|1[01][0-9]|[1-9]?[0-9]))?(?![0-9A-Za-z:])")]
    private static partial Regex Ipv6();

    [GeneratedRegex(@"(?<![0-9A-Za-z:.-])(?:[0-9A-Fa-f]{2}(?<s>[:-])[0-9A-Fa-f]{2}(?:\k<s>[0-9A-Fa-f]{2}){4}|[0-9A-Fa-f]{4}\.[0-9A-Fa-f]{4}\.[0-9A-Fa-f]{4})(?![0-9A-Za-z:-]|\.[0-9A-Za-z])")]
    private static partial Regex Mac();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<![0-9A-Za-z.])(?:[0-9]{1,2}\.[0-9]{1,2}\.[0-9]{2,4}|[0-9]{1,3}(?:\.[0-9]{1,3}){3})(?![0-9A-Za-z])")]
    private static partial Regex DateOrAddress();

    [GeneratedRegex(@"(?<![\w@.])\+?[0-9(][0-9 ./()-]*[0-9](?![\w@])")]
    private static partial Regex Phone();
}
