using System.Numerics;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class EmojiObjectTests
{
    // Heart at (100,100), 40 px: box 100..140 x 100..140.
    private static EmojiObject Heart(int id = 1) => new(id, 40, new(100, 100), "❤️");

    private static (EditorDocument Doc, SelectionSession Sel, int Id) New()
    {
        var doc = new EditorDocument(400, 300);
        int id = doc.NewId();
        doc.Add(Heart(id));
        return (doc, new SelectionSession(doc), id);
    }

    private static void AssertNear(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 0.01f, $"expected {expected}, got {actual}");

    private static void Drag(ToolSession s, params Vector2[] points)
    {
        s.Begin(points[0], false);
        foreach (var p in points.Skip(1))
            s.Move(p, false);
        s.End();
    }

    [Fact]
    public void The_box_is_a_square_of_Width_without_stroke_growth()
    {
        var box = EditorGeometry.LocalBox(Heart());
        AssertNear(new(100, 100), box.Min);
        AssertNear(new(140, 140), box.Max);
        var extent = EditorGeometry.Extent(Heart());
        AssertNear(new(100, 100), extent.Min);
        AssertNear(new(140, 140), extent.Max);
    }

    [Fact]
    public void Hit_test_covers_the_whole_box_plus_tolerance_only()
    {
        var (doc, _, id) = New();
        Assert.Equal(id, doc.HitTest(new(120, 120), 2)?.Id);
        Assert.Equal(id, doc.HitTest(new(141, 120), 2)?.Id);
        Assert.Null(doc.HitTest(new(146, 120), 2));
    }

    [Fact]
    public void Corner_drag_keeps_the_square_and_edge_handles_are_off()
    {
        var (doc, s, id) = New();
        s.Begin(new(120, 120), false);
        s.End();
        Assert.False(s.HasEdgeHandles);
        // Bottom right corner (140,140) to (180,150): the larger change wins, x 2.0 vs y 1.25.
        s.Begin(new(140, 140), false);
        s.Move(new(180, 150), false);
        s.End();
        var e = (EmojiObject)doc.Objects.Single(o => o.Id == id);
        Assert.Equal(80, e.Width, 3);
        AssertNear(new(100, 100), e.Position);
    }

    [Fact]
    public void Rotate_turns_about_the_center()
    {
        var e = (EmojiObject)ObjectTransform.Rotate(Heart(), new(120, 120), 0.5f);
        Assert.Equal(0.5f, e.Rotation, 4);
        AssertNear(new(100, 100), e.Position);
    }

    [Fact]
    public void Group_scale_keeps_the_emoji_square()
    {
        var e = (EmojiObject)ObjectTransform.ScaleUniform(Heart(), new(100, 100), 0.5f);
        Assert.Equal(20, e.Width, 3);
        AssertNear(new(100, 100), e.Position);
    }

    [Fact]
    public void Color_and_width_controls_leave_an_emoji_unchanged()
    {
        var (doc, s, id) = New();
        s.SelectAll();
        s.Recolor(new Rgba(255, 0, 0, 255));
        s.Rewidth(_ => 4);
        Assert.Equal(Heart(id), doc.Objects.Single());
    }

    [Fact]
    public void Drag_with_the_emoji_tool_places_a_square_in_the_drag_direction_as_one_undo_step()
    {
        var doc = new EditorDocument(400, 300);
        var tool = new ToolSession(doc) { Tool = EditorTool.Emoji, Emoji = "🔥" };
        Drag(tool, new(100, 100), new(140, 120));
        var e = Assert.IsType<EmojiObject>(Assert.Single(doc.Objects));
        Assert.Equal("🔥", e.Emoji);
        Assert.Equal(40, e.Width, 3);
        AssertNear(new(100, 100), e.Position);
        Assert.Null(tool.Live);

        Drag(tool, new(100, 100), new(70, 90));
        e = (EmojiObject)doc.Objects[^1];
        Assert.Equal(30, e.Width, 3);
        AssertNear(new(70, 70), e.Position);

        doc.Undo();
        Assert.Single(doc.Objects);
    }

    [Fact]
    public void Click_with_the_emoji_tool_centers_the_default_size_on_the_point()
    {
        var doc = new EditorDocument(400, 300);
        var tool = new ToolSession(doc) { Tool = EditorTool.Emoji, Emoji = "⭐", EmojiSize = 80 };
        Drag(tool, new Vector2(200, 150));
        var e = Assert.IsType<EmojiObject>(Assert.Single(doc.Objects));
        Assert.Equal(80, e.Width, 3);
        AssertNear(new(160, 110), e.Position);
    }

    [Fact]
    public void The_emoji_tool_without_an_emoji_adds_nothing()
    {
        var doc = new EditorDocument(400, 300);
        Drag(new ToolSession(doc) { Tool = EditorTool.Emoji, Emoji = "" }, new(100, 100), new(140, 140));
        Drag(new ToolSession(doc) { Tool = EditorTool.Emoji, Emoji = new string('x', DocumentJson.MaxEmojiLength + 1) }, new(100, 100), new(140, 140));
        Assert.Empty(doc.Objects);
        // A broken default size still gives a box the file format accepts.
        Drag(new ToolSession(doc) { Tool = EditorTool.Emoji, Emoji = "⭐", EmojiSize = 0 }, new Vector2(100, 100));
        Assert.Equal(1, Assert.Single(doc.Objects).Width);
    }
}
