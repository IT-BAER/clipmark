using System.Numerics;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class QuickMarkupTests
{
    private static PixelPoint[] Line(params (int X, int Y)[] points) => [.. points.Select(p => new PixelPoint(p.X, p.Y))];

    [Fact]
    public void Strokes_are_red_pens_of_the_default_width_with_pixel_centers_relative_to_the_cut()
    {
        var strokes = QuickMarkup.ToStrokes([Line((110, 220), (130, 240))], new PixelPoint(100, 200), 50, 50);
        var s = Assert.IsType<Stroke>(Assert.Single(strokes));
        Assert.Equal(StrokeKind.Pen, s.Kind);
        Assert.Equal(new Rgba(255, 0, 0, 255), s.Color);
        Assert.Equal(QuickMarkup.PenWidth, s.Width);
        Assert.Equal([new Vector2(10.5f, 20.5f), new Vector2(30.5f, 40.5f)], s.Points.ToArray());
    }

    [Fact]
    public void A_stroke_with_no_point_inside_the_image_is_dropped_and_ids_count_from_one()
    {
        var strokes = QuickMarkup.ToStrokes([Line((0, 0), (5, 5)), Line((20, 20), (25, 25)), Line((-9, -9), (15, 15))], new PixelPoint(10, 10), 30, 30);
        Assert.Equal([1, 2], strokes.Select(s => s.Id));
        Assert.Equal(new Vector2(10.5f, 10.5f), strokes[0].Points[0]);
        Assert.Equal(new Vector2(-18.5f, -18.5f), strokes[1].Points[0]);
    }

    [Fact]
    public void A_stroke_touching_the_image_keeps_all_its_points()
    {
        var strokes = QuickMarkup.ToStrokes([Line((-20, 5), (3, 5), (80, 5))], new PixelPoint(0, 0), 10, 10);
        Assert.Equal(3, Assert.Single(strokes).Points.Length);
    }

    [Fact]
    public void The_document_holds_the_strokes_and_crops_to_the_image_when_a_stroke_reaches_past_it()
    {
        var inside = QuickMarkup.Document(20, 20, QuickMarkup.ToStrokes([Line((5, 5), (8, 8))], new PixelPoint(0, 0), 20, 20));
        Assert.Single(inside.Objects);
        Assert.Null(inside.Crop);

        var over = QuickMarkup.Document(20, 20, QuickMarkup.ToStrokes([Line((5, 5), (60, 8))], new PixelPoint(0, 0), 20, 20));
        Assert.Equal(new PixelRect(0, 0, 20, 20), over.Crop);
        Assert.True(over.Bounds.Width > 20);
        Assert.Equal(new[] { 1 }, over.Objects.Select(o => o.Id));
    }

    [Fact]
    public void The_border_offsets_the_points_but_not_the_inside_test()
    {
        var strokes = QuickMarkup.ToStrokes([Line((5, 5)), Line((10, 5))], new PixelPoint(0, 0), 10, 10, border: 3);
        Assert.Equal(new Vector2(8.5f, 8.5f), Assert.Single(strokes).Points[0]);
    }
}
