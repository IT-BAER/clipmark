using System.Numerics;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class EditorDocumentTests
{
    private static readonly Rgba Red = new(255, 0, 0, 255);

    private static Stroke Pen(int id, params Vector2[] points) =>
        new(id, StrokeKind.Pen, Red, 4, [.. points]);

    private static Shape Box(int id, ShapeKind kind, float x1, float y1, float x2, float y2, float width = 2) =>
        new(id, kind, Red, width, new Vector2(x1, y1), new Vector2(x2, y2));

    private static int[] Ids(EditorDocument doc) => [.. doc.Objects.Select(o => o.Id)];

    private static EditorDocument WithThree(out int a, out int b, out int c)
    {
        var doc = new EditorDocument(100, 80);
        a = doc.NewId(); b = doc.NewId(); c = doc.NewId();
        doc.Add(Pen(a, new Vector2(0, 0)));
        doc.Add(Pen(b, new Vector2(1, 1)));
        doc.Add(Pen(c, new Vector2(2, 2)));
        return doc;
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void Non_positive_size_throws(int w, int h)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditorDocument(w, h));
    }

    [Fact]
    public void New_document_is_empty_clean_uncropped_and_bounded_by_the_image()
    {
        var doc = new EditorDocument(100, 80);
        Assert.Null(doc.Crop);
        Assert.Equal(new PixelRect(0, 0, 100, 80), doc.Bounds);
        Assert.Empty(doc.Objects);
        Assert.False(doc.CanUndo);
        Assert.False(doc.CanRedo);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void NewId_is_unique_and_increasing()
    {
        var doc = new EditorDocument(10, 10);
        int a = doc.NewId(), b = doc.NewId(), c = doc.NewId();
        Assert.True(a < b && b < c);
    }

    [Fact]
    public void Add_then_undo_then_redo()
    {
        var doc = new EditorDocument(100, 80);
        var s = Pen(doc.NewId(), new Vector2(1, 1));
        doc.Add(s);
        Assert.Equal([s.Id], Ids(doc));
        Assert.True(doc.CanUndo);

        doc.Undo();
        Assert.Empty(doc.Objects);
        Assert.True(doc.CanRedo);

        doc.Redo();
        Assert.Same(s, doc.Objects[0]);
    }

    [Fact]
    public void Remove_takes_all_listed_ids_as_one_undo_step_and_restores_z_order()
    {
        var doc = WithThree(out int a, out int b, out int c);
        doc.Remove([a, c, 999]);
        Assert.Equal([b], Ids(doc));

        doc.Undo();
        Assert.Equal([a, b, c], Ids(doc));

        doc.Redo();
        Assert.Equal([b], Ids(doc));
    }

    [Fact]
    public void Remove_of_unknown_ids_is_a_no_op()
    {
        var doc = WithThree(out _, out _, out _);
        int events = 0;
        doc.Changed += () => events++;
        doc.Remove([999]);
        Assert.Equal(0, events);

        doc.Undo(); // still the Add step, so Remove created no undo step
        Assert.Equal(2, doc.Objects.Count);
    }

    [Fact]
    public void SetCrop_is_one_undo_step()
    {
        var doc = new EditorDocument(100, 80);
        doc.SetCrop(new PixelRect(10, 20, 30, 40));
        Assert.Equal(new PixelRect(10, 20, 30, 40), doc.Crop);

        doc.Undo();
        Assert.Null(doc.Crop);
        doc.Redo();
        Assert.Equal(new PixelRect(10, 20, 30, 40), doc.Crop);
    }

    [Fact]
    public void SetCrop_clamps_to_the_image()
    {
        var doc = new EditorDocument(100, 80);
        doc.SetCrop(new PixelRect(-10, 50, 60, 100));
        Assert.Equal(new PixelRect(0, 50, 50, 30), doc.Crop);
    }

    [Fact]
    public void SetCrop_clamps_to_the_bounds_including_objects_outside_the_image()
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(Pen(doc.NewId(), new Vector2(-10, 40), new Vector2(50, 40)));
        doc.SetCrop(new PixelRect(-50, 30, 100, 100));
        Assert.Equal(PixelRect.FromCorners(-12, 30, 50, 80), doc.Crop);
    }

    [Fact]
    public void Add_outside_an_applied_crop_grows_the_crop_and_undo_shrinks_it_back()
    {
        var doc = new EditorDocument(100, 80);
        doc.SetCrop(new PixelRect(10, 10, 50, 50));
        doc.Add(Pen(doc.NewId(), new Vector2(150, 20)));
        Assert.Equal(PixelRect.FromCorners(10, 10, 152, 60), doc.Crop);
        doc.Undo();
        Assert.Equal(new PixelRect(10, 10, 50, 50), doc.Crop);
    }

    [Fact]
    public void Add_inside_an_applied_crop_or_without_crop_keeps_the_crop()
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(Pen(doc.NewId(), new Vector2(150, 20)));
        Assert.Null(doc.Crop);
        doc.SetCrop(new PixelRect(10, 10, 50, 50));
        doc.Add(Pen(doc.NewId(), new Vector2(30, 30)));
        Assert.Equal(new PixelRect(10, 10, 50, 50), doc.Crop);
    }

    [Fact]
    public void Remove_trims_a_grown_crop_to_the_new_bounds()
    {
        var doc = new EditorDocument(100, 80);
        doc.SetCrop(new PixelRect(10, 10, 50, 50));
        int id = doc.NewId();
        doc.Add(Pen(id, new Vector2(150, 20)));
        doc.Remove([id]);
        Assert.Equal(PixelRect.FromCorners(10, 10, 100, 60), doc.Crop);
    }

    [Fact]
    public void Bounds_grow_left_and_up_by_the_object_outline_including_half_the_width()
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(Pen(doc.NewId(), new Vector2(-10, -20), new Vector2(50, 50)));
        Assert.Equal(PixelRect.FromCorners(-12, -22, 100, 80), doc.Bounds);
    }

    [Fact]
    public void Bounds_round_fractional_edges_outward()
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(new Stroke(doc.NewId(), StrokeKind.Highlighter, Red, 1, [new(-0.2f, 10), new(100.6f, 80.2f)]));
        Assert.Equal(PixelRect.FromCorners(-1, 0, 102, 81), doc.Bounds);
    }

    [Theory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.Ellipse)]
    [InlineData(ShapeKind.Line)]
    public void Bounds_cover_shape_corners_plus_half_the_width(ShapeKind kind)
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(Box(doc.NewId(), kind, 120, 90, -10, 10, width: 6));
        Assert.Equal(PixelRect.FromCorners(-13, 0, 123, 93), doc.Bounds);
    }

    [Fact]
    public void Bounds_cover_the_arrow_head_wings()
    {
        var doc = new EditorDocument(100, 80);
        var arrow = Box(doc.NewId(), ShapeKind.Arrow, 50, 0, 100, 0);
        doc.Add(arrow);
        var (left, _) = EditorGeometry.ArrowHead(arrow.Start, arrow.End, arrow.Width);
        Assert.True(left.Y < -5); // the wing, not the shaft (top -1), sets the top edge
        Assert.Equal((int)MathF.Floor(left.Y - 1), doc.Bounds.Y);
        Assert.Equal(101, doc.Bounds.Right);
    }

    [Fact]
    public void Bounds_shrink_back_on_undo_and_remove_and_grow_on_redo()
    {
        var doc = new EditorDocument(100, 80);
        var image = new PixelRect(0, 0, 100, 80);
        int id = doc.NewId();
        doc.Add(Pen(id, new Vector2(150, 40)));
        var grown = PixelRect.FromCorners(0, 0, 152, 80);
        Assert.Equal(grown, doc.Bounds);

        doc.Undo();
        Assert.Equal(image, doc.Bounds);
        doc.Redo();
        Assert.Equal(grown, doc.Bounds);
        doc.Remove([id]);
        Assert.Equal(image, doc.Bounds);
    }

    [Fact]
    public void SetCrop_that_is_empty_after_clamp_or_equal_to_current_is_a_no_op()
    {
        var doc = new EditorDocument(100, 80);
        int events = 0;
        doc.Changed += () => events++;
        doc.SetCrop(new PixelRect(200, 0, 10, 10));
        doc.SetCrop(new PixelRect(0, 0, 100, 80));
        doc.SetCrop(new PixelRect(5, 5, 0, 10));
        Assert.Equal(0, events);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void New_command_clears_redo()
    {
        var doc = WithThree(out _, out _, out _);
        doc.Undo();
        Assert.True(doc.CanRedo);
        doc.Add(Pen(doc.NewId(), new Vector2(5, 5)));
        Assert.False(doc.CanRedo);

        doc.Undo();
        doc.SetCrop(new PixelRect(1, 1, 10, 10));
        Assert.False(doc.CanRedo);
    }

    [Fact]
    public void Undo_and_redo_are_no_ops_when_impossible()
    {
        var doc = new EditorDocument(10, 10);
        int events = 0;
        doc.Changed += () => events++;
        doc.Undo();
        doc.Redo();
        Assert.Equal(0, events);
    }

    [Fact]
    public void Changed_fires_once_per_state_change()
    {
        var doc = new EditorDocument(100, 80);
        int events = 0;
        doc.Changed += () => events++;
        int id = doc.NewId();
        doc.Add(Pen(id, new Vector2(1, 1)));
        Assert.Equal(1, events);
        doc.Remove([id, id + 50]);
        Assert.Equal(2, events);
        doc.SetCrop(new PixelRect(1, 1, 5, 5));
        Assert.Equal(3, events);
        doc.Undo();
        Assert.Equal(4, events);
        doc.Redo();
        Assert.Equal(5, events);
    }

    [Fact]
    public void Revision_identifies_the_content_state_across_undo_and_new_edits()
    {
        var doc = new EditorDocument(100, 80);
        object original = doc.Revision;
        doc.Add(Pen(doc.NewId(), new Vector2(1, 1)));
        object first = doc.Revision;
        Assert.NotEqual(original, first);

        doc.Undo();
        Assert.Equal(original, doc.Revision);

        doc.Add(Pen(doc.NewId(), new Vector2(2, 2))); // same undo position as "first", different content
        Assert.NotEqual(first, doc.Revision);
        Assert.NotEqual(original, doc.Revision);
    }

    [Fact]
    public void IsDirty_tracks_the_undo_position_against_the_clean_point()
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(Pen(doc.NewId(), new Vector2(1, 1)));
        Assert.True(doc.IsDirty);

        doc.MarkClean();
        Assert.False(doc.IsDirty);

        doc.Add(Pen(doc.NewId(), new Vector2(2, 2)));
        Assert.True(doc.IsDirty);
        doc.Undo();
        Assert.False(doc.IsDirty);
        doc.Undo();
        Assert.True(doc.IsDirty);
        doc.Redo();
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void IsDirty_stays_true_when_the_clean_state_was_discarded_from_redo()
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(Pen(doc.NewId(), new Vector2(1, 1)));
        doc.MarkClean();
        doc.Undo();
        doc.Add(Pen(doc.NewId(), new Vector2(2, 2)));
        Assert.True(doc.IsDirty);
        doc.Undo();
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void HitTest_returns_the_topmost_object()
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(Pen(1, new Vector2(0, 0), new Vector2(10, 0)));
        doc.Add(Pen(2, new Vector2(0, 0), new Vector2(10, 0)));
        Assert.Equal(2, doc.HitTest(new Vector2(5, 0), 1)?.Id);
    }

    [Fact]
    public void HitTest_misses_empty_space()
    {
        var doc = WithThree(out _, out _, out _);
        Assert.Null(doc.HitTest(new Vector2(50, 50), 1));
    }

    [Fact]
    public void HitTest_stroke_tolerance_boundary_is_half_width_plus_tolerance()
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(Pen(1, new Vector2(0, 0), new Vector2(10, 0))); // width 4
        Assert.NotNull(doc.HitTest(new Vector2(5, 3), 1)); // 3 <= 2 + 1
        Assert.Null(doc.HitTest(new Vector2(5, 3.5f), 1));
        Assert.NotNull(doc.HitTest(new Vector2(12.5f, 0), 1)); // past the end: distance to endpoint 2.5
        Assert.Null(doc.HitTest(new Vector2(13.5f, 0), 1));
    }

    [Fact]
    public void HitTest_single_point_stroke_uses_point_distance()
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(Pen(1, new Vector2(10, 10)));
        Assert.NotNull(doc.HitTest(new Vector2(10, 12.5f), 1));
        Assert.Null(doc.HitTest(new Vector2(10, 13.5f), 1));
    }

    [Fact]
    public void HitTest_line_uses_the_segment_regardless_of_direction()
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(Box(1, ShapeKind.Line, 50, 0, 0, 0)); // width 2
        Assert.NotNull(doc.HitTest(new Vector2(25, 0.9f), 0));
        Assert.Null(doc.HitTest(new Vector2(25, 2), 0));
    }

    [Fact]
    public void HitTest_rectangle_hits_the_outline_but_not_the_interior()
    {
        var doc = new EditorDocument(200, 100);
        doc.Add(Box(1, ShapeKind.Rectangle, 100, 50, 0, 0)); // corners given reversed
        Assert.NotNull(doc.HitTest(new Vector2(50, 0.5f), 0));
        Assert.NotNull(doc.HitTest(new Vector2(100, 25), 0));
        Assert.Null(doc.HitTest(new Vector2(50, 25), 0)); // interior
        Assert.Null(doc.HitTest(new Vector2(50, 1.5f), 0));
    }

    [Fact]
    public void HitTest_ellipse_hits_the_outline_but_not_the_center()
    {
        var doc = new EditorDocument(200, 100);
        doc.Add(Box(1, ShapeKind.Ellipse, 0, 0, 100, 50));
        Assert.NotNull(doc.HitTest(new Vector2(100, 25), 0)); // right extreme
        Assert.NotNull(doc.HitTest(new Vector2(50, 0), 0)); // top extreme
        Assert.Null(doc.HitTest(new Vector2(50, 25), 0));
    }

    [Fact]
    public void HitTest_arrow_hits_shaft_and_head_wings()
    {
        var doc = new EditorDocument(200, 100);
        doc.Add(Box(1, ShapeKind.Arrow, 0, 0, 100, 0)); // wing tips at (89.608, 6) and (89.608, -6)
        Assert.NotNull(doc.HitTest(new Vector2(40, 0), 0));
        Assert.NotNull(doc.HitTest(new Vector2(94.8f, -3), 0.2f)); // midpoint of the upper wing
        Assert.NotNull(doc.HitTest(new Vector2(94.8f, 3), 0.2f));
        Assert.Null(doc.HitTest(new Vector2(40, 5), 0));
    }

    [Fact]
    public void Replace_swaps_objects_by_id_in_place_as_one_undo_step()
    {
        var doc = WithThree(out int a, out int b, out int c);
        var movedA = Pen(a, new Vector2(10, 10));
        var movedC = Pen(c, new Vector2(20, 20));
        doc.Replace([movedA, movedC]);

        Assert.Equal([a, b, c], Ids(doc));
        Assert.Equal(movedA, doc.Objects[0]);
        Assert.Equal(movedC, doc.Objects[2]);
        doc.Undo();
        Assert.Equal(Pen(a, new Vector2(0, 0)), doc.Objects[0]);
        Assert.Equal(Pen(c, new Vector2(2, 2)), doc.Objects[2]);
    }

    [Fact]
    public void Replace_with_unchanged_or_unknown_objects_is_a_no_op()
    {
        var doc = WithThree(out int a, out _, out _);
        int changes = 0;
        doc.Changed += () => changes++;
        doc.Replace([Pen(a, new Vector2(0, 0)), Pen(99, new Vector2(5, 5))]);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Replace_outside_an_applied_crop_grows_the_crop_and_the_bounds()
    {
        var doc = new EditorDocument(100, 80);
        int id = doc.NewId();
        doc.Add(Pen(id, new Vector2(50, 40)));
        doc.SetCrop(new PixelRect(10, 10, 60, 50));
        doc.Replace([Pen(id, new Vector2(120, 40))]);
        Assert.Equal(new PixelRect(0, 0, 122, 80), doc.Bounds);
        Assert.Equal(PixelRect.FromCorners(10, 10, 122, 60), doc.Crop);
    }

    [Theory]
    [InlineData(ZOrder.Up, new[] { 1 }, new[] { 2, 1, 3, 4 })]
    [InlineData(ZOrder.Up, new[] { 4 }, new[] { 1, 2, 3, 4 })]
    [InlineData(ZOrder.Up, new[] { 1, 3 }, new[] { 2, 1, 4, 3 })]
    [InlineData(ZOrder.Up, new[] { 3, 4 }, new[] { 1, 2, 3, 4 })]
    [InlineData(ZOrder.Down, new[] { 4 }, new[] { 1, 2, 4, 3 })]
    [InlineData(ZOrder.Down, new[] { 2, 4 }, new[] { 2, 1, 4, 3 })]
    [InlineData(ZOrder.Down, new[] { 1, 2 }, new[] { 1, 2, 3, 4 })]
    [InlineData(ZOrder.Front, new[] { 1, 3 }, new[] { 2, 4, 1, 3 })]
    [InlineData(ZOrder.Back, new[] { 2, 4 }, new[] { 2, 4, 1, 3 })]
    public void Reorder_moves_the_listed_objects_keeping_their_relative_order(ZOrder move, int[] ids, int[] expected)
    {
        var doc = new EditorDocument(100, 80);
        for (int i = 1; i <= 4; i++)
            doc.Add(Pen(doc.NewId(), new Vector2(i, i)));
        int before = doc.Objects.Count;
        doc.Reorder(ids, move);
        Assert.Equal(expected, Ids(doc));
        Assert.Equal(before, doc.Objects.Count);
    }

    [Fact]
    public void Reorder_is_one_undo_step_and_a_no_op_when_nothing_moves()
    {
        var doc = WithThree(out int a, out _, out int c);
        int changes = 0;
        doc.Changed += () => changes++;
        doc.Reorder([c], ZOrder.Up);
        doc.Reorder([a], ZOrder.Back);
        Assert.Equal(0, changes);
        doc.Reorder([a], ZOrder.Front);
        Assert.Equal(1, changes);
        doc.Undo();
        Assert.Equal(a, doc.Objects[0].Id);
    }
}
