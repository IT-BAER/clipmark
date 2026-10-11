using System.Collections.Immutable;
using System.Numerics;

namespace Clipmark.Core.Editor;

public readonly record struct Rgba(byte R, byte G, byte B, byte A);

public enum StrokeKind { Pen, Highlighter }

public enum ShapeKind { Rectangle, Ellipse, Line, Arrow }

public enum ZOrder { Up, Down, Front, Back }

public readonly record struct FloatRect(Vector2 Min, Vector2 Max)
{
    public static FloatRect FromCorners(Vector2 a, Vector2 b) => new(Vector2.Min(a, b), Vector2.Max(a, b));

    public FloatRect Union(FloatRect other) => new(Vector2.Min(Min, other.Min), Vector2.Max(Max, other.Max));

    public bool Contains(Vector2 p) => p.X >= Min.X && p.Y >= Min.Y && p.X <= Max.X && p.Y <= Max.Y;

    public bool Contains(FloatRect r) => Contains(r.Min) && Contains(r.Max);
}

/// <summary>Annotation in image pixel space; Id is unique within its document.</summary>
public abstract record EditorObject(int Id, Rgba Color, float Width)
{
    /// <summary>Radians, clockwise in y-down space, about the center of <see cref="EditorGeometry.LocalBox"/>; always 0 for lines and arrows.</summary>
    public float Rotation { get; init; }

    public abstract EditorObject Offset(Vector2 delta);
}

public sealed record Stroke(int Id, StrokeKind Kind, Rgba Color, float Width, ImmutableArray<Vector2> Points)
    : EditorObject(Id, Color, Width)
{
    public override EditorObject Offset(Vector2 delta) => this with { Points = [.. Points.Select(p => p + delta)] };

    // Value equality over the points (ImmutableArray compares by reference).
    public bool Equals(Stroke? other) => other is not null && Id == other.Id && Kind == other.Kind
        && Color == other.Color && Width == other.Width && Rotation == other.Rotation && Points.AsSpan().SequenceEqual(other.Points.AsSpan());

    public override int GetHashCode() => HashCode.Combine(Id, Kind, Color, Width, Rotation, Points.Length);
}

/// <summary>
/// Text with its layout box's top-left at Position. Width is the font size in px, so the width controls set the text size;
/// UnitSize is the layout size at font size 1, measured by the app.
/// </summary>
public sealed record TextObject(int Id, Rgba Color, float Width, Vector2 Position, string Text, Vector2 UnitSize)
    : EditorObject(Id, Color, Width)
{
    public Vector2 Size => UnitSize * Width;

    public override EditorObject Offset(Vector2 delta) => this with { Position = Position + delta };
}

/// <summary>Line / arrow end fixed to an object; Anchor is normalized in the target's LocalBox (0..1 inside, may lie outside).</summary>
public readonly record struct Attachment(int Target, Vector2 Anchor);

public sealed record Shape(int Id, ShapeKind Kind, Rgba Color, float Width, Vector2 Start, Vector2 End)
    : EditorObject(Id, Color, Width)
{
    /// <summary>Valid only while Start sits on the anchor; lines and arrows only.</summary>
    public Attachment? StartAttach { get; init; }
    public Attachment? EndAttach { get; init; }
    /// <summary>Inside color of a rectangle or ellipse; null paints the outline only.</summary>
    public Rgba? Fill { get; init; }

    public override EditorObject Offset(Vector2 delta) => this with { Start = Start + delta, End = End + delta };
}
