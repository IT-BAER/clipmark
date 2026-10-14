namespace Clipmark.Core.Layout;

public static class ToolbarFit
{
    /// <summary>
    /// Whether the toolbar groups fit in <paramref name="available"/>: one row centers the edit tools between two equal
    /// side columns; two rows put the edit tools on their own row (they wrap there). <paramref name="gap"/> = column spacing.
    /// </summary>
    public static bool Fits(bool oneRow, double available, double gap, double capture, double edit, double output) =>
        (oneRow ? 2 * Math.Max(capture, output) + edit : capture + output) + 2 * gap <= available;
}
