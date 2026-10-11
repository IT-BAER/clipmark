using Clipmark.Core.Capture;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class EditorCropMathTests
{
    private static readonly PixelRect Rect = new(10, 10, 100, 100);
    private static readonly PixelRect Bounds = new(0, 0, 200, 150);

    [Fact]
    public void Bottom_right_corner_moves_right_and_bottom_edges()
    {
        Assert.Equal(new PixelRect(10, 10, 140, 110),
            CropMath.Resize(Rect, CropHandle.BottomRight, new PixelPoint(150, 120), Bounds));
    }

    [Fact]
    public void Top_left_corner_moves_left_and_top_edges()
    {
        Assert.Equal(new PixelRect(20, 30, 90, 80),
            CropMath.Resize(Rect, CropHandle.TopLeft, new PixelPoint(20, 30), Bounds));
    }

    [Fact]
    public void Top_right_and_bottom_left_corners_move_their_two_edges()
    {
        Assert.Equal(new PixelRect(10, 40, 120, 70),
            CropMath.Resize(Rect, CropHandle.TopRight, new PixelPoint(130, 40), Bounds));
        Assert.Equal(new PixelRect(5, 10, 105, 90),
            CropMath.Resize(Rect, CropHandle.BottomLeft, new PixelPoint(5, 100), Bounds));
    }

    [Fact]
    public void Edge_handle_changes_only_its_own_axis()
    {
        Assert.Equal(new PixelRect(30, 10, 80, 100),
            CropMath.Resize(Rect, CropHandle.Left, new PixelPoint(30, 999), Bounds));
        Assert.Equal(new PixelRect(10, 10, 100, 60),
            CropMath.Resize(Rect, CropHandle.Bottom, new PixelPoint(999, 70), Bounds));
    }

    [Fact]
    public void Edges_clamp_to_the_bounds()
    {
        Assert.Equal(new PixelRect(0, 10, 110, 100),
            CropMath.Resize(Rect, CropHandle.Left, new PixelPoint(-5, 0), Bounds));
        Assert.Equal(new PixelRect(10, 10, 190, 100),
            CropMath.Resize(Rect, CropHandle.Right, new PixelPoint(500, 0), Bounds));
        Assert.Equal(new PixelRect(10, 0, 100, 110),
            CropMath.Resize(Rect, CropHandle.Top, new PixelPoint(0, -40), Bounds));
        Assert.Equal(new PixelRect(10, 10, 100, 140),
            CropMath.Resize(Rect, CropHandle.Bottom, new PixelPoint(0, 500), Bounds));
    }

    [Fact]
    public void Edge_stops_at_the_opposite_edge_minus_min_size()
    {
        Assert.Equal(new PixelRect(10, 10, 1, 100),
            CropMath.Resize(Rect, CropHandle.Right, new PixelPoint(0, 0), Bounds));
        Assert.Equal(new PixelRect(10, 109, 100, 1),
            CropMath.Resize(Rect, CropHandle.Top, new PixelPoint(0, 200), Bounds));
        Assert.Equal(new PixelRect(10, 10, 20, 100),
            CropMath.Resize(Rect, CropHandle.Right, new PixelPoint(0, 0), Bounds, minSize: 20));
        Assert.Equal(new PixelRect(90, 10, 20, 100),
            CropMath.Resize(Rect, CropHandle.Left, new PixelPoint(500, 0), Bounds, minSize: 20));
    }

    [Theory]
    [InlineData(12, 12, CropHandle.TopLeft)]
    [InlineData(108, 13, CropHandle.TopRight)]
    [InlineData(11, 109, CropHandle.BottomLeft)]
    [InlineData(113, 107, CropHandle.BottomRight)]
    [InlineData(60, 7, CropHandle.Top)]
    [InlineData(60, 112, CropHandle.Bottom)]
    [InlineData(8, 60, CropHandle.Left)]
    [InlineData(111, 30, CropHandle.Right)]
    public void HitHandle_finds_corners_first_then_anywhere_along_an_edge(float x, float y, CropHandle expected)
    {
        Assert.Equal(expected, CropMath.HitHandle(Rect, new System.Numerics.Vector2(x, y), 5));
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(60, 3)]
    [InlineData(200, 60)]
    [InlineData(60, 116)]
    public void HitHandle_misses_inside_and_beyond_the_tolerance(float x, float y)
    {
        Assert.Null(CropMath.HitHandle(Rect, new System.Numerics.Vector2(x, y), 5));
    }
}
