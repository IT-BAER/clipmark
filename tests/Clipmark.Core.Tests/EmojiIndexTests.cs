using Clipmark.Core.Emoji;

namespace Clipmark.Core.Tests;

public class EmojiIndexTests
{
    private static readonly EmojiIndex German = EmojiIndex.Load("de-DE");
    private static readonly EmojiIndex English = EmojiIndex.Load("en-US");

    [Fact]
    public void The_index_lists_every_generated_emoji_in_Unicode_order_with_its_group_and_local_name()
    {
        Assert.Equal(1923, German.All.Count);
        Assert.Equal(new EmojiEntry("😀", EmojiGroup.Smileys, "grinsendes Gesicht"), German.All[0]);
        Assert.Equal("rotes Herz", German.All.Single(e => e.Emoji == "❤️").Name);
        Assert.Equal(EmojiGroup.Flags, German.All[^1].Group);
    }

    [Fact]
    public void Search_finds_local_and_English_words_by_word_prefix()
    {
        Assert.Contains(German.Search("herz"), e => e.Emoji == "❤️");
        Assert.Contains(German.Search("daum"), e => e.Emoji == "👍");
        Assert.Contains(German.Search("thumbs"), e => e.Emoji == "👍");
        Assert.DoesNotContain(German.Search("aumen"), e => e.Emoji == "👍");
    }

    [Fact]
    public void Search_ignores_case_and_accents()
    {
        Assert.Contains(German.Search("FROHLICH"), e => e.Emoji == "😀");
        Assert.Contains(EmojiIndex.Load("fr-FR").Search("etoile"), e => e.Emoji == "⭐");
    }

    [Fact]
    public void Every_word_of_the_query_must_match()
    {
        var hits = English.Search("red heart");
        Assert.Equal("❤️", hits[0].Emoji);
        Assert.All(hits, e => Assert.DoesNotContain("blue", e.Name));
    }

    [Fact]
    public void An_exact_name_comes_first_then_name_prefixes_then_keywords()
    {
        var hits = English.Search("fire");
        Assert.Equal("🔥", hits[0].Emoji);
        bool[] inName = [.. hits.Skip(1).Select(e => e.Name.Split(' ', '-').Any(w => w.StartsWith("fire", StringComparison.Ordinal)))];
        Assert.Contains(true, inName);
        Assert.Contains(false, inName);
        Assert.Equal(inName.OrderByDescending(b => b), inName);
    }

    [Fact]
    public void Cjk_queries_match_inside_words()
    {
        Assert.Contains(EmojiIndex.Load("ja-JP").Search("笑"), e => e.Emoji == "😀");
        Assert.Equal("🔥", EmojiIndex.Load("ja-JP").Search("火")[0].Emoji);
    }

    [Fact]
    public void An_empty_query_returns_all_and_an_unknown_language_falls_back_to_English()
    {
        Assert.Equal(German.All.Count, German.Search("  ").Count);
        var unknown = EmojiIndex.Load("xx-XX");
        Assert.Equal("red heart", unknown.All.Single(e => e.Emoji == "❤️").Name);
        Assert.Equal("火", EmojiIndex.Load("ja").All.Single(e => e.Emoji == "🔥").Name);
        Assert.Equal(EmojiIndex.Load("zh-CN").All[0].Name, EmojiIndex.Load("zh-Hans").All[0].Name);
        Assert.NotEqual(EmojiIndex.Load("zh-TW").All[0].Name, EmojiIndex.Load("zh-Hans").All[0].Name);
    }
}
