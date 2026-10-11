using System.Numerics;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class FillTests
{
    private static readonly Rgba Red = new(255, 0, 0, 255);
    private static readonly Rgba Yellow = new(255, 230, 0, 255);

    // An ellipse 100..200 x 100..150 under a rectangle 180..280 x 120..200, and a pen stroke at y = 250.
    private static (EditorDocument Doc, int Ellipse, int Box) New()
    {
        var doc = new EditorDocument(400, 300);
        int ellipse = doc.NewId(), box = doc.NewId(), pen = doc.NewId();
        doc.Add(new Shape(ellipse, ShapeKind.Ellipse, Red, 2, new(100, 100), new(200, 150)));
        doc.Add(new Shape(box, ShapeKind.Rectangle, Red, 2, new(180, 120), new(280, 200)));
        doc.Add(new Stroke(pen, StrokeKind.Pen, Red, 4, [new(20, 250), new(380, 250)]));
        return (doc, ellipse, box);
    }

    private static Shape Get(EditorDocument doc, int id) => (Shape)doc.Objects.Single(o => o.Id == id);

    [Fact]
    public void Fill_inside_an_outlined_shape_fills_it_as_one_undo_step()
    {
        var (doc, ellipse, _) = New();
        doc.Fill(new(130, 125), Yellow, 2);
        Assert.Equal(Yellow, Get(doc, ellipse).Fill);
        Assert.Null(doc.Background);
        doc.Undo();
        Assert.Null(Get(doc, ellipse).Fill);
    }

    [Fact]
    public void The_topmost_shape_under_the_point_wins()
    {
        var (doc, ellipse, box) = New();
        doc.Fill(new(190, 125), Yellow, 2);
        Assert.Equal(Yellow, Get(doc, box).Fill);
        Assert.Null(Get(doc, ellipse).Fill);
    }

    [Fact]
    public void Ellipse_corners_and_empty_space_and_strokes_fill_the_background()
    {
        var (doc, ellipse, _) = New();
        doc.Fill(new(103, 103), Yellow, 2); // inside the ellipse's box, outside the ellipse
        Assert.Null(Get(doc, ellipse).Fill);
        Assert.Equal(Yellow, doc.Background);
        doc.Undo();
        Assert.Null(doc.Background);
        doc.Fill(new(200, 250), Yellow, 2);
        Assert.Equal(Yellow, doc.Background);
    }

    [Fact]
    public void Rotation_is_respected()
    {
        var doc = new EditorDocument(400, 300);
        int id = doc.NewId();
        // 100 x 20 bar centered at (200,150), turned upright.
        doc.Add(new Shape(id, ShapeKind.Rectangle, Red, 2, new(150, 140), new(250, 160)) { Rotation = MathF.PI / 2 });
        doc.Fill(new(240, 150), Yellow, 2);
        Assert.Null(Get(doc, id).Fill);
        doc.Fill(new(200, 110), Yellow, 2);
        Assert.Equal(Yellow, Get(doc, id).Fill);
    }

    [Fact]
    public void A_filled_shape_is_hit_inside_and_an_outlined_one_only_on_its_outline()
    {
        var (doc, ellipse, _) = New();
        Assert.Null(doc.HitTest(new(130, 125), 2));
        doc.Fill(new(130, 125), Yellow, 2);
        Assert.Equal(ellipse, doc.HitTest(new(130, 125), 2)?.Id);
    }

    [Fact]
    public void Same_fill_again_adds_no_undo_step()
    {
        var (doc, _, _) = New();
        doc.Fill(new(130, 125), Yellow, 2);
        doc.Fill(new(10, 10), Yellow, 2);
        doc.Undo();
        doc.Undo();
        Assert.True(doc.CanUndo); // the three Adds remain
        doc.Fill(new(130, 125), Yellow, 2);
        doc.Fill(new(130, 125), Yellow, 2);
        doc.Undo();
        Assert.Null(doc.Background);
        Assert.All(doc.Objects.OfType<Shape>(), s => Assert.Null(s.Fill));
    }
}
