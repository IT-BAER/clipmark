using Clipmark.Core.Editor;
using Clipmark.Core.TextActions;

namespace Clipmark.Core.Tests;

public class OcrTableTests
{
    // Words are 10 px per character wide and 20 px high, 6 px apart inside a cell; rows are 40 px apart.
    private static OcrLine Row(int row, params (float X, string Text)[] cells) =>
        new([.. cells.SelectMany(c => Words(row, c.X, c.Text))]);

    private static IEnumerable<OcrWord> Words(int row, float x, string text)
    {
        foreach (var w in text.Split(' '))
        {
            yield return new OcrWord(w, new FloatRect(new(x, row * 40), new(x + w.Length * 10, row * 40 + 20)));
            x += w.Length * 10 + 6;
        }
    }

    private static readonly OcrLine[] Products =
    [
        Row(0, (0, "Product"), (200, "Region"), (400, "Units")),
        Row(1, (0, "Laptop Pro 14"), (200, "Austria"), (400, "120")),
        Row(2, (0, "Monitor"), (200, "Germany"), (400, "85")),
    ];

    [Fact]
    public void Build_splits_rows_into_cells_at_wide_gaps_and_keeps_words_of_one_cell_together()
    {
        var table = OcrTable.Build(Products);
        Assert.NotNull(table);
        Assert.Equal(
            [["Product", "Region", "Units"], ["Laptop Pro 14", "Austria", "120"], ["Monitor", "Germany", "85"]],
            table);
    }

    [Fact]
    public void Build_joins_separate_lines_of_one_row_even_when_their_tops_differ_slightly()
    {
        OcrLine Cell(int row, float x, float dy, string text) =>
            new([.. Words(row, x, text).Select(w => w with { Box = new FloatRect(w.Box.Min + new System.Numerics.Vector2(0, dy), w.Box.Max + new System.Numerics.Vector2(0, dy)) })]);
        var lines = new[] { Cell(0, 0, 0, "Name"), Cell(0, 200, 3, "Size"), Cell(1, 0, 2, "a.txt"), Cell(1, 200, -2, "12 KB") };
        Assert.Equal([["Name", "Size"], ["a.txt", "12 KB"]], OcrTable.Build(lines));
    }

    [Fact]
    public void Build_leaves_a_missing_cell_empty()
    {
        var lines = new[] { Row(0, (0, "Product"), (200, "Region"), (400, "Units")), Row(1, (0, "Dock"), (400, "40")) };
        Assert.Equal([["Product", "Region", "Units"], ["Dock", "", "40"]], OcrTable.Build(lines));
    }

    [Fact]
    public void Build_keeps_columns_apart_under_a_wide_title_and_note_and_puts_them_in_the_first_column()
    {
        var lines = new[] { Row(0, (0, "Quarterly hardware orders and other items")) }
            .Concat(Products.Select(l => new OcrLine([.. l.Words.Select(w => w with { Box = new FloatRect(w.Box.Min + new System.Numerics.Vector2(0, 40), w.Box.Max + new System.Numerics.Vector2(0, 40)) })])))
            .Append(Row(4, (0, "Prices in EUR excluding VAT and shipping costs")))
            .ToArray();
        Assert.Equal(
            [
                ["Quarterly hardware orders and other items", "", ""],
                ["Product", "Region", "Units"], ["Laptop Pro 14", "Austria", "120"], ["Monitor", "Germany", "85"],
                ["Prices in EUR excluding VAT and shipping costs", "", ""],
            ],
            OcrTable.Build(lines));
    }

    [Fact]
    public void Build_puts_a_wrapped_second_line_of_a_cell_in_its_own_row()
    {
        var lines = Products.Append(Row(3, (0, "USB-C 90W"))).ToArray();
        Assert.Equal(["USB-C 90W", "", ""], OcrTable.Build(lines)![^1]);
    }

    [Fact]
    public void Build_keeps_right_aligned_numbers_in_the_column_of_their_left_aligned_header()
    {
        // "Units" spans 400..450; the numbers start 10 px right of it, less than a cell gap.
        var lines = new[] { Row(0, (0, "Item"), (400, "Units")), Row(1, (0, "Dock"), (460, "40")), Row(2, (0, "Mouse"), (460, "85")) };
        Assert.Equal([["Item", "Units"], ["Dock", "40"], ["Mouse", "85"]], OcrTable.Build(lines));
    }

    [Fact]
    public void Build_keeps_a_column_apart_from_the_next_one_when_a_row_has_cells_in_both_across_a_small_gap()
    {
        // The numbers under "Units" end at 460, "Price" starts at 470: as close as above, but the header row holds both.
        var lines = new[]
        {
            Row(0, (0, "Item"), (400, "Units"), (470, "Price")),
            Row(1, (0, "Dock"), (440, "40"), (500, "189.90")),
            Row(2, (0, "Mouse"), (430, "310"), (510, "24.99")),
        };
        Assert.Equal([["Item", "Units", "Price"], ["Dock", "40", "189.90"], ["Mouse", "310", "24.99"]], OcrTable.Build(lines));
    }

    [Fact]
    public void Build_finds_no_table_in_a_single_column()
    {
        Assert.Null(OcrTable.Build([Row(0, (0, "Alpha")), Row(1, (0, "Beta")), Row(2, (0, "Gamma"))]));
    }

    [Fact]
    public void Build_finds_no_table_in_prose()
    {
        Assert.Null(OcrTable.Build([Row(0, (0, "The quick brown fox jumps")), Row(1, (0, "over the lazy dog again"))]));
    }

    [Fact]
    public void Build_needs_two_rows_with_several_cells()
    {
        Assert.Null(OcrTable.Build([Row(0, (0, "Name"), (200, "Size")), Row(1, (0, "a.txt"))]));
    }

    private static readonly string[][] Small = [["Größe", "a & b"], ["<x>", ""]];

    [Fact]
    public void Tsv_separates_cells_by_tabs_and_rows_by_line_breaks()
    {
        Assert.Equal($"Größe\ta & b{Environment.NewLine}<x>\t", OcrTable.Tsv(Small));
    }

    [Fact]
    public void Html_offsets_point_at_the_html_and_the_table_fragment_in_utf8_bytes()
    {
        string payload = OcrTable.Html(Small);
        var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        int Offset(string name) => int.Parse(System.Text.RegularExpressions.Regex.Match(payload, name + @":(\d+)").Groups[1].Value);
        string Slice(int from, int to) => System.Text.Encoding.UTF8.GetString(bytes, from, to - from);

        Assert.StartsWith("Version:0.9\r\n", payload);
        Assert.StartsWith("<html>", Slice(Offset("StartHTML"), Offset("EndHTML")));
        Assert.EndsWith("</html>", Slice(Offset("StartHTML"), Offset("EndHTML")));
        Assert.Equal(bytes.Length, Offset("EndHTML"));
        string fragment = Slice(Offset("StartFragment"), Offset("EndFragment"));
        Assert.StartsWith("<table", fragment);
        Assert.EndsWith("</table>", fragment);
    }

    [Fact]
    public void Html_has_one_row_per_table_row_with_escaped_cells()
    {
        string html = OcrTable.Html(Small);
        var cells = System.Text.RegularExpressions.Regex.Matches(html, @"<td[^>]*>(.*?)</td>").Select(m => m.Groups[1].Value);
        Assert.Equal(["Größe", "a &amp; b", "&lt;x&gt;", ""], cells);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<tr>").Count);
    }

    [Fact]
    public void Build_finds_no_table_without_text()
    {
        Assert.Null(OcrTable.Build([]));
    }
}
