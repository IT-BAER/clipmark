using System.Numerics;

namespace Clipmark.Core.Editor;

/// <summary>Rotate and resize of single objects; width and color never change.</summary>
public static class ObjectTransform
{
    /// <summary>Rotates the object about <paramref name="pivot"/>; lines and arrows turn their end points, other objects their center and <see cref="EditorObject.Rotation"/>.</summary>
    public static EditorObject Rotate(EditorObject o, Vector2 pivot, float angle)
    {
        var turn = Matrix3x2.CreateRotation(angle, pivot);
        if (IsSegment(o))
            return Map(o, p => Vector2.Transform(p, turn));
        var center = Center(o);
        return o.Offset(Vector2.Transform(center, turn) - center) with { Rotation = Normalize(o.Rotation + angle) };
    }

    /// <summary>Moves the handle's edge(s) to <paramref name="world"/> in the object's local frame; the opposite edge or corner stays fixed in world space.</summary>
    public static EditorObject Resize(EditorObject o, CropHandle handle, Vector2 world, bool keepAspect, float minSize = 1)
    {
        var box = EditorGeometry.LocalBox(o);
        var size = box.Max - box.Min;
        var p = EditorGeometry.ToLocal(o, world);
        bool left = handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft;
        bool right = handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight;
        bool top = handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight;
        bool bottom = handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight;
        var min = box.Min;
        var max = box.Max;
        if (size.X > 0)
        {
            if (left) min.X = MathF.Min(p.X, box.Max.X - minSize);
            if (right) max.X = MathF.Max(p.X, box.Min.X + minSize);
        }
        if (size.Y > 0)
        {
            if (top) min.Y = MathF.Min(p.Y, box.Max.Y - minSize);
            if (bottom) max.Y = MathF.Max(p.Y, box.Min.Y + minSize);
        }
        if (keepAspect && (left || right) && (top || bottom) && size.X > 0 && size.Y > 0)
        {
            float sx = (max.X - min.X) / size.X, sy = (max.Y - min.Y) / size.Y;
            float s = MathF.Abs(sx - 1) >= MathF.Abs(sy - 1) ? sx : sy;
            s = MathF.Max(s, MathF.Max(minSize / size.X, minSize / size.Y));
            var anchor = new Vector2(left ? box.Max.X : box.Min.X, top ? box.Max.Y : box.Min.Y);
            var far = anchor + new Vector2(left ? -1 : 1, top ? -1 : 1) * size * s;
            min = Vector2.Min(anchor, far);
            max = Vector2.Max(anchor, far);
        }
        var scale = new Vector2(size.X > 0 ? (max.X - min.X) / size.X : 1, size.Y > 0 ? (max.Y - min.Y) / size.Y : 1);
        var mapped = Map(o, q => min + (q - box.Min) * scale);
        // A rotated object turns about its new center, so shift it back onto the old local-to-world mapping.
        var center = (min + max) / 2;
        return mapped.Offset(EditorGeometry.ToWorld(o, center) - center);
    }

    /// <summary>Scales every geometry point about <paramref name="anchor"/> by <paramref name="factor"/>; rotation and width stay.</summary>
    public static EditorObject ScaleUniform(EditorObject o, Vector2 anchor, float factor)
    {
        if (o.Rotation == 0)
            return Map(o, p => anchor + (p - anchor) * factor);
        var center = Center(o);
        return Map(o, p => center + (p - center) * factor).Offset(anchor + (center - anchor) * factor - center);
    }

    internal static bool IsSegment(EditorObject o) => o is Shape { Kind: ShapeKind.Line or ShapeKind.Arrow };

    private static Vector2 Center(EditorObject o)
    {
        var box = EditorGeometry.LocalBox(o);
        return (box.Min + box.Max) / 2;
    }

    private static EditorObject Map(EditorObject o, Func<Vector2, Vector2> f) => o switch
    {
        Stroke s => s with { Points = [.. s.Points.Select(f)] },
        Shape s => s with { Start = f(s.Start), End = f(s.End) },
        // Text scales its font size with the vertical scale; callers keep the aspect.
        TextObject t when t.Size.Y > 0 => t with
        {
            Position = f(t.Position), Width = t.Width * Vector2.Distance(f(t.Position), f(t.Position + new Vector2(0, t.Size.Y))) / t.Size.Y,
        },
        TextObject t => t with { Position = f(t.Position) },
        _ => o,
    };

    private static float Normalize(float radians) => MathF.IEEERemainder(radians, 2 * MathF.PI);
}
