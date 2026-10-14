using Clipmark.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Clipmark.App;

/// <summary>Lays out the visible children left to right and starts a new line where the next one does not fit; lines are centered.</summary>
public sealed partial class WrapPanel : Panel
{
    public double Spacing { get; set; }

    public double LineSpacing { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Visible();
        foreach (var item in items)
            item.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        double width = 0, height = 0;
        foreach (var line in Lines(items, availableSize.Width))
        {
            width = Math.Max(width, line.Width);
            height += line.Height + (height > 0 ? LineSpacing : 0);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var items = Visible();
        double y = 0;
        // Layout rounding can arrange a fraction narrower than measured: the lines must break as in MeasureOverride.
        foreach (var line in Lines(items, finalSize.Width + 1))
        {
            double x = Math.Max(0, (finalSize.Width - line.Width) / 2);
            for (int i = line.Start; i < line.End; i++)
            {
                var size = items[i].DesiredSize;
                items[i].Arrange(new Rect(x, y + (line.Height - size.Height) / 2, size.Width, size.Height));
                x += size.Width + Spacing;
            }
            y += line.Height + LineSpacing;
        }
        return finalSize;
    }

    private List<UIElement> Visible() => [.. Children.Where(c => c.Visibility == Visibility.Visible)];

    private IEnumerable<(int Start, int End, double Width, double Height)> Lines(List<UIElement> items, double available)
    {
        var starts = WrapLayout.LineStarts([.. items.Select(i => i.DesiredSize.Width)], available, Spacing);
        for (int n = 0; n < starts.Count; n++)
        {
            int start = starts[n], end = n + 1 < starts.Count ? starts[n + 1] : items.Count;
            var line = items.Skip(start).Take(end - start).Select(i => i.DesiredSize).ToList();
            yield return (start, end, line.Sum(s => s.Width) + Spacing * (line.Count - 1), line.Max(s => s.Height));
        }
    }
}
