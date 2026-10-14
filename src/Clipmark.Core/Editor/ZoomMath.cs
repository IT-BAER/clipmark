namespace Clipmark.Core.Editor;

public static class ZoomMath
{
    public const double Min = 0.1, Max = 8, Step = 1.25;

    /// <summary>Largest zoom that shows the whole content in the viewport; 100% for empty content.</summary>
    public static double Fit(double viewportWidth, double viewportHeight, double contentWidth, double contentHeight) =>
        contentWidth <= 0 || contentHeight <= 0 ? 1
            : Math.Clamp(Math.Min(viewportWidth / contentWidth, viewportHeight / contentHeight), Min, Max);

    public static double In(double zoom) => Math.Clamp(zoom * Step, Min, Max);

    public static double Out(double zoom) => Math.Clamp(zoom / Step, Min, Max);
}
