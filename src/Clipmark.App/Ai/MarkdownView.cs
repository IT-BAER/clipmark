using Clipmark.Core.Ai;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Clipmark.App.Ai;

/// <summary>Renders the chat Markdown model into a selectable RichTextBlock; links (http / https only) open in the browser.</summary>
internal static class MarkdownView
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private const double Indent = 20;

    public static void Render(Microsoft.UI.Xaml.Controls.RichTextBlock target, string markdown)
    {
        target.Blocks.Clear();
        foreach (var block in MarkdownModel.Parse(markdown))
            Add(target.Blocks, block, 0, secondary: false);
    }

    private static void Add(BlockCollection into, MdBlock block, double left, bool secondary)
    {
        switch (block)
        {
            case MdHeading h:
                var heading = Paragraph(h.Inlines, left, secondary);
                heading.FontWeight = FontWeights.SemiBold;
                heading.FontSize = h.Level switch { 1 => 20, 2 => 17, _ => 15 };
                into.Add(heading);
                break;
            case MdParagraph p:
                into.Add(Paragraph(p.Inlines, left, secondary));
                break;
            case MdCode c:
                var code = new Paragraph { FontFamily = Mono, Margin = new Thickness(left + 8, 4, 0, 8) };
                code.Inlines.Add(new Run { Text = c.Text });
                into.Add(code);
                break;
            case MdQuote q:
                foreach (var inner in q.Blocks)
                    Add(into, inner, left + Indent, secondary: true);
                break;
            case MdRule:
                into.Add(new Paragraph { Inlines = { new Run { Text = "———" } }, Foreground = Secondary, Margin = new Thickness(left, 0, 0, 8) });
                break;
            case MdList l:
                for (int i = 0; i < l.Items.Count; i++)
                {
                    string marker = l.Ordered ? $"{l.Start + i}." : "•";
                    var item = l.Items[i];
                    for (int j = 0; j < item.Count; j++)
                    {
                        if (j == 0 && item[0] is MdParagraph first)
                        {
                            // Hanging indent: the marker sits in the indent, wrapped lines align with the text.
                            var p = Paragraph(first.Inlines, left + Indent, secondary);
                            p.TextIndent = -Indent;
                            p.Margin = new Thickness(left + Indent, 0, 0, 4);
                            p.Inlines.Insert(0, new Run { Text = marker + "  " });
                            into.Add(p);
                        }
                        else
                        {
                            if (j == 0)
                                into.Add(new Paragraph { Inlines = { new Run { Text = marker } }, Margin = new Thickness(left, 0, 0, 0) });
                            Add(into, item[j], left + Indent, secondary);
                        }
                    }
                }
                break;
        }
    }

    private static Brush Secondary => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    private static Paragraph Paragraph(IReadOnlyList<MdInline> inlines, double left, bool secondary)
    {
        var p = new Paragraph { Margin = new Thickness(left, 0, 0, 8) };
        if (secondary)
            p.Foreground = Secondary;
        foreach (var inline in inlines)
            p.Inlines.Add(Inline(inline));
        return p;
    }

    private static Inline Inline(MdInline inline)
    {
        if (inline is not MdRun run)
            return new LineBreak();
        var text = new Run { Text = run.Text };
        if (run.Code)
            text.FontFamily = Mono;
        if (run.Bold)
            text.FontWeight = FontWeights.SemiBold;
        if (run.Italic)
            text.FontStyle = Windows.UI.Text.FontStyle.Italic;
        if (run.Link is null)
            return text;
        var link = new Hyperlink { NavigateUri = new Uri(run.Link) };
        link.Inlines.Add(text);
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(link, run.Link);
        return link;
    }
}
