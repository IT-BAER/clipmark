using System.Numerics;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class ToolSessionTests
{
    private static readonly Rgba Red = new(232, 17, 35, 255);

    private static (EditorDocument Doc, ToolSession Session) New(EditorTool tool, float width = 4)
    {
        var doc = new EditorDocument(400, 300);
        return (doc, new ToolSession(doc) { Tool = tool, Color = Red, Width = width });
    }

    private static void Drag(ToolSession s, bool shift, params Vector2[] points)
    {
        s.Begin(points[0], shift);
        foreach (var p in points.Skip(1))
            s.Move(p, shift);
        s.End();
    }

    [Fact]
    public void Pen_drag_adds_one_stroke_as_one_undo_step()
    {
        var (doc, s) = New(EditorTool.Pen);
        Drag(s, false, new(50, 150), new(50.4f, 150), new(200, 150), new(350, 150));

        var stroke = Assert.IsType<Stroke>(Assert.Single(doc.Objects));
        Assert.Equal(StrokeKind.Pen, stroke.Kind);
        Assert.Equal(Red, stroke.Color);
        Assert.Equal(4, stroke.Width);
        Assert.Equal([new(50, 150), new(200, 150), new(350, 150)], stroke.Points.ToArray()); // thinned
        Assert.Null(s.Live);
        Assert.False(s.IsActive);
        doc.Undo();
        Assert.Empty(doc.Objects);
    }

    [Fact]
    public void Live_stroke_follows_the_pointer_until_release()
    {
        var (doc, s) = New(EditorTool.Pen);
        s.Begin(new(10, 10), false);
        s.Move(new(20, 10), false);
        var live = Assert.IsType<Stroke>(s.Live);
        Assert.Equal([new(10, 10), new(20, 10)], live.Points.ToArray());
        Assert.Empty(doc.Objects);
    }

    [Fact]
    public void Click_without_move_adds_a_dot()
    {
        var (doc, s) = New(EditorTool.Pen);
        Drag(s, false, new Vector2(30, 40));
        Assert.Equal([new(30, 40)], Assert.IsType<Stroke>(Assert.Single(doc.Objects)).Points.ToArray());
    }

    [Fact]
    public void Highlighter_is_half_transparent()
    {
        var (doc, s) = New(EditorTool.Highlighter, 20);
        Drag(s, false, new(50, 60), new(350, 60), new(50, 60));
        var stroke = Assert.IsType<Stroke>(Assert.Single(doc.Objects));
        Assert.Equal(StrokeKind.Highlighter, stroke.Kind);
        Assert.Equal(Red with { A = 128 }, stroke.Color);
    }

    [Theory]
    [InlineData(EditorTool.Rectangle, ShapeKind.Rectangle)]
    [InlineData(EditorTool.Ellipse, ShapeKind.Ellipse)]
    [InlineData(EditorTool.Line, ShapeKind.Line)]
    [InlineData(EditorTool.Arrow, ShapeKind.Arrow)]
    public void Shape_tools_add_a_shape_from_press_to_release(EditorTool tool, ShapeKind kind)
    {
        var (doc, s) = New(tool);
        Drag(s, false, new(100, 200), new(200, 230), new(300, 260));
        var shape = Assert.IsType<Shape>(Assert.Single(doc.Objects));
        Assert.Equal((kind, new Vector2(100, 200), new Vector2(300, 260)), (shape.Kind, shape.Start, shape.End));
    }

    [Fact]
    public void Shape_click_without_move_adds_nothing()
    {
        var (doc, s) = New(EditorTool.Rectangle);
        Drag(s, false, new Vector2(100, 200));
        Assert.Empty(doc.Objects);
        Assert.False(doc.CanUndo);
    }

    [Theory]
    [InlineData(EditorTool.Line)]
    [InlineData(EditorTool.Arrow)]
    public void Shift_snaps_lines_and_arrows_to_45_degrees(EditorTool tool)
    {
        var (doc, s) = New(tool);
        Drag(s, true, new(50, 280), new(350, 290));
        var shape = Assert.IsType<Shape>(Assert.Single(doc.Objects));
        Assert.Equal(280, shape.End.Y, 3);
    }

    [Fact]
    public void Shift_does_not_change_rectangles()
    {
        var (doc, s) = New(EditorTool.Rectangle);
        Drag(s, true, new(50, 280), new(350, 290));
        Assert.Equal(new Vector2(350, 290), Assert.IsType<Shape>(Assert.Single(doc.Objects)).End);
    }

    [Fact]
    public void Eraser_drag_removes_every_touched_object_as_one_undo_step()
    {
        var (doc, s) = New(EditorTool.Pen);
        Drag(s, false, new(50, 150), new(350, 150));
        Drag(s, false, new(50, 60), new(350, 60));
        Drag(s, false, new(50, 250), new(350, 250));
        s.Tool = EditorTool.Eraser;

        s.Begin(new(200, 40), false);
        s.Move(new(200, 60), false);
        s.Move(new(200, 150), false);
        Assert.Equal(2, s.Erased.Count);
        Assert.Equal(3, doc.Objects.Count); // removed only on release
        s.End();

        Assert.Single(doc.Objects);
        Assert.Empty(s.Erased);
        doc.Undo();
        Assert.Equal(3, doc.Objects.Count);
    }

    [Fact]
    public void Eraser_miss_adds_no_undo_step()
    {
        var (doc, s) = New(EditorTool.Pen);
        Drag(s, false, new(50, 150), new(350, 150));
        s.Tool = EditorTool.Eraser;
        Drag(s, false, new(200, 10), new(200, 20));
        Assert.Single(doc.Objects);
        doc.Undo();
        Assert.Empty(doc.Objects);
    }

    [Fact]
    public void Move_and_end_without_begin_do_nothing()
    {
        var (doc, s) = New(EditorTool.Pen);
        s.Move(new(10, 10), false);
        s.End();
        Assert.Null(s.Live);
        Assert.Empty(doc.Objects);
    }
}
