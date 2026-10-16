using Clipmark.Core.Recording;

namespace Clipmark.Core.Tests;

public class GifEncoderTests
{
    private static byte[] Solid(int w, int h, Func<int, int, (byte R, byte G, byte B)> color)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (r, g, b) = color(x, y);
                int o = (y * w + x) * 4;
                px[o] = b; px[o + 1] = g; px[o + 2] = r; px[o + 3] = 255;
            }
        return px;
    }

    private static GifReader.Gif Encode(int w, int h, params (byte[] Bgra, int Delay)[] frames)
    {
        using var ms = new MemoryStream();
        using (var enc = new GifEncoder(ms, w, h))
        {
            foreach (var (px, delay) in frames) enc.AddFrame(px, delay);
        }
        return GifReader.Read(ms.ToArray());
    }

    [Fact]
    public void A_frame_with_few_colors_round_trips_exactly_and_loops_forever()
    {
        var px = Solid(40, 30, (x, y) => x < 20 ? ((byte)255, (byte)0, (byte)0) : y < 15 ? ((byte)0, (byte)128, (byte)255) : ((byte)17, (byte)17, (byte)17));
        var gif = Encode(40, 30, (px, 8));
        Assert.Equal((40, 30), (gif.Width, gif.Height));
        Assert.Equal(0, gif.LoopCount);
        var frame = Assert.Single(gif.Frames);
        Assert.Equal(8, frame.DelayCs);
        Assert.Equal(px, frame.Canvas);
    }

    [Fact]
    public void An_unchanged_frame_extends_the_previous_delay_and_a_change_stores_only_its_rectangle()
    {
        var a = Solid(32, 24, (_, _) => (10, 20, 30));
        var b = Solid(32, 24, (x, y) => x is >= 5 and <= 9 && y is >= 3 and <= 4 ? ((byte)200, (byte)0, (byte)0) : ((byte)10, (byte)20, (byte)30));
        var gif = Encode(32, 24, (a, 8), (a, 9), (b, 8));
        Assert.Equal([17, 8], gif.Frames.Select(f => f.DelayCs));
        Assert.Equal(a, gif.Frames[0].Canvas);
        Assert.Equal(b, gif.Frames[1].Canvas);
        Assert.Equal((5, 3, 5, 2), (gif.Frames[1].X, gif.Frames[1].Y, gif.Frames[1].Width, gif.Frames[1].Height));
    }

    [Fact]
    public void Differences_within_the_tolerance_are_not_written()
    {
        var a = Solid(16, 16, (x, _) => ((byte)(x * 8), 100, 100));
        var noisy = Solid(16, 16, (x, y) => ((byte)(x * 8 + (x + y) % 3), 100, 98));
        var gif = Encode(16, 16, (a, 4), (noisy, 4));
        var frame = Assert.Single(gif.Frames);
        Assert.Equal(8, frame.DelayCs);
        Assert.Equal(a, frame.Canvas);
    }

    [Fact]
    public void Many_colors_are_reduced_to_one_table_of_at_most_256_entries_with_small_error()
    {
        var px = Solid(128, 96, (x, y) => ((byte)(x * 2), (byte)(y * 2 + 30), (byte)((x + y) % 256)));
        var frame = Assert.Single(Encode(128, 96, (px, 5)).Frames);
        Assert.InRange(frame.ColorTableSize, 2, 256);
        double error = 0;
        for (int i = 0; i < px.Length; i++) if (i % 4 != 3) error += Math.Abs(px[i] - frame.Canvas[i]);
        Assert.InRange(error / (px.Length * 3 / 4), 0, 4);
    }

    [Fact]
    public void A_large_noisy_frame_round_trips_through_lzw_table_resets()
    {
        var random = new Random(7);
        var colors = Enumerable.Range(0, 200).Select(_ => ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256))).ToArray();
        var px = Solid(300, 200, (_, _) => colors[random.Next(colors.Length)]);
        var frame = Assert.Single(Encode(300, 200, (px, 3)).Frames);
        Assert.Equal(px, frame.Canvas);
    }
}
