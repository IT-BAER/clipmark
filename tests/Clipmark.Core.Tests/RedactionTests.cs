using Clipmark.Core.Capture;
using Clipmark.Core.Editor;
using Clipmark.Core.TextActions;

namespace Clipmark.Core.Tests;

public class RedactionTests
{
    [Fact]
    public void Redact_adds_boxes_clipped_to_the_image_as_one_undo_step()
    {
        var doc = new EditorDocument(100, 50);
        doc.Redact([new PixelRect(10, 10, 20, 5), new PixelRect(90, 40, 30, 30)]);
        Assert.Equal([new PixelRect(10, 10, 20, 5), new PixelRect(90, 40, 10, 10)], doc.Redactions);
        Assert.True(doc.IsDirty);
        doc.Undo();
        Assert.Empty(doc.Redactions);
    }

    [Fact]
    public void Redact_outside_the_image_or_with_nothing_adds_no_step()
    {
        var doc = new EditorDocument(100, 50);
        doc.Redact([]);
        doc.Redact([new PixelRect(200, 200, 5, 5)]);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void Fill_paints_only_the_clipped_rect()
    {
        // 4 x 3 image, all pixels (1, 2, 3, 4).
        var bgra = Enumerable.Repeat(new byte[] { 1, 2, 3, 4 }, 12).SelectMany(p => p).ToArray();
        Pixels.Fill(bgra, 4, new PixelRect(2, 1, 5, 1), new Rgba(10, 20, 30, 255));
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++)
            {
                int i = (y * 4 + x) * 4;
                byte[] expected = y == 1 && x >= 2 ? [30, 20, 10, 255] : [1, 2, 3, 4];
                Assert.Equal(expected, bgra[i..(i + 4)]);
            }
    }

    [Fact]
    public void Shrink_averages_each_block_and_keeps_partial_blocks_at_the_edge()
    {
        // 3 x 2 image, factor 2: blocks (0..1, 0..1) and the partial column 2.
        byte[] bgra =
        [
            0, 0, 0, 255, 100, 100, 100, 255, 7, 8, 9, 255,
            200, 200, 200, 255, 100, 100, 100, 255, 9, 10, 11, 255,
        ];
        var (small, w, h) = Pixels.Shrink(bgra, 3, 2, 2);
        Assert.Equal((2, 1), (w, h));
        Assert.Equal([100, 100, 100, 255, 8, 9, 10, 255], small);
    }

    private static OcrLine Line(params string[] words) =>
        new([.. words.Select((w, i) => new OcrWord(w, new(new(i * 100, 0), new(i * 100 + 80, 20))))]);

    private static PixelRect Words(int first, int last) =>
        PixelRect.FromCorners(first * 100 - 2, -2, last * 100 + 80 + 2, 22);

    [Fact]
    public void An_email_covers_its_word()
    {
        Assert.Equal([Words(1, 1)], OcrText.Sensitive([Line("Mail:", "test.user+tag@example.com,", "danke")], 2));
    }

    [Theory]
    [InlineData("+43", "660", "1234567")]
    [InlineData("(0732)", "12", "34", "56")]
    [InlineData("555-123-4567")]
    [InlineData("+49/89/1234-567")]
    public void A_phone_number_covers_all_its_words_but_not_the_label(params string[] number)
    {
        Assert.Equal([Words(1, number.Length)], OcrText.Sensitive([Line(["Tel.", .. number, "abends"])], 2));
    }

    [Theory]
    [InlineData("11.10.2026")]
    [InlineData("10.10.20.35")]
    [InlineData("1234")]
    [InlineData("1.234,56")]
    [InlineData("v2.3.12")]
    [InlineData("12:30")]
    public void Dates_addresses_and_short_numbers_are_not_phone_numbers(string word)
    {
        Assert.Empty(OcrText.Sensitive([Line("am", word, "um")], 2));
    }

    // Measured: Windows OCR returned "Tel." and "1234567" but no words for "+43 660" between them.
    private static OcrLine Gapped() => new([
        new OcrWord("Tel.", new(new(20, 172), new(67, 196))),
        new OcrWord("1234567", new(new(206, 173), new(321, 196))),
        new OcrWord("abends", new(new(560, 172), new(650, 196))),
    ]);

    [Fact]
    public void A_phone_number_extends_over_unread_gaps_to_its_neighbors()
    {
        Assert.Equal([PixelRect.FromCorners(69, 171, 558, 198)], OcrText.Sensitive([Gapped()], 2));
    }

    [Fact]
    public void A_phone_number_does_not_extend_over_normal_word_spacing()
    {
        var line = new OcrLine([
            new OcrWord("Tel.", new(new(20, 172), new(67, 196))),
            new OcrWord("1234567", new(new(76, 173), new(191, 196))),
        ]);
        Assert.Equal([PixelRect.FromCorners(74, 171, 193, 198)], OcrText.Sensitive([line], 2));
    }

    [Fact]
    public void Several_matches_in_several_lines_are_found_separately()
    {
        var found = OcrText.Sensitive([Line("a@b.at", "x", "c@d.com"), Line("x", "0660", "1234567")], 2);
        Assert.Equal(3, found.Count);
    }

    [Fact]
    public void Join_puts_words_on_lines()
    {
        Assert.Equal($"Hallo Welt{Environment.NewLine}zwei", OcrText.Join([Line("Hallo", "Welt"), Line("zwei")]));
    }
}
