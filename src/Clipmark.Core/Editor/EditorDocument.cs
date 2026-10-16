using System.Collections.Immutable;
using System.Numerics;
using Clipmark.Core.Capture;

namespace Clipmark.Core.Editor;

public sealed class EditorDocument
{
    private const int EllipseSegments = 64;
    private const float AnchorSlack = 0.01f;

    // Bounds = image rect united with every object's outline; Crop = null means export Bounds.
    // Background = canvas color under the image (bucket on empty space); null = transparent.
    // Redactions = black boxes over the image pixels (image space, clipped); burned into the base image when it is stored.
    private sealed record State(ImmutableList<EditorObject> Objects, PixelRect? Crop, PixelRect Bounds, Rgba? Background = null)
    {
        public ImmutableList<PixelRect> Redactions { get; init; } = [];
    }

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
    public Rgba? Background => Current.Background;
    public IReadOnlyList<PixelRect> Redactions => Current.Redactions;
    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _history.Count - 1;
    public bool IsDirty => _position != _cleanPosition;

    /// <summary>Same reference while the content is the same state (undo returns to it); compare with ReferenceEquals.</summary>
    public object Revision => Current;
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
        if (kept.Count != Current.Objects.Count) PushObjects(Resolve(kept, new HashSet<int>()), Current.Crop);
    }

    /// <summary>
    /// Swaps in changed objects by id, keeping their z position, as one undo step; attached line / arrow ends follow
    /// (see <see cref="WithAttached"/>) and an applied crop grows to include every changed object.
    /// </summary>
    public void Replace(IReadOnlyCollection<EditorObject> changed)
    {
        var objects = Swap(changed);
        if (objects == Current.Objects) return;
        var crop = Current.Crop;
        for (int i = 0; i < objects.Count && crop is { } c; i++)
            if (objects[i] != Current.Objects[i]) crop = Union(c, OuterRect(objects[i]));
        PushObjects(objects, crop);
    }

    /// <summary>
    /// The changed objects plus the lines / arrows attached to them, as <see cref="Replace"/> would store them:
    /// ends attached to a changed target move onto its anchor; changed ends that left their anchor detach.
    /// </summary>
    public IReadOnlyList<EditorObject> WithAttached(IReadOnlyCollection<EditorObject> changed)
    {
        var objects = Swap(changed);
        return [.. objects.Where((o, i) => o != Current.Objects[i])];
    }

    /// <summary>Topmost object, not a line / arrow, whose local box grown by half its width plus the tolerance contains the point.</summary>
    public EditorObject? SnapTarget(Vector2 point, float tolerance, int exclude = 0)
    {
        for (int i = Objects.Count - 1; i >= 0; i--)
        {
            var o = Objects[i];
            if (o.Id == exclude || ObjectTransform.IsSegment(o)) continue;
            var box = EditorGeometry.LocalBox(o);
            var grow = new Vector2(EditorGeometry.HalfStroke(o) + tolerance);
            if (new FloatRect(box.Min - grow, box.Max + grow).Contains(EditorGeometry.ToLocal(o, point))) return o;
        }
        return null;
    }

    /// <summary>Moves the listed objects in z-order (one step or to the end), keeping their relative order, as one undo step.</summary>
    public void Reorder(IReadOnlyCollection<int> ids, ZOrder move)
    {
        var list = Current.Objects.ToList();
        bool Listed(int i) => ids.Contains(list[i].Id);
        switch (move)
        {
            case ZOrder.Up:
                for (int i = list.Count - 2; i >= 0; i--)
                    if (Listed(i) && !Listed(i + 1)) (list[i], list[i + 1]) = (list[i + 1], list[i]);
                break;
            case ZOrder.Down:
                for (int i = 1; i < list.Count; i++)
                    if (Listed(i) && !Listed(i - 1)) (list[i], list[i - 1]) = (list[i - 1], list[i]);
                break;
            default:
                var listed = list.Where(o => ids.Contains(o.Id)).ToList();
                var rest = list.Where(o => !ids.Contains(o.Id)).ToList();
                list = move == ZOrder.Front ? [.. rest, .. listed] : [.. listed, .. rest];
                break;
        }
        if (!list.SequenceEqual(Current.Objects)) Push(Current with { Objects = [.. list] });
    }

    /// <summary>
    /// Bucket: fills the topmost rectangle or ellipse whose inside or outline holds the point, else sets the canvas background;
    /// one undo step when anything changes.
    /// </summary>
    public void Fill(Vector2 point, Rgba color, float tolerance)
    {
        for (int i = Objects.Count - 1; i >= 0; i--)
        {
            if (Objects[i] is not Shape { Kind: ShapeKind.Rectangle or ShapeKind.Ellipse } s) continue;
            var local = EditorGeometry.ToLocal(s, point);
            if (Interior(s, local) || DistanceTo(s, local) <= EditorGeometry.HalfStroke(s) + tolerance)
            {
                Replace([s with { Fill = color }]);
                return;
            }
        }
        if (Background != color) Push(Current with { Background = color });
    }

    /// <summary>Black boxes over the image pixels, clipped to the image, as one undo step.</summary>
    public void Redact(IEnumerable<PixelRect> rects)
    {
        var image = new PixelRect(0, 0, Width, Height);
        var added = rects.Select(r => r.Intersect(image)).OfType<PixelRect>().ToList();
        if (added.Count > 0) Push(Current with { Redactions = Current.Redactions.AddRange(added) });
    }

    /// <summary>Removes every redaction box as one undo step; nothing to do without any.</summary>
    public void ClearRedactions()
    {
        if (Current.Redactions.Count > 0) Push(Current with { Redactions = [] });
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
            var local = EditorGeometry.ToLocal(o, point);
            if (o is Shape { Fill: not null } filled && Interior(filled, local)) return o;
            if (DistanceTo(o, local) <= EditorGeometry.HalfStroke(o) + tolerance) return o;
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

    private void PushObjects(ImmutableList<EditorObject> objects, PixelRect? crop) => Push(Settled(objects, crop));

    // The crop never reaches past the bounds, so erasing an object that grew it trims it again.
    private State Settled(ImmutableList<EditorObject> objects, PixelRect? crop)
    {
        var bounds = new PixelRect(0, 0, Width, Height);
        foreach (var o in objects)
            bounds = Union(bounds, OuterRect(o));
        return Current with { Objects = objects, Bounds = bounds, Crop = crop is { } c ? c.Intersect(bounds) ?? c : null };
    }

    /// <summary>Document at a saved state, clean, without undo history; new ids continue after the highest loaded id.</summary>
    internal static EditorDocument Restore(int width, int height, ImmutableList<EditorObject> objects, PixelRect? crop, Rgba? background)
    {
        var doc = new EditorDocument(width, height);
        doc._history[0] = doc.Settled(objects, crop) with { Background = background };
        doc._nextId = objects.IsEmpty ? 1 : objects.Max(o => o.Id) + 1;
        return doc;
    }

    /// <summary>Attachment of a line / arrow end dropped at the point, or null when no object is there.</summary>
    public Attachment? Attach(Vector2 point, float tolerance, int exclude = 0) =>
        SnapTarget(point, tolerance, exclude) is { } target ? new Attachment(target.Id, EditorGeometry.AnchorOf(target, point)) : null;

    private ImmutableList<EditorObject> Swap(IReadOnlyCollection<EditorObject> changed)
    {
        var objects = Current.Objects;
        var ids = new HashSet<int>();
        foreach (var obj in changed)
        {
            int index = objects.FindIndex(o => o.Id == obj.Id);
            if (index < 0 || objects[index] == obj) continue;
            objects = objects.SetItem(index, obj);
            ids.Add(obj.Id);
        }
        return ids.Count == 0 ? objects : Resolve(objects, ids);
    }

    // Ends attached to a changed target move onto its anchor; ends of changed lines that left their anchor, or whose target is gone, detach.
    private static ImmutableList<EditorObject> Resolve(ImmutableList<EditorObject> objects, IReadOnlySet<int> changed)
    {
        var byId = objects.ToDictionary(o => o.Id);
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i] is not Shape s || (s.StartAttach is null && s.EndAttach is null)) continue;
            bool moved = changed.Contains(s.Id);
            var (start, startAttach) = End(s.Start, s.StartAttach);
            var (end, endAttach) = End(s.End, s.EndAttach);
            var next = s with { Start = start, End = end, StartAttach = startAttach, EndAttach = endAttach };
            if (next != s) objects = objects.SetItem(i, next);

            (Vector2, Attachment?) End(Vector2 point, Attachment? attach)
            {
                if (attach is not { } a || !byId.TryGetValue(a.Target, out var target)) return (point, null);
                var anchored = EditorGeometry.AnchorPoint(target, a.Anchor);
                if (changed.Contains(a.Target)) return (anchored, a);
                return moved && Vector2.Distance(point, anchored) > AnchorSlack ? (point, null) : (point, a);
            }
        }
        return objects;
    }

    private static PixelRect Union(PixelRect a, PixelRect b) => PixelRect.FromCorners(Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));

    /// <summary>Pixels the object paints, rounded outward.</summary>
    private static PixelRect OuterRect(EditorObject o)
    {
        var e = EditorGeometry.Extent(o);
        return PixelRect.FromCorners((int)MathF.Floor(e.Min.X), (int)MathF.Floor(e.Min.Y),
            (int)MathF.Ceiling(e.Max.X), (int)MathF.Ceiling(e.Max.Y));
    }

    private static float DistanceTo(EditorObject o, Vector2 p) => o switch
    {
        Stroke s => DistanceToPolyline(p, s.Points),
        Shape { Kind: ShapeKind.Line } s => EditorGeometry.DistanceToSegment(p, s.Start, s.End),
        Shape { Kind: ShapeKind.Arrow } s => ArrowDistance(p, s),
        Shape { Kind: ShapeKind.Rectangle } s => DistanceToPolyline(p, RectangleOutline(s)),
        Shape { Kind: ShapeKind.Ellipse } s => DistanceToPolyline(p, EllipseOutline(s)),
        TextObject or EmojiObject => DistanceToBox(p, EditorGeometry.LocalBox(o)),
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

    /// <summary>Point (local frame) inside a rectangle's box or an ellipse.</summary>
    private static bool Interior(Shape s, Vector2 p)
    {
        var box = EditorGeometry.LocalBox(s);
        if (!box.Contains(p)) return false;
        if (s.Kind != ShapeKind.Ellipse) return true;
        var radius = (box.Max - box.Min) / 2;
        if (radius.X == 0 || radius.Y == 0) return false;
        var d = (p - (box.Min + box.Max) / 2) / radius;
        return d.LengthSquared() <= 1;
    }

    // 0 inside: text is grabbed anywhere in its box.
    private static float DistanceToBox(Vector2 p, FloatRect box) =>
        Vector2.Distance(p, Vector2.Clamp(p, box.Min, box.Max));

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
