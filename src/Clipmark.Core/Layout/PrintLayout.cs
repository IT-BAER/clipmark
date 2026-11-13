namespace Clipmark.Core.Layout;

public static class PrintLayout
{
    /// <summary>
    /// Where a printed image of <paramref name="width"/> x <paramref name="height"/> px goes in the printable area (DIP,
    /// 1/96 inch; one image px is one DIP at full size): shrunk to fit, never enlarged, centered.
    /// </summary>
    public static (double X, double Y, double Width, double Height) Place(int width, int height,
        double areaX, double areaY, double areaWidth, double areaHeight)
    {
        double scale = Math.Min(1, Math.Min(areaWidth / width, areaHeight / height));
        double w = width * scale, h = height * scale;
        return (areaX + (areaWidth - w) / 2, areaY + (areaHeight - h) / 2, w, h);
    }
}
