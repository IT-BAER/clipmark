using System.Numerics;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class RulerTests
{
    private static Ruler At(float angle) => new(new(200, 150), angle, 48);

    private static Vector2 Axis(float angle) => new(MathF.Cos(angle * MathF.PI / 180), MathF.Sin(angle * MathF.PI / 180));

    private static Vector2 Normal(float angle) => new(-Axis(angle).Y, Axis(angle).X);

    private static float Across(RulerEdge edge, Vector2 p) => Vector2.Dot(p - edge.Point, new(-edge.Direction.Y, edge.Direction.X));

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(345)]
    public void Contains_covers_the_band_along_its_axis(float angle)
    {
        var r = At(angle);
        Assert.True(r.Contains(r.Center + Axis(angle) * 299 + Normal(angle) * 47, 300));
        Assert.True(r.Contains(r.Center - Axis(angle) * 299 - Normal(angle) * 47, 300));
        Assert.False(r.Contains(r.Center + Axis(angle) * 301, 300));
        Assert.False(r.Contains(r.Center + Normal(angle) * 49, 300));
        Assert.False(r.Contains(r.Center - Normal(angle) * 49, 300));
    }

    [Theory]
    [InlineData(0, 1, 15)]
    [InlineData(345, 1, 0)]
    [InlineData(0, -1, 345)]
    [InlineData(0, 3, 45)]
    [InlineData(10, -1, 355)]
    [InlineData(0, 24, 0)]
    [InlineData(0, -25, 345)]
    public void RotateBy_steps_by_15_degrees_and_wraps(float start, int steps, float expected)
    {
        var turned = At(start).RotateBy(steps);
        Assert.Equal(expected, turned.Angle);
        Assert.Equal(At(start).Center, turned.Center);
        Assert.Equal(48, turned.HalfHeight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(345)]
    public void EdgeNear_finds_the_long_edge_within_tolerance(float angle)
    {
        var r = At(angle);
        foreach (float side in new[] { 1f, -1f })
        {
            var onEdge = r.Center + Normal(angle) * (side * 48) + Axis(angle) * 30;
            var near = r.Center + Normal(angle) * (side * 60) - Axis(angle) * 80;
            var far = r.Center + Normal(angle) * (side * 70);
            Assert.InRange(Math.Abs(Across(r.EdgeNear(onEdge, 16)!.Value, onEdge)), 0, 0.01);
            var edge = r.EdgeNear(near, 16)!.Value;
            Assert.InRange(Math.Abs(Across(edge, near)), 11.99, 12.01);
            Assert.Equal(Axis(angle).X, edge.Direction.X, 1e-4);
            Assert.Equal(Axis(angle).Y, edge.Direction.Y, 1e-4);
            Assert.Null(r.EdgeNear(far, 16));
        }
        Assert.Null(r.EdgeNear(r.Center, 16));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(345)]
    public void Project_keeps_order_and_lies_on_the_edge(float angle)
    {
        var r = At(angle);
        var edge = r.EdgeNear(r.Center + Normal(angle) * 55, 16)!.Value;
        Vector2[] points = [new(10, 20), new(300, 40), new(120, 260), new(205, 151)];
        var projected = Ruler.Project(points, edge);

        Assert.Equal(points.Length, projected.Length);
        for (int i = 0; i < points.Length; i++)
        {
            Assert.InRange(Math.Abs(Across(edge, projected[i])), 0, 0.01);
            Assert.Equal(Vector2.Dot(points[i] - edge.Point, edge.Direction), Vector2.Dot(projected[i] - edge.Point, edge.Direction), 0.01);
        }
    }
}
