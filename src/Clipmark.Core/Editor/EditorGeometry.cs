using System.Collections.Immutable;
using System.Numerics;

namespace Clipmark.Core.Editor;

public static class EditorGeometry
{
    private const float HeadAngle = MathF.PI / 6;

    /// <summary>End rotated about <paramref name="start"/> to the nearest multiple of 45 degrees, same length.</summary>
    public static Vector2 SnapAngle(Vector2 start, Vector2 end)
    {
        var d = end - start;
        float length = d.Length();
        if (length == 0) return end;
        float step = MathF.PI / 4;
        float angle = MathF.Round(MathF.Atan2(d.Y, d.X) / step) * step;
        return start + length * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>Wing end points; Left is the wing on the left of the heading in y-down space.</summary>
    public static (Vector2 Left, Vector2 Right) ArrowHead(Vector2 start, Vector2 end, float width)
    {
        var shaft = end - start;
        float length = shaft.Length();
        if (length == 0) return (end, end);
        float head = MathF.Min(MathF.Max(12f, 4f * width), length);
        var back = -shaft / length;
        return (end + head * Rotate(back, HeadAngle), end + head * Rotate(back, -HeadAngle));
    }

    public static ImmutableArray<Vector2> Thin(IEnumerable<Vector2> points, float minDistance)
    {
        var kept = ImmutableArray.CreateBuilder<Vector2>();
        Vector2 last = default;
        bool any = false, lastKept = true;
        foreach (var p in points)
        {
            last = p;
            if (!any || Vector2.Distance(p, kept[^1]) >= minDistance)
            {
                kept.Add(p);
                lastKept = true;
            }
            else lastKept = false;
            any = true;
        }
        if (any && !lastKept && last != kept[^1]) kept.Add(last);
        return kept.ToImmutable();
    }

    public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared == 0 ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0, 1);
        return Vector2.Distance(p, a + t * ab);
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = MathF.Cos(radians), s = MathF.Sin(radians);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }
}
