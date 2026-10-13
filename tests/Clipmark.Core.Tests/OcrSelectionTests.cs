using System.Numerics;
using Clipmark.Core.Editor;
using Clipmark.Core.TextActions;

namespace Clipmark.Core.Tests;

public class OcrSelectionTests
{
    // Each word is 10 px per character wide, words 20 px apart, lines 40 px apart and 20 px high.
    private static OcrLine Line(int row, params string[] words)
    {
        var list = new List<OcrWord>();
        float x = 0;
        foreach (var w in words)
        {
            list.Add(new OcrWord(w, new FloatRect(new(x, row * 40), new(x + w.Length * 10, row * 40 + 20))));
            x += w.Length * 10 + 20;
        }
        return new OcrLine(list);
    }

    private static readonly OcrLine[] Text2 = [Line(0, "Hello", "world"), Line(1, "second", "line")];

    [Fact]
    public void Text_inside_one_line_is_the_substring_between_the_carets()
    {
        Assert.Equal("lo wo", OcrSelection.Text(Text2, new(0, 3), new(0, 8)));
    }

    [Fact]
    public void Text_across_lines_takes_the_line_ends_in_either_caret_order()
    {
        Assert.Equal($"world{Environment.NewLine}sec", OcrSelection.Text(Text2, new(1, 3), new(0, 6)));
    }

    [Theory]
    [InlineData(93, 10, 0, 8)]  // "world" spans x 70..120: 46% of 5 characters rounds to 2
    [InlineData(1, 10, 0, 0)]
    [InlineData(55, 10, 0, 5)]  // gap 50..70, nearer "Hello"
    [InlineData(65, 10, 0, 6)]  // nearer "world"
    [InlineData(-1, 10, 0, 0)]  // inside the slack before the first word
    [InlineData(0, 50, 1, 0)]
    [InlineData(119, 59, 1, 11)] // end of "line" (80..120)
    public void HitTest_maps_a_point_on_text_to_the_nearest_character_boundary(float x, float y, int line, int offset)
    {
        Assert.Equal(new OcrCaret(line, offset), OcrSelection.HitTest(Text2, new Vector2(x, y), 2));
    }

    [Theory]
    [InlineData(60, 30)]  // between the lines, outside the slack
    [InlineData(125, 10)] // right of "world" beyond the slack
    [InlineData(60, -5)]
    public void HitTest_returns_null_off_the_text(float x, float y)
    {
        Assert.Null(OcrSelection.HitTest(Text2, new Vector2(x, y), 2));
    }

    [Theory]
    [InlineData(200, 100, 1, 11)] // below and right of the text: end of the last line
    [InlineData(-50, -50, 0, 0)]
    [InlineData(93, 25, 0, 8)]    // nearer line 0 vertically
    public void Nearest_clamps_a_dragged_point_to_the_closest_line(float x, float y, int line, int offset)
    {
        Assert.Equal(new OcrCaret(line, offset), OcrSelection.Nearest(Text2, new Vector2(x, y)));
    }

    [Fact]
    public void Nearest_is_null_without_words()
    {
        Assert.Null(OcrSelection.Nearest([new OcrLine([])], Vector2.Zero));
    }

    [Fact]
    public void Boxes_cover_the_selected_characters_of_each_line()
    {
        Assert.Equal(
            [new FloatRect(new(30, 0), new(120, 20)), new FloatRect(new(0, 40), new(30, 60))],
            OcrSelection.Boxes(Text2, new(1, 3), new(0, 3)));
        Assert.Empty(OcrSelection.Boxes(Text2, new(0, 4), new(0, 4)));
    }

    [Theory]
    [InlineData(0, 0, "Hello")]
    [InlineData(0, 5, "Hello")]  // end of "Hello"
    [InlineData(0, 6, "world")]  // start of "world"
    [InlineData(1, 9, "line")]
    public void Word_spans_the_word_around_a_caret(int line, int offset, string word)
    {
        var (start, end) = OcrSelection.Word(Text2, new OcrCaret(line, offset));
        Assert.Equal(word, OcrSelection.Text(Text2, start, end));
    }

    [Fact]
    public void All_selects_the_same_text_as_copy_all()
    {
        var (start, end) = OcrSelection.All(Text2);
        Assert.Equal(OcrText.Join(Text2), OcrSelection.Text(Text2, start, end));
    }

    [Fact]
    public void Carets_never_split_a_letter_from_its_combining_mark()
    {
        OcrLine[] cafe = [Line(0, "Café")]; // 5 UTF-16 units, 4 letters over x 0..50
        Assert.Equal(new OcrCaret(0, 5), OcrSelection.HitTest(cafe, new Vector2(47, 10), 2));
        Assert.Equal(new OcrCaret(0, 3), OcrSelection.HitTest(cafe, new Vector2(40, 10), 2));
        Assert.Equal([new FloatRect(new(37.5f, 0), new(50, 20))], OcrSelection.Boxes(cafe, new(0, 3), new(0, 5)));
    }
}
