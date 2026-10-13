using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Clipmark.Core.Ai;

public abstract record MdBlock;
public sealed record MdParagraph(IReadOnlyList<MdInline> Inlines) : MdBlock;
public sealed record MdHeading(int Level, IReadOnlyList<MdInline> Inlines) : MdBlock;
public sealed record MdList(bool Ordered, int Start, IReadOnlyList<IReadOnlyList<MdBlock>> Items) : MdBlock;
public sealed record MdCode(string? Language, string Text) : MdBlock;
public sealed record MdQuote(IReadOnlyList<MdBlock> Blocks) : MdBlock;
public sealed record MdRule : MdBlock;

public abstract record MdInline;
public sealed record MdRun(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Link = null) : MdInline;
public sealed record MdLineBreak : MdInline;

/// <summary>Markdown reduced to the blocks the chat renders. Tolerates partial (streaming) input; only absolute http(s) links survive.</summary>
public static class MarkdownModel
{
    public static IReadOnlyList<MdBlock> Parse(string markdown) => Blocks(Markdig.Markdown.Parse(markdown));

    private static List<MdBlock> Blocks(ContainerBlock container)
    {
        var result = new List<MdBlock>();
        foreach (var block in container)
        {
            switch (block)
            {
                case HeadingBlock h:
                    result.Add(new MdHeading(h.Level, Inlines(h.Inline)));
                    break;
                case ParagraphBlock p:
                    result.Add(new MdParagraph(Inlines(p.Inline)));
                    break;
                case FencedCodeBlock f:
                    result.Add(new MdCode(string.IsNullOrWhiteSpace(f.Info) ? null : f.Info, Lines(f)));
                    break;
                case CodeBlock c:
                    result.Add(new MdCode(null, Lines(c)));
                    break;
                case HtmlBlock html:
                    result.Add(new MdParagraph([new MdRun(Lines(html))]));
                    break;
                case QuoteBlock q:
                    result.Add(new MdQuote(Blocks(q)));
                    break;
                case ThematicBreakBlock:
                    result.Add(new MdRule());
                    break;
                case ListBlock l:
                    result.Add(new MdList(l.IsOrdered, int.TryParse(l.OrderedStart, out int start) ? start : 1,
                        l.OfType<ListItemBlock>().Select(i => (IReadOnlyList<MdBlock>)Blocks(i)).ToList()));
                    break;
                case ContainerBlock other:
                    result.AddRange(Blocks(other));
                    break;
            }
        }
        return result;
    }

    private static string Lines(LeafBlock block) => block.Lines.ToString().TrimEnd('\r', '\n');

    private static List<MdInline> Inlines(ContainerInline? container)
    {
        var result = new List<MdInline>();
        if (container is not null)
            Walk(container, false, false, null, result);
        return result;
    }

    private static void Walk(ContainerInline container, bool bold, bool italic, string? link, List<MdInline> into)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    into.Add(new MdRun(lit.Content.ToString(), bold, italic, Link: link));
                    break;
                case CodeInline code:
                    into.Add(new MdRun(code.Content, bold, italic, Code: true, Link: link));
                    break;
                case LineBreakInline lb:
                    into.Add(lb.IsHard ? new MdLineBreak() : new MdRun(" ", bold, italic, Link: link));
                    break;
                case EmphasisInline em:
                    Walk(em, bold || em.DelimiterCount >= 2, italic || em.DelimiterCount is 1 or 3, link, into);
                    break;
                case LinkInline l:
                    Walk(l, bold, italic, l.IsImage ? null : SafeLink(l.Url), into);
                    break;
                case AutolinkInline auto:
                    into.Add(new MdRun(auto.Url, bold, italic, Link: SafeLink(auto.Url)));
                    break;
                case HtmlEntityInline entity:
                    into.Add(new MdRun(entity.Transcoded.ToString(), bold, italic, Link: link));
                    break;
                case HtmlInline html:
                    into.Add(new MdRun(html.Tag, bold, italic, Link: link));
                    break;
                case ContainerInline nested:
                    Walk(nested, bold, italic, link, into);
                    break;
            }
        }
    }

    private static string? SafeLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.OriginalString : null;
}
