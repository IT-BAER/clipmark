using System.Numerics;

namespace Clipmark.Core.Editor;

/// <summary>Shape tools come last (Rectangle and up); Text, Eyedropper and Bucket act on a click in the app, not in a drag session.</summary>
public enum EditorTool { Select, Pen, Highlighter, Eraser, Text, Eyedropper, Bucket, Rectangle, Ellipse, Line, Arrow }

/// <summary>One pointer drag with the current tool; commits to the document on <see cref="End"/> as one undo step.</summary>
public sealed class ToolSession(EditorDocument document)
{
    public const float EraserTolerance = 6;
    public const byte HighlighterAlpha = 128;

    private readonly List<Vector2> _points = [];
    private readonly HashSet<int> _erased = [];

    public EditorTool Tool { get; set; }
    public Rgba Color { get; set; }
    public float Width { get; set; }
    /// <summary>Distance in image px within which a line / arrow end attaches to an object; the app scales it with the zoom.</summary>
    public float SnapTolerance { get; set; } = 6;
    /// <summary>False = line / arrow ends never attach (premium feature locked).</summary>
    public bool AttachEnabled { get; set; } = true;
    public bool IsActive { get; private set; }

    /// <summary>In-progress stroke or shape (Id 0), drawn on top until the drag ends.</summary>
    public EditorObject? Live { get; private set; }

    /// <summary>Objects the eraser touched in this drag; hidden while dragging, removed on release.</summary>
    public IReadOnlySet<int> Erased => _erased;

    public void Begin(Vector2 point, bool shift)
    {
        if (IsActive)
            return;
        IsActive = true;
        _points.Clear();
        _points.Add(point);
        Update(shift);
    }

    public void Move(Vector2 point, bool shift)
    {
        if (!IsActive)
            return;
        if (Tool is EditorTool.Pen or EditorTool.Highlighter or EditorTool.Eraser || _points.Count == 1)
            _points.Add(point);
        else
            _points[1] = point;
        Update(shift);
    }

    public void End()
    {
        if (!IsActive)
            return;
        IsActive = false;
        var live = Live;
        Live = null;
        int[] erased = [.. _erased];
        _erased.Clear();
        switch (live)
        {
            case Stroke s:
                document.Add(s with { Id = document.NewId(), Points = EditorGeometry.Thin(s.Points, 1) });
                break;
            case Shape s when s.Start != s.End:
                document.Add(s with { Id = document.NewId() });
                break;
            case null when erased.Length > 0:
                document.Remove(erased);
                break;
        }
    }

    private void Update(bool shift)
    {
        var start = _points[0];
        var end = _points[^1];
        switch (Tool)
        {
            case EditorTool.Eraser:
                if (document.HitTest(end, EraserTolerance) is { } hit)
                    _erased.Add(hit.Id);
                break;
            case EditorTool.Pen or EditorTool.Highlighter:
                var kind = Tool == EditorTool.Pen ? StrokeKind.Pen : StrokeKind.Highlighter;
                var color = kind == StrokeKind.Highlighter ? Color with { A = HighlighterAlpha } : Color;
                Live = new Stroke(0, kind, color, Width, [.. _points]);
                break;
            default:
                var shape = Tool switch
                {
                    EditorTool.Rectangle => ShapeKind.Rectangle,
                    EditorTool.Ellipse => ShapeKind.Ellipse,
                    EditorTool.Line => ShapeKind.Line,
                    _ => ShapeKind.Arrow,
                };
                if (shift && shape is ShapeKind.Line or ShapeKind.Arrow)
                    end = EditorGeometry.SnapAngle(start, end);
                Live = shape is ShapeKind.Line or ShapeKind.Arrow && AttachEnabled
                    ? new Shape(0, shape, Color, Width, start, end)
                    {
                        StartAttach = document.Attach(start, SnapTolerance), EndAttach = document.Attach(end, SnapTolerance),
                    }
                    : new Shape(0, shape, Color, Width, start, end);
                break;
        }
    }
}
