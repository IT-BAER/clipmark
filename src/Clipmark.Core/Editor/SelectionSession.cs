using System.Numerics;

namespace Clipmark.Core.Editor;

/// <summary>Transform frame of the selection: an unrotated box turned by Rotation about its center.</summary>
public sealed record SelectionFrame(FloatRect Box, float Rotation)
{
    public Vector2 Center => (Box.Min + Box.Max) / 2;

    /// <summary>World position of a corner or edge-middle handle.</summary>
    public Vector2 Handle(CropHandle handle)
    {
        var (min, max, c) = (Box.Min, Box.Max, Center);
        return ToWorld(handle switch
        {
            CropHandle.Left => new(min.X, c.Y),
            CropHandle.Top => new(c.X, min.Y),
            CropHandle.Right => new(max.X, c.Y),
            CropHandle.Bottom => new(c.X, max.Y),
            CropHandle.TopLeft => min,
            CropHandle.TopRight => new(max.X, min.Y),
            CropHandle.BottomLeft => new(min.X, max.Y),
            _ => max,
        });
    }

    /// <summary>World position of the rotate handle, <paramref name="offset"/> px above the top edge middle.</summary>
    public Vector2 RotateHandle(float offset) => ToWorld(new(Center.X, Box.Min.Y - offset));

    private Vector2 ToWorld(Vector2 p) => Rotation == 0 ? p : Vector2.Transform(p, Matrix3x2.CreateRotation(Rotation, Center));
}

/// <summary>Select tool: click / toggle-click, rubber band, move / resize / rotate / endpoint drags, and edits of the selected objects (each one undo step).</summary>
public sealed class SelectionSession(EditorDocument document)
{
    private enum Drag { None, Move, Resize, Rotate, Start, End }

    private static readonly CropHandle[] Corners = [CropHandle.TopLeft, CropHandle.TopRight, CropHandle.BottomLeft, CropHandle.BottomRight];
    private static readonly CropHandle[] Edges = [CropHandle.Left, CropHandle.Top, CropHandle.Right, CropHandle.Bottom];
    private const float SnapStep = MathF.PI / 12;

    private readonly HashSet<int> _selected = [];
    private Vector2 _start;
    private Drag _drag;
    private bool _toggle;
    private CropHandle _handle;
    private SelectionFrame? _frame;
    private EditorObject[] _originals = [];

    /// <summary>Hit distance for clicks in image px; the app scales it with the zoom.</summary>
    public float Tolerance { get; set; } = 6;
    /// <summary>Grab distance for frame, rotate and endpoint handles in image px.</summary>
    public float HandleTolerance { get; set; } = 6;
    /// <summary>Distance of the rotate handle above the frame's top edge in image px.</summary>
    public float RotateOffset { get; set; } = 24;
    public bool IsActive { get; private set; }

    /// <summary>Selected ids that still exist in the document (undo can remove objects).</summary>
    public IReadOnlySet<int> Selected
    {
        get
        {
            _selected.IntersectWith(document.Objects.Select(o => o.Id));
            return _selected;
        }
    }

    /// <summary>Rubber band while dragging on empty space, else null.</summary>
    public FloatRect? Band { get; private set; }

    /// <summary>Whole-pixel move of the selection while dragging it; the document changes on <see cref="End"/>.</summary>
    public Vector2 Offset { get; private set; }

    /// <summary>Transformed selected objects during a drag; empty when not dragging or nothing changed yet.</summary>
    public IReadOnlyList<EditorObject> Live { get; private set; } = [];

    /// <summary>Null without a selection or for a single line / arrow; follows <see cref="Live"/> while dragging.</summary>
    public SelectionFrame? Frame => Current() switch
    {
        [] => null,
        [var o] when ObjectTransform.IsSegment(o) => null,
        [var o] => new SelectionFrame(EditorGeometry.LocalBox(o), o.Rotation),
        var many => new SelectionFrame(many.Select(EditorGeometry.Extent).Aggregate((a, b) => a.Union(b)), 0),
    };

    public Vector2? RotateHandle => Frame?.RotateHandle(RotateOffset);

    /// <summary>Edge handles exist for a single selected object that can stretch (not text); else only corners.</summary>
    public bool HasEdgeHandles => Current() is [var o] && o is not TextObject;

    /// <summary>End points of a single selected line / arrow, else null.</summary>
    public (Vector2 Start, Vector2 End)? EndpointHandles =>
        Current() is [Shape s] && ObjectTransform.IsSegment(s) ? (s.Start, s.End) : null;

    /// <summary>Area the selected objects paint, or null without a selection.</summary>
    public FloatRect? SelectionBox
    {
        get
        {
            FloatRect? box = null;
            foreach (var o in SelectedObjects())
                box = box is { } b ? b.Union(EditorGeometry.Extent(o)) : EditorGeometry.Extent(o);
            return box;
        }
    }

    public void Begin(Vector2 point, bool toggle)
    {
        if (IsActive)
            return;
        IsActive = true;
        _start = point;
        _toggle = toggle;
        Offset = Vector2.Zero;
        Live = [];
        // Handles win over toggling: Shift is also the aspect / angle-snap modifier and is often held before the press.
        if (GrabHandle(point))
        {
            _frame = Frame;
            _originals = [.. SelectedObjects()];
            return;
        }
        var hit = document.HitTest(point, Tolerance);
        if (toggle)
        {
            if (hit is not null && !_selected.Remove(hit.Id))
                _selected.Add(hit.Id);
            _drag = Drag.None;
            if (hit is null)
                Band = new FloatRect(point, point);
            return;
        }
        if (hit is not null && !Selected.Contains(hit.Id))
        {
            _selected.Clear();
            _selected.Add(hit.Id);
        }
        _drag = hit is not null || SelectionBox?.Contains(point) == true ? Drag.Move : Drag.None;
        if (_drag == Drag.None)
        {
            _selected.Clear();
            Band = new FloatRect(point, point);
        }
        else
            _originals = [.. SelectedObjects()];
    }

    public void Move(Vector2 point, bool shift)
    {
        if (!IsActive)
            return;
        if (Band is not null)
        {
            Band = FloatRect.FromCorners(_start, point);
            return;
        }
        EditorObject[] live;
        switch (_drag)
        {
            case Drag.Move:
                var d = point - _start;
                if (shift)
                    d = MathF.Abs(d.X) >= MathF.Abs(d.Y) ? d with { Y = 0 } : d with { X = 0 };
                Offset = new Vector2(MathF.Round(d.X), MathF.Round(d.Y));
                var offset = Offset;
                live = [.. _originals.Select(o => o.Offset(offset))];
                break;
            case Drag.Rotate:
                live = Rotated(point, shift);
                break;
            case Drag.Resize when _originals.Length == 1:
                live = [ObjectTransform.Resize(_originals[0], _handle, point, keepAspect: shift || _originals[0] is TextObject)];
                break;
            case Drag.Resize:
                live = Scaled(point);
                break;
            case Drag.Start or Drag.End when _originals is [Shape line]:
                var other = _drag == Drag.Start ? line.End : line.Start;
                var end = shift ? EditorGeometry.SnapAngle(other, point) : point;
                var attach = document.Attach(end, Tolerance, exclude: line.Id);
                live = [_drag == Drag.Start ? line with { Start = end, StartAttach = attach } : line with { End = end, EndAttach = attach }];
                break;
            default:
                return;
        }
        Live = live.SequenceEqual(_originals) ? [] : live;
    }

    public void End()
    {
        if (!IsActive)
            return;
        IsActive = false;
        if (Band is { } band)
        {
            Band = null;
            foreach (var o in document.Objects)
                if (band.Contains(EditorGeometry.Extent(o)))
                    _selected.Add(o.Id);
        }
        else if (Live.Count > 0)
            document.Replace([.. Live]);
        _drag = Drag.None;
        _toggle = false;
        Offset = Vector2.Zero;
        Live = [];
        _originals = [];
        _frame = null;
    }

    public void Nudge(Vector2 delta) => Change(o => o.Offset(delta));

    public void Delete()
    {
        document.Remove([.. Selected]);
        _selected.Clear();
    }

    public void SelectAll() => _selected.UnionWith(document.Objects.Select(o => o.Id));

    public void Clear() => _selected.Clear();

    public void Reorder(ZOrder move) => document.Reorder([.. Selected], move);

    /// <summary>New RGB for the selection; every object keeps its own opacity (highlighter strokes stay at their fixed one).</summary>
    public void Recolor(Rgba color) => Change(o => o with
    {
        Color = color with { A = o is Stroke { Kind: StrokeKind.Highlighter } ? ToolSession.HighlighterAlpha : o.Color.A },
    });

    /// <summary>New opacity for the selection, RGB kept; highlighter strokes stay at their fixed one.</summary>
    public void Reopacity(byte alpha) => Change(o => o with
    {
        Color = o.Color with { A = o is Stroke { Kind: StrokeKind.Highlighter } ? ToolSession.HighlighterAlpha : alpha },
    });

    public void Rewidth(Func<EditorObject, float> width) => Change(o => o with { Width = width(o) });

    // Rotate handle first, then endpoints, corners, and (single selection only) edges.
    private bool GrabHandle(Vector2 point)
    {
        bool Near(Vector2 handle) => Vector2.Distance(point, handle) <= HandleTolerance;
        if (Frame is not { } frame)
        {
            if (EndpointHandles is not var (start, end))
                return false;
            return Near(start) ? Grab(Drag.Start) : Near(end) && Grab(Drag.End);
        }
        if (Near(frame.RotateHandle(RotateOffset)))
            return Grab(Drag.Rotate);
        CropHandle[] handles = HasEdgeHandles ? [.. Corners, .. Edges] : Corners;
        foreach (var h in handles)
            if (Near(frame.Handle(h)))
            {
                _handle = h;
                return Grab(Drag.Resize);
            }
        return false;
    }

    private bool Grab(Drag drag)
    {
        _drag = drag;
        return true;
    }

    private EditorObject[] Rotated(Vector2 point, bool shift)
    {
        var center = _frame!.Center;
        float Angle(Vector2 p) => MathF.Atan2(p.Y - center.Y, p.X - center.X);
        float delta = Angle(point) - Angle(_start);
        if (shift)
            delta = _originals is [var single] ? Snap(single.Rotation + delta) - single.Rotation : Snap(delta);
        return [.. _originals.Select(o => ObjectTransform.Rotate(o, center, delta))];
    }

    // Uniform group scale about the opposite frame corner; no object box shrinks below 1 px.
    private EditorObject[] Scaled(Vector2 point)
    {
        var anchor = _frame!.Handle(Opposite(_handle));
        float pressed = Vector2.Distance(_start, anchor);
        if (pressed == 0)
            return _originals;
        float factor = Vector2.Distance(point, anchor) / pressed;
        foreach (var o in _originals)
        {
            var box = EditorGeometry.LocalBox(o);
            var size = box.Max - box.Min;
            if (size.X > 0) factor = MathF.Max(factor, 1 / size.X);
            if (size.Y > 0) factor = MathF.Max(factor, 1 / size.Y);
        }
        if (factor <= 0)
            return _originals;
        return [.. _originals.Select(o => ObjectTransform.ScaleUniform(o, anchor, factor))];
    }

    private static CropHandle Opposite(CropHandle corner) => corner switch
    {
        CropHandle.TopLeft => CropHandle.BottomRight,
        CropHandle.TopRight => CropHandle.BottomLeft,
        CropHandle.BottomLeft => CropHandle.TopRight,
        _ => CropHandle.TopLeft,
    };

    private static float Snap(float radians) => MathF.Round(radians / SnapStep) * SnapStep;

    private IReadOnlyList<EditorObject> Current() => Live.Count > 0 ? Live : [.. SelectedObjects()];

    private IEnumerable<EditorObject> SelectedObjects()
    {
        var ids = Selected;
        return document.Objects.Where(o => ids.Contains(o.Id));
    }

    private void Change(Func<EditorObject, EditorObject> change)
    {
        EditorObject[] changed = [.. SelectedObjects().Select(change)];
        if (changed.Length > 0)
            document.Replace(changed);
    }
}
