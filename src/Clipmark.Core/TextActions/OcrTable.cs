using System.Text;
using Clipmark.Core.Editor;

namespace Clipmark.Core.TextActions;

/// <summary>Recognized text as a table (Copy as table).</summary>
public static class OcrTable
{
    // A gap between words wider than this times the word height starts a new cell; spaces are about 0.3x.
    private const float CellGap = 0.75f;

    /// <summary>
    /// Rows of cells, or null when fewer than 2 rows have 2 or more cells. Lines that overlap vertically form one row; a
    /// row splits into cells at wide gaps. Columns come only from rows with several cells, so a title or note line
    /// does not merge them; such a line goes to the first column it overlaps.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>>? Build(IReadOnlyList<OcrLine> lines)
    {
        var rows = Rows(lines).Select(Cells).ToList();
        if (rows.Count(r => r.Count > 1) < 2)
            return null;
        var bands = Bands(rows.SelectMany((r, i) => r.Count > 1 ? r.Select(c => (i, c)) : []));
        if (bands.Count < 2)
            return null;
        var table = new List<IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            var cells = new string[bands.Count];
            foreach (var (min, max, _, text) in row)
            {
                int band = Band(bands, min, max);
                cells[band] = cells[band] is null ? text : cells[band] + " " + text;
            }
            table.Add([.. cells.Select(c => c ?? "")]);
        }
        return table;
    }

    /// <summary>Cells separated by tabs, rows by line breaks (plain-text paste, Excel too).</summary>
    public static string Tsv(IReadOnlyList<IReadOnlyList<string>> rows) =>
        string.Join(Environment.NewLine, rows.Select(r => string.Join('\t', r)));

    /// <summary>The clipboard "HTML Format" payload (CF_HTML): a bordered table, offsets in UTF-8 bytes.</summary>
    public static string Html(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var table = new StringBuilder("<table style=\"border-collapse:collapse\">");
        foreach (var row in rows)
        {
            table.Append("<tr>");
            foreach (var cell in row)
                table.Append("<td style=\"border:1px solid #000;padding:4px\">").Append(cell.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")).Append("</td>");
            table.Append("</tr>");
        }
        table.Append("</table>");
        const string Header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string Before = "<html><body><!--StartFragment-->", After = "<!--EndFragment--></body></html>";
        int start = Encoding.UTF8.GetByteCount(string.Format(Header, 0, 0, 0, 0));
        int fragment = start + Encoding.UTF8.GetByteCount(Before), end = fragment + Encoding.UTF8.GetByteCount(table.ToString());
        return string.Format(Header, start, end + Encoding.UTF8.GetByteCount(After), fragment, end) + Before + table + After;
    }

    // Lines grouped top to bottom: a line joins the row it overlaps vertically by at least half the smaller height.
    private static List<List<OcrWord>> Rows(IReadOnlyList<OcrLine> lines)
    {
        var rows = new List<(float Top, float Bottom, List<OcrWord> Words)>();
        foreach (var line in lines.Where(l => l.Words.Count > 0).OrderBy(l => l.Words.Min(w => w.Box.Min.Y + w.Box.Max.Y)))
        {
            float top = line.Words.Min(w => w.Box.Min.Y), bottom = line.Words.Max(w => w.Box.Max.Y);
            int i = rows.FindIndex(r => Math.Min(bottom, r.Bottom) - Math.Max(top, r.Top) >= 0.5f * Math.Min(bottom - top, r.Bottom - r.Top));
            if (i < 0)
                rows.Add((top, bottom, [.. line.Words]));
            else
                rows[i] = (Math.Min(top, rows[i].Top), Math.Max(bottom, rows[i].Bottom), [.. rows[i].Words, .. line.Words]);
        }
        return [.. rows.Select(r => r.Words)];
    }

    // Words left to right, split where the gap is wider than CellGap times the smaller word height.
    private static List<(float Min, float Max, float Height, string Text)> Cells(List<OcrWord> words)
    {
        var cells = new List<(float Min, float Max, float Height, string Text)>();
        OcrWord? previous = null;
        foreach (var word in words.OrderBy(w => w.Box.Min.X))
        {
            var box = word.Box;
            if (previous is { } p && box.Min.X - p.Box.Max.X <= CellGap * Math.Min(Height(box), Height(p.Box)))
                cells[^1] = (cells[^1].Min, Math.Max(cells[^1].Max, box.Max.X), Math.Min(cells[^1].Height, Height(box)), cells[^1].Text + " " + word.Text);
            else
                cells.Add((box.Min.X, box.Max.X, Height(box), word.Text));
            previous = word;
        }
        return cells;
    }

    private static float Height(FloatRect box) => box.Max.Y - box.Min.Y;

    // Cell ranges merged into column bands left to right: overlapping, or across a gap narrower than a cell gap when the
    // band holds no cell of the same row yet (a left-aligned header over right-aligned numbers).
    private static List<(float Min, float Max)> Bands(IEnumerable<(int Row, (float Min, float Max, float Height, string Text) Cell)> cells)
    {
        var bands = new List<(float Min, float Max, HashSet<int> Rows)>();
        foreach (var (row, (min, max, height, _)) in cells.OrderBy(c => c.Cell.Min))
        {
            if (bands.Count > 0 && (min <= bands[^1].Max || (min - bands[^1].Max <= CellGap * height && !bands[^1].Rows.Contains(row))))
            {
                bands[^1] = (bands[^1].Min, Math.Max(bands[^1].Max, max), bands[^1].Rows);
                bands[^1].Rows.Add(row);
            }
            else
                bands.Add((min, max, [row]));
        }
        return [.. bands.Select(b => (b.Min, b.Max))];
    }

    // The first band the range overlaps, else the nearest one.
    private static int Band(List<(float Min, float Max)> bands, float min, float max)
    {
        int first = bands.FindIndex(b => b.Min <= max && min <= b.Max);
        if (first >= 0)
            return first;
        var distance = bands.Select(b => Math.Max(b.Min - max, min - b.Max)).ToList();
        return distance.IndexOf(distance.Min());
    }
}
