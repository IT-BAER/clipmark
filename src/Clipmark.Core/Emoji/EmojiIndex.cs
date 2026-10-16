using System.Globalization;
using System.Text;

namespace Clipmark.Core.Emoji;

public enum EmojiGroup { Smileys, People, Animals, Food, Travel, Activities, Objects, Symbols, Flags }

/// <summary>Name is the UI language's CLDR name, else the English one.</summary>
public sealed record EmojiEntry(string Emoji, EmojiGroup Group, string Name);

/// <summary>Emoji from the generated Data files (scripts\gen-emoji.ps1) with search over local and English names and keywords.</summary>
public sealed class EmojiIndex
{
    private sealed record Terms(string[] NameWords, string[] Words, string Text, string[] Names);

    private readonly Terms[] _terms;

    public IReadOnlyList<EmojiEntry> All { get; }

    private EmojiIndex(List<EmojiEntry> all, Terms[] terms)
    {
        All = all;
        _terms = terms;
    }

    /// <summary>Index for a UI language tag such as "de-DE" or "zh-Hant"; a language without data gets English.</summary>
    public static EmojiIndex Load(string language)
    {
        var english = Annotations("en")!;
        var local = DataFile(language) is { } file && file != "en" ? Annotations(file) : null;
        var all = new List<EmojiEntry>();
        var terms = new List<Terms>();
        foreach (var line in Lines("emoji"))
        {
            var f = line.Split('\t');
            string emoji = f[1];
            (string Name, string[] Keywords) en = english.GetValueOrDefault(emoji, (f[2], Array.Empty<string>()));
            var own = local?.GetValueOrDefault(emoji);
            string name = own?.Name ?? en.Name;
            all.Add(new EmojiEntry(emoji, Enum.Parse<EmojiGroup>(f[0], ignoreCase: true), name));
            string[] names = [Normalize(name), Normalize(en.Name)];
            var words = names.Concat(en.Keywords).Concat(own?.Keywords ?? []).Select(Normalize).ToArray();
            terms.Add(new Terms([.. names.SelectMany(Split)], [.. words.SelectMany(Split)], string.Join('\n', words), names));
        }
        return new EmojiIndex(all, [.. terms]);
    }

    /// <summary>Every query word must start a word of a name or keyword (CJK: occur anywhere); exact names first, then name matches.</summary>
    public IReadOnlyList<EmojiEntry> Search(string query)
    {
        string q = Normalize(query.Trim());
        string[] tokens = Split(q);
        if (tokens.Length == 0)
            return All;
        var hits = new List<(EmojiEntry Entry, int Rank)>();
        for (int i = 0; i < _terms.Length; i++)
        {
            var t = _terms[i];
            if (!tokens.All(k => Matches(t.Words, t.Text, k)))
                continue;
            int rank = t.Names.Contains(q) ? 0 : Matches(t.NameWords, string.Join('\n', t.Names), tokens[0]) ? 1 : 2;
            hits.Add((All[i], rank));
        }
        return [.. hits.OrderBy(h => h.Rank).Select(h => h.Entry)];
    }

    private static bool Matches(string[] words, string text, string token) =>
        token.Any(c => c >= '⺀') ? text.Contains(token, StringComparison.Ordinal)
            : words.Any(w => w.StartsWith(token, StringComparison.Ordinal));

    private static string[] Split(string s) =>
        s.Split([' ', '-', ':', ',', '.', '’', '\'', '\n', '(', ')'], StringSplitOptions.RemoveEmptyEntries);

    // Lower case without accents: "Fröhlich" -> "frohlich".
    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static readonly string[] Files =
        ["en", "de", "es", "fr", "it", "ja", "ko", "nl", "pl", "pt", "ru", "sv", "tr", "uk", "zh-Hans", "zh-Hant"];

    private static string? DataFile(string language)
    {
        string tag = language.Trim();
        if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return tag.Contains("Hant", StringComparison.OrdinalIgnoreCase) || tag.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                || tag.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) || tag.EndsWith("-MO", StringComparison.OrdinalIgnoreCase)
                ? "zh-Hant" : "zh-Hans";
        string primary = tag.Split('-')[0].ToLowerInvariant();
        return Files.Contains(primary) ? primary : null;
    }

    private static Dictionary<string, (string Name, string[] Keywords)>? Annotations(string file)
    {
        var map = new Dictionary<string, (string Name, string[] Keywords)>(StringComparer.Ordinal);
        foreach (var line in Lines(file))
        {
            var f = line.Split('\t');
            map[f[0]] = (f[1], f[2].Split('|', StringSplitOptions.RemoveEmptyEntries));
        }
        return map;
    }

    private static IEnumerable<string> Lines(string file)
    {
        using var stream = typeof(EmojiIndex).Assembly.GetManifestResourceStream($"Clipmark.Core.Emoji.{file}.tsv")
            ?? throw new InvalidOperationException($"emoji data {file} missing");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
            if (line.Length > 0 && line[0] != '#')
                yield return line;
    }
}
