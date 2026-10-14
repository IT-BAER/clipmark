namespace Clipmark.Core.Layout;

public static class WrapLayout
{
    /// <summary>Index of the first item of each line: items run left to right, the next item that does not fit starts a new line.</summary>
    public static IReadOnlyList<int> LineStarts(IReadOnlyList<double> widths, double available, double spacing)
    {
        var starts = new List<int>();
        double used = 0;
        for (int i = 0; i < widths.Count; i++)
        {
            if (starts.Count == 0 || used + spacing + widths[i] > available)
            {
                starts.Add(i);
                used = widths[i];
            }
            else
                used += spacing + widths[i];
        }
        return starts;
    }
}
