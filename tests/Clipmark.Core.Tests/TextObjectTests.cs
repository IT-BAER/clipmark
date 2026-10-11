using System.Numerics;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class TextObjectTests
{
    private static readonly Rgba Red = new(255, 0, 0, 255);

    // "Hi" at (100,100), font size 20, layout 1.5 x 1.25 em: box 100..130 x 100..125.
    private static TextObject Text(int id = 1) => new(id, Red, 20, new(100, 100), "Hi", new(1.5f, 1.25f));

    private static (EditorDocument Doc, SelectionSession Sel, int Id) New()
    {
        var doc = new EditorDocument(400, 300);
        int id = doc.NewId();
        doc.Add(Text(id));
        return (doc, new SelectionSession(doc), id);
    }

    private static void AssertNear(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 0.01f, $"expected {expected}, got {actual}");

    [Fact]
    public void Size_follows_the_font_size_and_the_box_is_the_layout_box_without_stroke_growth()
    {
        var t = Text();
        AssertNear(new(30, 25), t.Size);
        AssertNear(new(60, 50), (t with { Width = 40 }).Size);
        var box = EditorGeometry.LocalBox(t);
        AssertNear(new(100, 100), box.Min);
        AssertNear(new(130, 125), box.Max);
        var extent = EditorGeometry.Extent(t);
        AssertNear(new(100, 100), extent.Min);
        AssertNear(new(130, 125), extent.Max);
    }

    [Fact]
    public void Rotated_text_extent_covers_the_turned_box()
    {
        var t = Text() with { Rotation = MathF.PI / 2 };
        var extent = EditorGeometry.Extent(t);
        // Center (115,112.5); 30 x 25 turned 90 degrees is 25 x 30.
        AssertNear(new(102.5f, 97.5f), extent.Min);
        AssertNear(new(127.5f, 127.5f), extent.Max);
    }

    [Fact]
    public void Hit_test_covers_the_whole_box_plus_tolerance_only()
    {
        var (doc, _, id) = New();
        Assert.Equal(id, doc.HitTest(new(115, 112), 2)?.Id);
        Assert.Equal(id, doc.HitTest(new(131, 112), 2)?.Id);
        Assert.Null(doc.HitTest(new(136, 112), 2));
        Assert.Equal(id, doc.SnapTarget(new(115, 112), 2)?.Id);
    }

    [Fact]
    public void Offset_moves_the_position()
    {
        var moved = (TextObject)Text().Offset(new(5, -5));
        AssertNear(new(105, 95), moved.Position);
        Assert.Equal("Hi", moved.Text);
    }

    [Fact]
    public void Corner_drag_scales_the_font_size_uniformly_and_edge_handles_are_off()
    {
        var (doc, s, id) = New();
        s.Begin(new(115, 112), false);
        s.End();
        Assert.Single(s.Selected);
        // Bottom right corner (130,125) to (160,140): the larger change wins, x 2.0 vs y 1.6.
        s.Begin(new(130, 125), false);
        s.Move(new(160, 140), false);
        s.End();
        var t = (TextObject)doc.Objects.Single(o => o.Id == id);
        Assert.Equal(40, t.Width, 3);
        AssertNear(new(100, 100), t.Position);

        // Right edge middle (160,125) moves the text instead of resizing it.
        s.Begin(new(160, 125), false);
        s.Move(new(170, 125), false);
        s.End();
        t = (TextObject)doc.Objects.Single(o => o.Id == id);
        Assert.Equal(40, t.Width, 3);
        AssertNear(new(110, 100), t.Position);
    }

    [Fact]
    public void Rewidth_changes_the_font_size_and_with_it_the_box()
    {
        var (doc, s, id) = New();
        s.SelectAll();
        s.Rewidth(_ => 10);
        var t = (TextObject)doc.Objects.Single(o => o.Id == id);
        AssertNear(new(115, 112.5f), EditorGeometry.LocalBox(t).Max);
    }

    [Fact]
    public void Group_scale_keeps_text_proportional()
    {
        var t = (TextObject)ObjectTransform.ScaleUniform(Text(), new(100, 100), 0.5f);
        Assert.Equal(10, t.Width, 3);
        AssertNear(new(100, 100), t.Position);
    }
}
