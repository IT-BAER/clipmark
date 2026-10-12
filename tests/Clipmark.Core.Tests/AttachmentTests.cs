using System.Numerics;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class AttachmentTests
{
    private static readonly Rgba Red = new(255, 0, 0, 255);
    private static readonly Rgba Blue = new(0, 0, 255, 255);

    // A rectangle 100..200 x 100..150 and an arrow drawn from (20,20) to (150,125), its end inside the rectangle.
    private static (EditorDocument Doc, int Box, int Arrow) New()
    {
        var doc = new EditorDocument(400, 300);
        int box = doc.NewId();
        doc.Add(new Shape(box, ShapeKind.Rectangle, Red, 2, new(100, 100), new(200, 150)));
        var tool = new ToolSession(doc) { Tool = EditorTool.Arrow, Color = Red, Width = 2 };
        tool.Begin(new(20, 20), false);
        tool.Move(new(150, 125), false);
        tool.End();
        return (doc, box, doc.Objects[^1].Id);
    }

    private static T Get<T>(EditorDocument doc, int id) where T : EditorObject => (T)doc.Objects.Single(o => o.Id == id);

    private static void AssertNear(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 0.01f, $"expected {expected}, got {actual}");

    [Fact]
    public void Arrow_stays_free_when_attaching_is_off()
    {
        var doc = new EditorDocument(400, 300);
        doc.Add(new Shape(doc.NewId(), ShapeKind.Rectangle, Red, 2, new(100, 100), new(200, 150)));
        var tool = new ToolSession(doc) { Tool = EditorTool.Arrow, Color = Red, Width = 2, AttachEnabled = false };
        tool.Begin(new(150, 125), false);
        tool.Move(new(180, 125), false);
        tool.End();

        var a = (Shape)doc.Objects[^1];
        Assert.Null(a.StartAttach);
        Assert.Null(a.EndAttach);
    }

    [Fact]
    public void Drawing_an_arrow_end_onto_a_shape_attaches_it_at_the_dropped_spot()
    {
        var (doc, box, arrow) = New();
        var a = Get<Shape>(doc, arrow);
        Assert.Null(a.StartAttach);
        Assert.Equal(box, a.EndAttach?.Target);
        AssertNear(new(0.5f, 0.5f), a.EndAttach!.Value.Anchor);
        AssertNear(new(150, 125), a.End);
    }

    [Fact]
    public void Moving_the_target_moves_the_attached_end_in_the_same_undo_step()
    {
        var (doc, box, arrow) = New();
        doc.Replace([Get<Shape>(doc, box).Offset(new(50, 20))]);
        var a = Get<Shape>(doc, arrow);
        AssertNear(new(200, 145), a.End);
        AssertNear(new(20, 20), a.Start);
        Assert.Equal(box, a.EndAttach?.Target);
        doc.Undo();
        AssertNear(new(150, 125), Get<Shape>(doc, arrow).End);
    }

    [Fact]
    public void Resizing_and_rotating_the_target_keep_the_end_on_its_normalized_anchor()
    {
        var (doc, box, arrow) = New();
        doc.Replace([new Shape(box, ShapeKind.Rectangle, Red, 2, new(100, 100), new(300, 200))]);
        AssertNear(new(200, 150), Get<Shape>(doc, arrow).End);

        var (doc2, box2, arrow2) = New();
        var rotated = ObjectTransform.Rotate(Get<Shape>(doc2, box2), new(100, 100), MathF.PI / 2);
        doc2.Replace([rotated]);
        // (150,125) turned 90 degrees clockwise about (100,100) is (75,150).
        AssertNear(new(75, 150), Get<Shape>(doc2, arrow2).End);
    }

    [Fact]
    public void Moving_the_arrow_alone_detaches_it_and_a_recolor_keeps_the_attachment()
    {
        var (doc, _, arrow) = New();
        doc.Replace([Get<Shape>(doc, arrow) with { Color = Blue }]);
        Assert.NotNull(Get<Shape>(doc, arrow).EndAttach);
        doc.Replace([Get<Shape>(doc, arrow).Offset(new(0, 80))]);
        var a = Get<Shape>(doc, arrow);
        Assert.Null(a.EndAttach);
        AssertNear(new(150, 205), a.End);
    }

    [Fact]
    public void Moving_arrow_and_target_together_keeps_the_attachment()
    {
        var (doc, box, arrow) = New();
        var d = new Vector2(10, 10);
        doc.Replace([Get<Shape>(doc, box).Offset(d), Get<Shape>(doc, arrow).Offset(d)]);
        var a = Get<Shape>(doc, arrow);
        Assert.Equal(box, a.EndAttach?.Target);
        AssertNear(new(160, 135), a.End);
        AssertNear(new(30, 30), a.Start);
    }

    [Fact]
    public void Deleting_the_target_keeps_the_arrow_detached_and_undo_reattaches()
    {
        var (doc, box, arrow) = New();
        doc.Remove([box]);
        var a = Get<Shape>(doc, arrow);
        Assert.Null(a.EndAttach);
        AssertNear(new(150, 125), a.End);
        doc.Undo();
        Assert.Equal(box, Get<Shape>(doc, arrow).EndAttach?.Target);
    }

    [Fact]
    public void WithAttached_previews_dependent_arrows_without_changing_the_document()
    {
        var (doc, box, arrow) = New();
        var moved = Get<Shape>(doc, box).Offset(new(0, 40));
        var preview = doc.WithAttached([moved]);
        Assert.Equal(2, preview.Count);
        AssertNear(new(150, 165), ((Shape)preview.Single(o => o.Id == arrow)).End);
        AssertNear(new(150, 125), Get<Shape>(doc, arrow).End);
    }

    [Fact]
    public void SnapTarget_is_the_topmost_non_segment_object_under_the_point()
    {
        var (doc, box, arrow) = New();
        Assert.Equal(box, doc.SnapTarget(new(150, 125), 2)?.Id);
        Assert.Equal(box, doc.SnapTarget(new(99, 125), 2)?.Id);
        Assert.Null(doc.SnapTarget(new(60, 60), 2)); // only the arrow's shaft is here
        Assert.Null(doc.SnapTarget(new(150, 125), 2, exclude: box));
        Assert.NotEqual(arrow, doc.SnapTarget(new(150, 125), 2)?.Id);
    }

    [Fact]
    public void Dragging_an_end_point_onto_a_shape_attaches_and_off_it_detaches()
    {
        var doc = new EditorDocument(400, 300);
        int box = doc.NewId(), line = doc.NewId();
        doc.Add(new Shape(box, ShapeKind.Ellipse, Red, 2, new(100, 100), new(200, 200)));
        doc.Add(new Shape(line, ShapeKind.Arrow, Red, 2, new(20, 250), new(80, 250)));
        var s = new SelectionSession(doc);
        s.Begin(new(50, 250), false);
        s.End();
        s.Begin(new(80, 250), false);
        s.Move(new(150, 150), false);
        Assert.Equal(box, ((Shape)s.Live[0]).EndAttach?.Target);
        s.End();
        Assert.Equal(box, Get<Shape>(doc, line).EndAttach?.Target);
        s.Begin(new(150, 150), false);
        s.Move(new(300, 50), false);
        s.End();
        Assert.Null(Get<Shape>(doc, line).EndAttach);
    }
}
