using System.Collections.Immutable;
using System.Numerics;
using Clipmark.Core.Capture;

namespace Clipmark.Core.Editor;

public sealed class EditorDocument
{
    private const int EllipseSegments = 64;

    // Bounds = image rect united with every object's outline; Crop = null means export Bounds.
    private sealed record State(ImmutableList<EditorObject> Objects, PixelRect? Crop, PixelRect Bounds);

    // history[position] is the current state; entries after position are redo.
    private readonly List<State> _history;
    private int _position;
    private int _cleanPosition; // -1 when the clean state is no longer reachable
    private int _nextId = 1;

    public EditorDocument(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _history = [new State([], null, new PixelRect(0, 0, width, height))];
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<EditorObject> Objects => Current.Objects;
    public PixelRect? Crop => Current.Crop;
    public PixelRect Bounds => Current.Bounds;
    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _history.Count - 1;
    public bool IsDirty => _position != _cleanPosition;
    public event Action? Changed;

    private State Current => _history[_position];

    public int NewId() => _nextId++;

    /// <summary>Adds the object; an applied crop grows to include it.</summary>
    public void Add(EditorObject obj)
    {
        var crop = Current.Crop is { } c ? Union(c, OuterRect(obj)) : (PixelRect?)null;
        PushObjects(Current.Objects.Add(obj), crop);
    }

    public void Remove(IReadOnlyCollection<int> ids)
    {
        var kept = Current.Objects.RemoveAll(o => ids.Contains(o.Id));
        if (kept.Count != Current.Objects.Count) PushObjects(kept, Current.Crop);
    }

    public void SetCrop(PixelRect rect)
    {
        var clamped = rect.Intersect(Bounds);
        if (clamped is { } c && c != (Crop ?? Bounds)) Push(Current with { Crop = c });
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _position--;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _position++;
        Changed?.Invoke();
    }

    public void MarkClean() => _cleanPosition = _position;

    public EditorObject? HitTest(Vector2 point, float tolerance)
    {
        for (int i = Objects.Count - 1; i >= 0; i--)
        {
            var o = Objects[i];
            if (DistanceTo(o, point) <= o.Width / 2 + tolerance) return o;
        }
        return null;
    }

    private void Push(State next)
    {
        _history.RemoveRange(_position + 1, _history.Count - _position - 1);
        if (_cleanPosition > _position) _cleanPosition = -1;
        _history.Add(next);
        _position++;
        Changed?.Invoke();
    }

    // The crop never reaches past the bounds, so erasing an object that grew it trims it again.
    private void PushObjects(ImmutableList<EditorObject> objects, PixelRect? crop)
    {
        var bounds = new PixelRect(0, 0, Width, Height);
        foreach (var o in objects)
            bounds = Union(bounds, OuterRect(o));
        Push(Current with { Objects = objects, Bounds = bounds, Crop = crop is { } c ? c.Intersect(bounds) ?? c : null });
    }

    private static PixelRect Union(PixelRect a, PixelRect b) => PixelRect.FromCorners(Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));

    /// <summary>Pixels the object paints, rounded outward.</summary>
    private static PixelRect OuterRect(EditorObject o)
    {
        float r = o.Width / 2;
        float left = float.PositiveInfinity, top = float.PositiveInfinity;
        float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
        foreach (var p in Outline(o))
        {
            left = MathF.Min(left, p.X - r);
            top = MathF.Min(top, p.Y - r);
            right = MathF.Max(right, p.X + r);
            bottom = MathF.Max(bottom, p.Y + r);
        }
        return PixelRect.FromCorners((int)MathF.Floor(left), (int)MathF.Floor(top),
            (int)MathF.Ceiling(right), (int)MathF.Ceiling(bottom));
    }

    // Round caps and joins: every painted pixel lies within Width/2 of one of these points.
    private static IEnumerable<Vector2> Outline(EditorObject o)
    {
        switch (o)
        {
            case Stroke s:
                return s.Points;
            case Shape { Kind: ShapeKind.Arrow } s:
                var (left, right) = EditorGeometry.ArrowHead(s.Start, s.End, s.Width);
                return [s.Start, s.End, left, right];
            case Shape s:
                return [s.Start, s.End, new(s.Start.X, s.End.Y), new(s.End.X, s.Start.Y)];
            default:
                return [];
        }
    }

    private static float DistanceTo(EditorObject o, Vector2 p) => o switch
    {
        Stroke s => DistanceToPolyline(p, s.Points),
        Shape { Kind: ShapeKind.Line } s => EditorGeometry.DistanceToSegment(p, s.Start, s.End),
        Shape { Kind: ShapeKind.Arrow } s => ArrowDistance(p, s),
        Shape { Kind: ShapeKind.Rectangle } s => DistanceToPolyline(p, RectangleOutline(s)),
        Shape { Kind: ShapeKind.Ellipse } s => DistanceToPolyline(p, EllipseOutline(s)),
        _ => float.PositiveInfinity,
    };

    private static float DistanceToPolyline(Vector2 p, IReadOnlyList<Vector2> points)
    {
        if (points.Count == 0) return float.PositiveInfinity;
        if (points.Count == 1) return Vector2.Distance(p, points[0]);
        float best = float.PositiveInfinity;
        for (int i = 1; i < points.Count; i++)
            best = MathF.Min(best, EditorGeometry.DistanceToSegment(p, points[i - 1], points[i]));
        return best;
    }

    private static float ArrowDistance(Vector2 p, Shape s)
    {
        var (left, right) = EditorGeometry.ArrowHead(s.Start, s.End, s.Width);
        return MathF.Min(EditorGeometry.DistanceToSegment(p, s.Start, s.End),
            MathF.Min(EditorGeometry.DistanceToSegment(p, s.End, left),
                EditorGeometry.DistanceToSegment(p, s.End, right)));
    }

    private static Vector2[] RectangleOutline(Shape s)
    {
        var min = Vector2.Min(s.Start, s.End);
        var max = Vector2.Max(s.Start, s.End);
        return [min, new(max.X, min.Y), max, new(min.X, max.Y), min];
    }

    private static Vector2[] EllipseOutline(Shape s)
    {
        var center = (s.Start + s.End) / 2;
        var radius = Vector2.Abs(s.End - s.Start) / 2;
        var points = new Vector2[EllipseSegments + 1];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = 2 * MathF.PI * i / EllipseSegments;
            points[i] = center + radius * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }
        return points;
    }
}
