using Clipmark.Core.Ai;

namespace Clipmark.Core.Tests;

public sealed class MarkdownModelTests
{
    private static IReadOnlyList<MdInline> Inlines(string md) => ((MdParagraph)MarkdownModel.Parse(md).Single()).Inlines;

    [Fact]
    public void Paragraph_with_emphasis_code_and_plain_text()
    {
        Assert.Equal(
        [
            new MdRun("a "), new MdRun("b", Bold: true), new MdRun(" "), new MdRun("c", Italic: true),
            new MdRun(" "), new MdRun("d", Code: true),
        ], Inlines("a **b** *c* `d`"));
    }

    [Fact]
    public void Nested_emphasis_combines()
    {
        Assert.Equal([new MdRun("x", Bold: true, Italic: true)], Inlines("*__x__*"));
    }

    [Fact]
    public void Soft_break_becomes_a_space_and_hard_break_a_line_break()
    {
        Assert.Equal([new MdRun("a"), new MdRun(" "), new MdRun("b")], Inlines("a\nb"));
        Assert.Equal([new MdRun("a"), new MdLineBreak(), new MdRun("b")], Inlines("a  \nb"));
    }

    [Fact]
    public void Heading_and_rule()
    {
        var blocks = MarkdownModel.Parse("## Title\n\n---\n");
        var heading = Assert.IsType<MdHeading>(blocks[0]);
        Assert.Equal(2, heading.Level);
        Assert.Equal([new MdRun("Title")], heading.Inlines);
        Assert.IsType<MdRule>(blocks[1]);
    }

    [Fact]
    public void Lists_keep_order_start_and_nested_blocks()
    {
        var list = (MdList)MarkdownModel.Parse("3. one\n4. two").Single();
        Assert.True(list.Ordered);
        Assert.Equal(3, list.Start);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal([new MdRun("two")], ((MdParagraph)list.Items[1].Single()).Inlines);

        var bullets = (MdList)MarkdownModel.Parse("- a\n- b").Single();
        Assert.False(bullets.Ordered);
    }

    [Fact]
    public void Fenced_and_indented_code()
    {
        var fenced = (MdCode)MarkdownModel.Parse("```cs\nint x;\nint y;\n```").Single();
        Assert.Equal(("cs", "int x;\nint y;"), (fenced.Language, fenced.Text));

        var indented = (MdCode)MarkdownModel.Parse("    raw\n    code").Single();
        Assert.Equal((null, "raw\ncode"), (indented.Language, indented.Text));
    }

    [Fact]
    public void Quote_wraps_blocks()
    {
        var q = (MdQuote)MarkdownModel.Parse("> hi").Single();
        Assert.Equal([new MdRun("hi")], ((MdParagraph)q.Blocks.Single()).Inlines);
    }

    [Fact]
    public void Http_and_https_links_are_kept()
    {
        Assert.Equal([new MdRun("site", Link: "https://example.com/a")], Inlines("[site](https://example.com/a)"));
        Assert.Equal([new MdRun("s", Link: "http://example.com")], Inlines("[s](http://example.com)"));
    }

    [Theory]
    [InlineData("[x](file:///C:/Windows/notepad.exe)")]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](ms-settings:network)")]
    [InlineData("[x](relative/path)")]
    [InlineData("[x](//evil.example/a)")]
    public void Other_link_targets_become_plain_text(string md)
    {
        var runs = Inlines(md);
        Assert.All(runs, r => Assert.Null(((MdRun)r).Link));
        Assert.Equal("x", ((MdRun)runs[0]).Text);
    }

    [Fact]
    public void Partial_streaming_input_does_not_throw()
    {
        var code = (MdCode)MarkdownModel.Parse("```py\nprint(1)").Single();
        Assert.Equal("print(1)", code.Text);

        Assert.Contains(Inlines("**bold"), r => ((MdRun)r).Text.Contains("bold"));
        Assert.Empty(MarkdownModel.Parse(""));
    }

    [Fact]
    public void Html_becomes_plain_text()
    {
        var blocks = MarkdownModel.Parse("<div>hi</div>\n\ntext <b>x</b>");
        Assert.All(blocks, b => Assert.True(b is MdParagraph or MdCode));
        Assert.Contains("hi", Flatten(blocks[0]));
    }

    private static string Flatten(MdBlock b) => b switch
    {
        MdParagraph p => string.Concat(p.Inlines.OfType<MdRun>().Select(r => r.Text)),
        MdCode c => c.Text,
        _ => "",
    };
}
