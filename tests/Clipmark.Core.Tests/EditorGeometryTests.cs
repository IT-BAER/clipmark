using System.Numerics;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class EditorGeometryTests
{
    private static void AssertNear(Vector2 expected, Vector2 actual, float eps = 1e-3f)
    {
        Assert.InRange(actual.X, expected.X - eps, expected.X + eps);
        Assert.InRange(actual.Y, expected.Y - eps, expected.Y + eps);
    }

    [Theory]
    [InlineData(10, 1, 10.0499, 0)] // 5.7 deg -> 0, length sqrt(101)
    [InlineData(3, 3, 3, 3)] // exactly 45
    [InlineData(0, 5, 0, 5)] // exactly 90
    [InlineData(10, 9, 9.5131, 9.5131)] // 42 deg -> 45, length sqrt(181) / sqrt(2)
    [InlineData(-10, 1, -10.0499, 0)] // left, 174.3 deg -> 180
    public void SnapAngle_snaps_to_nearest_45_degrees_keeping_length(float x, float y, float ex, float ey)
    {
        AssertNear(new Vector2(ex, ey), EditorGeometry.SnapAngle(Vector2.Zero, new Vector2(x, y)));
    }

    [Fact]
    public void SnapAngle_is_relative_to_start()
    {
        AssertNear(new Vector2(1, 6), EditorGeometry.SnapAngle(new Vector2(1, 1), new Vector2(1, 6)));
    }

    [Fact]
    public void SnapAngle_with_equal_points_returns_end()
    {
        Assert.Equal(new Vector2(4, 4), EditorGeometry.SnapAngle(new Vector2(4, 4), new Vector2(4, 4)));
    }

    [Fact]
    public void ArrowHead_wings_are_symmetric_around_the_shaft_and_end_L_behind_the_tip()
    {
        // width 2 -> L = max(12, 8) = 12; wing = tip + 12 * (-cos30, -+sin30)
        var (left, right) = EditorGeometry.ArrowHead(Vector2.Zero, new Vector2(100, 0), 2);
        AssertNear(new Vector2(89.6077f, -6), left);
        AssertNear(new Vector2(89.6077f, 6), right);
    }

    [Fact]
    public void ArrowHead_length_grows_with_width()
    {
        // width 5 -> L = 20
        var (left, right) = EditorGeometry.ArrowHead(Vector2.Zero, new Vector2(100, 0), 5);
        AssertNear(new Vector2(82.6795f, -10), left);
        AssertNear(new Vector2(82.6795f, 10), right);
    }

    [Fact]
    public void ArrowHead_is_capped_at_the_shaft_length()
    {
        var (left, right) = EditorGeometry.ArrowHead(Vector2.Zero, new Vector2(5, 0), 2);
        AssertNear(new Vector2(0.6699f, -2.5f), left);
        AssertNear(new Vector2(0.6699f, 2.5f), right);
    }

    [Fact]
    public void ArrowHead_follows_the_shaft_direction()
    {
        // shaft pointing down (+y): back is -y; left of heading is +x in y-down space
        var (left, right) = EditorGeometry.ArrowHead(Vector2.Zero, new Vector2(0, 100), 2);
        AssertNear(new Vector2(6, 89.6077f), left);
        AssertNear(new Vector2(-6, 89.6077f), right);
    }

    [Fact]
    public void Thin_drops_close_points_and_keeps_the_last_input()
    {
        var thinned = EditorGeometry.Thin(
            [new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(3.5f, 0)], 2);
        Assert.Equal(new Vector2[] { new(0, 0), new(2, 0), new(3.5f, 0) }, thinned);
    }

    [Fact]
    public void Thin_does_not_duplicate_the_last_point_when_already_kept()
    {
        var thinned = EditorGeometry.Thin([new(0, 0), new(5, 0)], 2);
        Assert.Equal(new Vector2[] { new(0, 0), new(5, 0) }, thinned);
    }

    [Fact]
    public void Thin_of_empty_input_is_empty()
    {
        Assert.Empty(EditorGeometry.Thin([], 2));
    }

    [Theory]
    [InlineData(5, 3, 3)] // beside the segment
    [InlineData(-3, 4, 5)] // before a: distance to a
    [InlineData(13, 4, 5)] // past b: distance to b
    [InlineData(4, 0, 0)] // on the segment
    public void DistanceToSegment_measures_to_the_nearest_point_of_the_segment(float x, float y, float expected)
    {
        Assert.Equal(expected, EditorGeometry.DistanceToSegment(new Vector2(x, y), new Vector2(0, 0), new Vector2(10, 0)), 4);
    }

    [Fact]
    public void DistanceToSegment_of_a_degenerate_segment_is_the_point_distance()
    {
        Assert.Equal(5f, EditorGeometry.DistanceToSegment(new Vector2(3, 4), Vector2.Zero, Vector2.Zero), 4);
    }
}
