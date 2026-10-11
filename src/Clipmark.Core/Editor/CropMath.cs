using System.Numerics;
using Clipmark.Core.Capture;

namespace Clipmark.Core.Editor;

public enum CropHandle { Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

public static class CropMath
{
    /// <summary>Moves the handle's edge(s) to <paramref name="to"/> within <paramref name="bounds"/>; an edge stops <paramref name="minSize"/> short of the opposite one.</summary>
    public static PixelRect Resize(PixelRect rect, CropHandle handle, PixelPoint to, PixelRect bounds, int minSize = 1)
    {
        int left = rect.X, top = rect.Y, right = rect.Right, bottom = rect.Bottom;
        if (handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft)
            left = Math.Clamp(to.X, bounds.X, right - minSize);
        if (handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight)
            right = Math.Clamp(to.X, left + minSize, bounds.Right);
        if (handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight)
            top = Math.Clamp(to.Y, bounds.Y, bottom - minSize);
        if (handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight)
            bottom = Math.Clamp(to.Y, top + minSize, bounds.Bottom);
        return PixelRect.FromCorners(left, top, right, bottom);
    }

    /// <summary>Handle under the point: a corner within <paramref name="tolerance"/> first, else anywhere along an edge.</summary>
    public static CropHandle? HitHandle(PixelRect rect, Vector2 p, float tolerance)
    {
        bool left = MathF.Abs(p.X - rect.X) <= tolerance, right = MathF.Abs(p.X - rect.Right) <= tolerance;
        bool top = MathF.Abs(p.Y - rect.Y) <= tolerance, bottom = MathF.Abs(p.Y - rect.Bottom) <= tolerance;
        bool inX = p.X >= rect.X && p.X <= rect.Right, inY = p.Y >= rect.Y && p.Y <= rect.Bottom;
        return (left, top, right, bottom) switch
        {
            (true, true, _, _) => CropHandle.TopLeft,
            (_, true, true, _) => CropHandle.TopRight,
            (true, _, _, true) => CropHandle.BottomLeft,
            (_, _, true, true) => CropHandle.BottomRight,
            (_, true, _, _) when inX => CropHandle.Top,
            (_, _, _, true) when inX => CropHandle.Bottom,
            (true, _, _, _) when inY => CropHandle.Left,
            (_, _, true, _) when inY => CropHandle.Right,
            _ => null,
        };
    }
}
