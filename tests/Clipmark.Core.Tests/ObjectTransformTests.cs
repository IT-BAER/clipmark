using System.Numerics;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class ObjectTransformTests
{
    private static readonly Rgba Red = new(255, 0, 0, 255);
    private const float Quarter = MathF.PI / 2;

    private static Shape Rect(float x1, float y1, float x2, float y2, float rotation = 0, float width = 2) =>
        new(1, ShapeKind.Rectangle, Red, width, new(x1, y1), new(x2, y2)) { Rotation = rotation };

    private static void Near(Vector2 expected, Vector2 actual, float eps = 1e-3f) =>
        Assert.True(Vector2.Distance(expected, actual) <= eps, $"expected {expected}, got {actual}");

    private static void Near(FloatRect expected, FloatRect actual)
    {
        Near(expected.Min, actual.Min);
        Near(expected.Max, actual.Max);
    }

    [Fact]
    public void Rotation_takes_part_in_equality_of_strokes_and_shapes()
    {
        var stroke = new Stroke(1, StrokeKind.Pen, Red, 4, [new(0, 0), new(10, 0)]);
        Assert.NotEqual(stroke, stroke with { Rotation = 1 });
        Assert.Equal(stroke with { Rotation = 1 }, stroke with { Points = [new(0, 0), new(10, 0)], Rotation = 1 });
        Assert.Equal((stroke with { Rotation = 1 }).GetHashCode(), (stroke with { Rotation = 1 }).GetHashCode());
        var rect = Rect(0, 0, 10, 10);
        Assert.NotEqual(rect, rect with { Rotation = 1 });
        Assert.Equal(rect with { Rotation = 1 }, Rect(0, 0, 10, 10, 1));
    }

    [Fact]
    public void LocalBox_and_world_mapping_rotate_about_the_box_center()
    {
        var rect = Rect(40, 20, 0, 0, Quarter);
        Assert.Equal(new FloatRect(new(0, 0), new(40, 20)), EditorGeometry.LocalBox(rect));
        Near(new(20, -10), EditorGeometry.ToWorld(rect, new(0, 10)));
        Near(new(0, 10), EditorGeometry.ToLocal(rect, new(20, -10)));
        var stroke = new Stroke(1, StrokeKind.Pen, Red, 4, [new(5, 7), new(-3, 9), new(1, 2)]);
        Assert.Equal(new FloatRect(new(-3, 2), new(5, 9)), EditorGeometry.LocalBox(stroke));
    }

    [Fact]
    public void Extent_of_a_rotated_rectangle_uses_rotated_corners_and_miter_growth()
    {
        float g = MathF.Sqrt(2);
        Near(new FloatRect(new(25 - g, -25 - g), new(75 + g, 75 + g)), EditorGeometry.Extent(Rect(0, 0, 100, 50, Quarter)));
    }

    [Fact]
    public void Extent_of_a_rotated_stroke_uses_rotated_points_and_half_the_width()
    {
        var stroke = new Stroke(1, StrokeKind.Pen, Red, 4, [new(0, 0), new(100, 0)]) { Rotation = Quarter };
        Near(new FloatRect(new(48, -52), new(52, 52)), EditorGeometry.Extent(stroke));
    }

    [Fact]
    public void Bounds_cover_the_corners_of_a_rotated_rectangle()
    {
        var doc = new EditorDocument(100, 80);
        doc.Add(Rect(0, 0, 100, 50, Quarter) with { Id = doc.NewId() });
        Assert.Equal(PixelRect.FromCorners(0, -27, 100, 80), doc.Bounds);
    }

    [Fact]
    public void HitTest_finds_a_rotated_rectangle_on_its_rotated_outline_only()
    {
        var doc = new EditorDocument(300, 300);
        doc.Add(Rect(100, 100, 200, 140, Quarter) with { Id = doc.NewId() });
        Assert.NotNull(doc.HitTest(new(130, 120), 2)); // rotated left side
        Assert.NotNull(doc.HitTest(new(150, 70), 2)); // rotated top side
        Assert.Null(doc.HitTest(new(150, 100), 2)); // unrotated top side
        Assert.Null(doc.HitTest(new(100, 120), 2)); // unrotated left side
    }

    [Fact]
    public void Rotate_moves_the_center_of_a_rectangle_about_the_pivot_and_adds_the_angle()
    {
        var r = (Shape)ObjectTransform.Rotate(Rect(0, 0, 20, 10, 0.25f), Vector2.Zero, Quarter);
        Near(new(-15, 5), r.Start);
        Near(new(5, 15), r.End);
        Assert.Equal(0.25f + Quarter, r.Rotation, 5);
    }

    [Fact]
    public void Rotate_turns_the_end_points_of_a_line_and_keeps_rotation_zero()
    {
        var line = new Shape(1, ShapeKind.Line, Red, 2, new(10, 0), new(20, 0));
        var r = (Shape)ObjectTransform.Rotate(line, Vector2.Zero, Quarter);
        Near(new(0, 10), r.Start);
        Near(new(0, 20), r.End);
        Assert.Equal(0, r.Rotation);
    }

    [Theory]
    [InlineData(CropHandle.Left, 0, 99, 0, 10, 50, 30)]
    [InlineData(CropHandle.Top, 99, 0, 10, 0, 50, 30)]
    [InlineData(CropHandle.Right, 60, 99, 10, 10, 60, 30)]
    [InlineData(CropHandle.Bottom, 99, 40, 10, 10, 50, 40)]
    [InlineData(CropHandle.TopLeft, 0, 0, 0, 0, 50, 30)]
    [InlineData(CropHandle.TopRight, 60, 0, 10, 0, 60, 30)]
    [InlineData(CropHandle.BottomLeft, 0, 40, 0, 10, 50, 40)]
    [InlineData(CropHandle.BottomRight, 60, 40, 10, 10, 60, 40)]
    public void Resize_moves_the_handle_edges_of_an_unrotated_rectangle(CropHandle handle, float px, float py,
        float x1, float y1, float x2, float y2)
    {
        var r = ObjectTransform.Resize(Rect(10, 10, 50, 30), handle, new(px, py), keepAspect: false);
        Assert.Equal(new FloatRect(new(x1, y1), new(x2, y2)), EditorGeometry.LocalBox(r));
        Assert.Equal(2, r.Width);
    }

    [Fact]
    public void Resize_keeps_the_start_end_orientation_of_a_shape()
    {
        var r = (Shape)ObjectTransform.Resize(Rect(50, 30, 10, 10), CropHandle.BottomRight, new(60, 40), false);
        Assert.Equal(new Vector2(60, 40), r.Start);
        Assert.Equal(new Vector2(10, 10), r.End);
    }

    [Fact]
    public void Resize_of_a_rotated_rectangle_keeps_the_opposite_edge_fixed_in_world_space()
    {
        var rect = Rect(0, 0, 40, 20, Quarter);
        Near(new(20, -10), EditorGeometry.ToWorld(rect, new(0, 10))); // left edge middle = anchor
        var r = ObjectTransform.Resize(rect, CropHandle.Right, new(20, 50), false);
        var box = EditorGeometry.LocalBox(r);
        Assert.Equal(60, box.Max.X - box.Min.X, 3);
        Assert.Equal(20, box.Max.Y - box.Min.Y, 3);
        Assert.Equal(Quarter, r.Rotation);
        float cy = (box.Min.Y + box.Max.Y) / 2;
        Near(new(20, -10), EditorGeometry.ToWorld(r, new(box.Min.X, cy)));
        Near(new(20, 50), EditorGeometry.ToWorld(r, new(box.Max.X, cy)));
    }

    [Fact]
    public void Resize_with_keepAspect_uses_the_larger_relative_change()
    {
        var r = ObjectTransform.Resize(Rect(0, 0, 40, 20), CropHandle.BottomRight, new(60, 22), keepAspect: true);
        Assert.Equal(new FloatRect(new(0, 0), new(60, 30)), EditorGeometry.LocalBox(r));
        r = ObjectTransform.Resize(Rect(0, 0, 40, 20), CropHandle.TopLeft, new(38, -20), keepAspect: true);
        Assert.Equal(new FloatRect(new(-40, -20), new(40, 20)), EditorGeometry.LocalBox(r));
    }

    [Fact]
    public void Resize_clamps_at_the_minimum_size_without_flipping()
    {
        var r = ObjectTransform.Resize(Rect(0, 0, 40, 20), CropHandle.Right, new(-50, 10), false);
        Assert.Equal(new FloatRect(new(0, 0), new(1, 20)), EditorGeometry.LocalBox(r));
        r = ObjectTransform.Resize(Rect(0, 0, 40, 20), CropHandle.TopLeft, new(100, 100), false, minSize: 4);
        Assert.Equal(new FloatRect(new(36, 16), new(40, 20)), EditorGeometry.LocalBox(r));
    }

    [Fact]
    public void Resize_scales_stroke_points_and_keeps_a_degenerate_axis()
    {
        var stroke = new Stroke(1, StrokeKind.Pen, Red, 4, [new(0, 0), new(20, 10), new(40, 20)]);
        var r = (Stroke)ObjectTransform.Resize(stroke, CropHandle.Right, new(80, 5), false);
        Assert.Equal([new(0, 0), new(40, 10), new(80, 20)], r.Points.ToArray());
        Assert.Equal(4, r.Width);

        var flat = new Stroke(1, StrokeKind.Pen, Red, 4, [new(0, 10), new(40, 10)]);
        Assert.Equal(flat, ObjectTransform.Resize(flat, CropHandle.Bottom, new(20, 50), false));
        r = (Stroke)ObjectTransform.Resize(flat, CropHandle.BottomRight, new(80, 50), true);
        Assert.Equal([new(0, 10), new(80, 10)], r.Points.ToArray());
    }

    [Fact]
    public void ScaleUniform_scales_about_the_anchor_and_keeps_rotation_and_width()
    {
        var r = (Shape)ObjectTransform.ScaleUniform(Rect(10, 10, 30, 20), new(10, 10), 2);
        Assert.Equal(new Vector2(10, 10), r.Start);
        Assert.Equal(new Vector2(50, 30), r.End);
        Assert.Equal(2, r.Width);

        var rotated = ObjectTransform.ScaleUniform(Rect(10, 10, 30, 20, Quarter), new(10, 10), 2);
        var box = EditorGeometry.LocalBox(rotated);
        Near(new(30, 20), (box.Min + box.Max) / 2);
        Near(new(40, 20), box.Max - box.Min);
        Assert.Equal(Quarter, rotated.Rotation);

        var line = (Shape)ObjectTransform.ScaleUniform(new Shape(1, ShapeKind.Line, Red, 2, new(10, 10), new(20, 10)), Vector2.Zero, 3);
        Assert.Equal(new Vector2(30, 30), line.Start);
        Assert.Equal(new Vector2(60, 30), line.End);
    }
}
