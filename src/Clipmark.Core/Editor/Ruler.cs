using System.Numerics;

namespace Clipmark.Core.Editor;

/// <summary>Infinite straight line through <see cref="Point"/> along the unit vector <see cref="Direction"/>.</summary>
public readonly record struct RulerEdge(Vector2 Point, Vector2 Direction);

/// <summary>Drawing ruler in image px: a band of <see cref="HalfHeight"/> around <see cref="Center"/>, turned by <see cref="Angle"/> degrees (clockwise on screen).</summary>
public readonly record struct Ruler(Vector2 Center, float Angle, float HalfHeight)
{
    public const float StepDegrees = 15;

    private Vector2 Axis => new(MathF.Cos(Angle * MathF.PI / 180), MathF.Sin(Angle * MathF.PI / 180));

    private Vector2 Normal => new(-Axis.Y, Axis.X);

    public bool Contains(Vector2 point, float halfLength)
    {
        var d = point - Center;
        return Math.Abs(Vector2.Dot(d, Axis)) <= halfLength && Math.Abs(Vector2.Dot(d, Normal)) <= HalfHeight;
    }

    /// <summary>Turns by whole steps (positive = clockwise); the angle stays in 0 to 359.</summary>
    public Ruler RotateBy(int steps) => this with { Angle = ((Angle + steps * StepDegrees) % 360 + 360) % 360 };

    /// <summary>The long edge line within <paramref name="tolerance"/> of the point (the nearer one), else null.</summary>
    public RulerEdge? EdgeNear(Vector2 point, float tolerance)
    {
        float across = Vector2.Dot(point - Center, Normal);
        float side = across >= 0 ? 1 : -1;
        return Math.Abs(across - side * HalfHeight) <= tolerance ? new RulerEdge(Center + Normal * (side * HalfHeight), Axis) : null;
    }

    /// <summary>The points moved onto the edge line, in the same order.</summary>
    public static Vector2[] Project(IEnumerable<Vector2> points, RulerEdge edge) =>
        [.. points.Select(p => edge.Point + edge.Direction * Vector2.Dot(p - edge.Point, edge.Direction))];
}
