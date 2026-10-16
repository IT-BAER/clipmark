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

    /// <summary>Area the object paints: its outline points mapped to world, grown by half the width (miter corners of a rotated rectangle by more).</summary>
    public static FloatRect Extent(EditorObject o)
    {
        float grow = o is Shape { Kind: ShapeKind.Rectangle } && o.Rotation != 0 ? o.Width / 2 * MathF.Sqrt(2) : HalfStroke(o);
        var r = new Vector2(grow);
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        foreach (var local in Outline(o))
        {
            var p = ToWorld(o, local);
            min = Vector2.Min(min, p - r);
            max = Vector2.Max(max, p + r);
        }
        return new FloatRect(min, max);
    }

    /// <summary>Unrotated geometry box without the width: stroke point bounds, or the Start/End box of a shape.</summary>
    public static FloatRect LocalBox(EditorObject o) => o switch
    {
        Stroke { Points.Length: > 0 } s => new FloatRect(
            s.Points.Aggregate(Vector2.Min), s.Points.Aggregate(Vector2.Max)),
        Shape s => FloatRect.FromCorners(s.Start, s.End),
        TextObject t => new FloatRect(t.Position, t.Position + t.Size),
        EmojiObject e => new FloatRect(e.Position, e.Position + new Vector2(e.Width)),
        _ => default,
    };

    /// <summary>How far paint reaches past the geometry: half the stroke width; 0 for text and emoji, whose Width is their size.</summary>
    public static float HalfStroke(EditorObject o) => o is TextObject or EmojiObject ? 0 : o.Width / 2;

    /// <summary>World point of a normalized anchor in the target's LocalBox.</summary>
    public static Vector2 AnchorPoint(EditorObject target, Vector2 anchor)
    {
        var box = LocalBox(target);
        return ToWorld(target, box.Min + anchor * (box.Max - box.Min));
    }

    /// <summary>Normalized anchor of a world point; a zero-size box side maps to its middle.</summary>
    public static Vector2 AnchorOf(EditorObject target, Vector2 world)
    {
        var box = LocalBox(target);
        var size = box.Max - box.Min;
        var p = ToLocal(target, world) - box.Min;
        return new(size.X > 0 ? p.X / size.X : 0.5f, size.Y > 0 ? p.Y / size.Y : 0.5f);
    }

    public static Vector2 ToWorld(EditorObject o, Vector2 local) => RotateAbout(o, local, o.Rotation);

    public static Vector2 ToLocal(EditorObject o, Vector2 world) => RotateAbout(o, world, -o.Rotation);

    private static Vector2 RotateAbout(EditorObject o, Vector2 p, float radians)
    {
        if (radians == 0) return p;
        var box = LocalBox(o);
        return Vector2.Transform(p, Matrix3x2.CreateRotation(radians, (box.Min + box.Max) / 2));
    }

    // Round caps and joins: every painted pixel lies within Width/2 of one of these points.
    private static IEnumerable<Vector2> Outline(EditorObject o)
    {
        switch (o)
        {
            case Stroke s:
                return s.Points;
            case Shape { Kind: ShapeKind.Arrow } s:
                var (left, right) = ArrowHead(s.Start, s.End, s.Width);
                return [s.Start, s.End, left, right];
            case Shape s:
                return [s.Start, s.End, new(s.Start.X, s.End.Y), new(s.End.X, s.Start.Y)];
            case TextObject or EmojiObject:
                var box = LocalBox(o);
                return [box.Min, box.Max, new(box.Min.X, box.Max.Y), new(box.Max.X, box.Min.Y)];
            default:
                return [];
        }
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
