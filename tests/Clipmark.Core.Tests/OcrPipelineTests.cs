using Clipmark.Core.TextActions;
using Clipmark.Core.TextActions.Ocr;

namespace Clipmark.Core.Tests;

public sealed class OcrPipelineTests
{
    // Expected values are cv2.resize(..., INTER_LINEAR) outputs on the same uint8 inputs.
    [Fact]
    public void Resize_upscales_2x2_to_4x4_like_OpenCV()
    {
        var dst = OcrImage.Resize([0, 100, 200, 255], 2, 2, 1, 4, 4);
        Assert.Equal(new byte[] { 0, 25, 75, 100, 50, 72, 117, 139, 150, 167, 200, 216, 200, 214, 241, 255 }, dst);
    }

    [Fact]
    public void Resize_downscales_4x4_to_2x2_like_OpenCV()
    {
        var src = Enumerable.Range(0, 16).Select(i => (byte)(i * 16)).ToArray();
        Assert.Equal(new byte[] { 40, 72, 168, 200 }, OcrImage.Resize(src, 4, 4, 1, 2, 2));
    }

    [Fact]
    public void Resize_handles_non_integer_scales_like_OpenCV()
    {
        var dst = OcrImage.Resize([10, 20, 30, 40, 50, 60], 3, 2, 1, 5, 3);
        Assert.Equal(new byte[] { 10, 14, 20, 26, 30, 25, 29, 35, 41, 45, 40, 44, 50, 56, 60 }, dst);
    }

    [Fact]
    public void Crop_rotates_counter_clockwise_like_rot90()
    {
        // 3x2 image, pixel value = index; rot90 gives rows [2 5], [1 4], [0 3].
        var bgra = Enumerable.Range(0, 6).SelectMany(i => new byte[] { (byte)i, 0, 0, 255 }).ToArray();
        var rotated = OcrImage.Crop(bgra, 3, 0, 0, 3, 2, rotate: true);
        Assert.Equal(new byte[] { 2, 5, 1, 4, 0, 3 }, rotated.Where((_, i) => i % 4 == 0).ToArray());
    }

    [Fact]
    public void Dilation_spreads_a_pixel_right_and_down()
    {
        var prob = new float[25];
        prob[2 * 5 + 2] = 1;
        var mask = TextDetection.Mask(prob, 5, 5, 0.3f, dilate: true);
        var set = Enumerable.Range(0, 25).Where(i => mask[i]).Select(i => (i % 5, i / 5));
        Assert.Equal([(2, 2), (3, 2), (2, 3), (3, 3)], set);
    }

    [Fact]
    public void Components_and_boxes_match_the_reference_numbers()
    {
        var prob = new float[20 * 10];
        void Fill(int x0, int y0, int x1, int y1, float v)
        {
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) prob[y * 20 + x] = v;
        }
        Fill(2, 3, 12, 7, 0.9f);   // text block
        Fill(16, 0, 18, 2, 0.95f); // 3x3 after dilation, mean 0.42 < 0.5
        Fill(14, 8, 20, 10, 0.35f); // only 2 rows high
        var mask = TextDetection.Mask(prob, 20, 10, 0.3f, dilate: true);
        var components = TextDetection.Components(mask, 20, 10);
        Assert.Equal([new Component(16, 0, 3, 3), new Component(2, 3, 11, 5), new Component(14, 8, 6, 2)], components);

        // d = 11*5*1.6/(2*16) = 2.75; image is twice the map size and clamps the bottom edge.
        var boxes = TextDetection.Boxes(prob, 20, 10, components, 40, 20, 0.5f, 1.6);
        Assert.Equal([new TextBox(0, 0.5, 31.5, 20)], boxes);
    }

    [Fact]
    public void Input_size_snaps_to_multiples_of_32_within_the_side_limits()
    {
        Assert.Equal((1440, 736), TextDetection.InputSize(420, 214, 3200, 736));
        Assert.Equal((2560, 1440), TextDetection.InputSize(2560, 1440, 3200, 736));
        Assert.Equal((3200, 800), TextDetection.InputSize(6400, 1600, 3200, 736));
    }

    [Fact]
    public void Reading_order_rounds_rows_half_to_even()
    {
        TextBox[] boxes = [new(50, 15, 60, 20), new(10, 25, 20, 30), new(90, 14, 99, 20), new(200, 4, 210, 9)];
        Assert.Equal([200, 90, 10, 50], TextBox.InReadingOrder(boxes, b => b).Select(b => (int)b.X0));
    }

    private static readonly string[] Labels = ["", "a", "b", " "];

    private static float[] Steps(params int[] ids)
    {
        var probs = new float[ids.Length * Labels.Length];
        for (int t = 0; t < ids.Length; t++)
        {
            for (int c = 0; c < Labels.Length; c++) probs[t * Labels.Length + c] = 0.05f;
            probs[t * Labels.Length + ids[t]] = 0.8f + t * 0.01f;
        }
        return probs;
    }

    [Fact]
    public void Ctc_collapses_repeats_drops_blanks_and_keeps_spaces()
    {
        var result = CtcDecoder.Decode(Steps(1, 1, 0, 1, 2, 2, 3, 1), 8, 4, Labels);
        Assert.Equal("aab a", result.Text);
        Assert.Equal([0, 3, 4, 6, 7], result.Chars.Select(c => c.Step));
        Assert.Equal((0.80 + 0.83 + 0.84 + 0.86 + 0.87) / 5, result.Confidence, 5);
    }

    [Fact]
    public void Ctc_of_only_blanks_is_empty_with_zero_confidence()
    {
        var result = CtcDecoder.Decode(Steps(0, 0, 0), 3, 4, Labels);
        Assert.Equal("", result.Text);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public void Word_boxes_split_the_line_box_at_the_space_centers()
    {
        var chars = CtcDecoder.Decode(Steps(1, 1, 0, 1, 2, 2, 3, 1), 8, 4, Labels).Chars;
        var words = CtcDecoder.Words(chars, 2.5, new TextBox(100, 10, 200, 30));
        Assert.Equal(["aab", "a"], words.Select(w => w.Text));
        Assert.Equal((100f, 10f, 116.25f, 30f), (words[0].Box.Min.X, words[0].Box.Min.Y, words[0].Box.Max.X, words[0].Box.Max.Y));
        Assert.Equal((116.25f, 200f), (words[1].Box.Min.X, words[1].Box.Max.X));
    }

    [Fact]
    public void A_run_of_spaces_splits_at_its_center()
    {
        var chars = CtcDecoder.Decode(Steps(1, 3, 0, 3, 2), 5, 4, Labels).Chars;
        var words = CtcDecoder.Words(chars, 10, new TextBox(0, 0, 50, 10));
        Assert.Equal(["a", "b"], words.Select(w => w.Text));
        Assert.Equal((0f, 25f), (words[0].Box.Min.X, words[0].Box.Max.X));
        Assert.Equal((25f, 50f), (words[1].Box.Min.X, words[1].Box.Max.X));
    }

    [Theory]
    [InlineData("abc", 0.6, true)]
    [InlineData("abc", 0.4, false)]
    [InlineData("  ", 0.99, false)]
    [InlineData("X", 0.85, false)]
    [InlineData(" la ", 0.89, false)]
    [InlineData("X", 0.95, true)]
    public void Keep_drops_low_confidence_blank_and_unsure_short_lines(string text, double confidence, bool kept)
    {
        Assert.Equal(kept, TextLines.Keep(text, confidence));
    }

    private static TextLine Line(string text, double x0, double y0, double x1, double y1) =>
        TextLine.Rotated(text, new TextBox(x0, y0, x1, y1));

    [Fact]
    public void Merge_joins_a_close_line_on_the_same_row()
    {
        var merged = TextLines.Merge([Line("B", 60, 2, 100, 22), Line("A", 0, 0, 50, 20)], 1.0);
        var line = Assert.Single(merged);
        Assert.Equal("A B", line.Text);
        Assert.Equal(new TextBox(0, 0, 100, 22), line.Box);
        Assert.Equal(["A", "B"], line.Words.Select(w => w.Text));
    }

    [Theory]
    [InlineData(75, 2, 100, 22)]  // gap 25 > 1.0 * height
    [InlineData(45, 2, 100, 22)]  // overlaps horizontally
    [InlineData(60, 2, 100, 29)]  // height 27 >= 1.3 * 20
    [InlineData(60, 13, 100, 33)] // vertical overlap 7 < 0.6 * 20
    public void Merge_keeps_lines_apart(double x0, double y0, double x1, double y1)
    {
        var merged = TextLines.Merge([Line("A", 0, 0, 50, 20), Line("B", x0, y0, x1, y1)], 1.0);
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void Engine_finds_no_lines_in_a_solid_image()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Clipmark.slnx")))
            dir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dir)) ?? throw new DirectoryNotFoundException("repo root");
        var models = Path.Combine(dir, "src", "Clipmark.App", "Assets", "Ocr");
        using var engine = new OcrEngine(Path.Combine(models, "text-det.onnx"), Path.Combine(models, "text-rec.onnx"));
        var bgra = Enumerable.Repeat(new byte[] { 200, 120, 40, 255 }, 300 * 200).SelectMany(p => p).ToArray();
        Assert.Empty(engine.Recognize(bgra, 300, 200));
    }
}
