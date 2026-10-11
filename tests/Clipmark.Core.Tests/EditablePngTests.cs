using System.Buffers.Binary;
using System.Text;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class EditablePngTests
{
    // A valid 1x1 gray PNG (signature, IHDR, IDAT, IEND).
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
    private static readonly byte[] Base = [1, 2, 3, 250, 0, 7];
    private const string Json = """{"format":"clipmark","text":"ä €"}""";

    private static List<(string Type, byte[] Data, uint Crc)> Chunks(byte[] png)
    {
        var list = new List<(string, byte[], uint)>();
        for (int at = 8; at < png.Length;)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            list.Add((Encoding.ASCII.GetString(png, at + 4, 4), png[(at + 8)..(at + 8 + length)],
                BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length))));
            at += 12 + length;
        }
        return list;
    }

    [Fact]
    public void Embedded_data_reads_back()
    {
        var data = EditablePng.Read(EditablePng.Embed(Png, Json, Base));
        Assert.NotNull(data);
        Assert.Equal(Json, data.Json);
        Assert.Equal(Base, data.BasePng);
    }

    [Fact]
    public void The_chunk_sits_before_IEND_and_every_original_chunk_is_unchanged()
    {
        var before = Chunks(Png);
        var after = Chunks(EditablePng.Embed(Png, Json, Base));
        Assert.Equal(["IHDR", "IDAT", "clMK", "IEND"], after.Select(c => c.Type));
        foreach (var (b, a) in new[] { (before[0], after[0]), (before[1], after[1]), (before[2], after[3]) })
        {
            Assert.Equal(b.Type, a.Type);
            Assert.Equal(b.Data, a.Data);
            Assert.Equal(b.Crc, a.Crc);
        }
    }

    [Fact]
    public void Crc32_matches_the_known_IEND_value()
    {
        // Every PNG ends with IEND and the CRC AE 42 60 82.
        Assert.Equal(0xAE426082u, Chunks(Png)[^1].Crc);
        Assert.Equal(0xAE426082u, EditablePng.Crc32("IEND"u8));
    }

    [Fact]
    public void The_chunk_crc_is_correct()
    {
        var chunk = Chunks(EditablePng.Embed(Png, Json, Base))[2];
        Assert.Equal(EditablePng.Crc32([.. "clMK"u8, .. chunk.Data]), chunk.Crc);
    }

    [Fact]
    public void A_plain_png_has_no_editable_data()
    {
        Assert.Null(EditablePng.Read(Png));
    }

    [Fact]
    public void A_damaged_chunk_is_ignored()
    {
        var file = EditablePng.Embed(Png, Json, Base);
        int at = Png.Length - 12 + 8 + 2; // inside the clMK data
        file[at] ^= 0xFF;
        Assert.Null(EditablePng.Read(file));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFF, 0xFF, 0xF0, 0x63, 0x6C, 0x4D, 0x4B })]
    public void Garbage_and_truncated_files_have_no_editable_data(byte[] file)
    {
        Assert.Null(EditablePng.Read(file));
    }

    [Fact]
    public void Embedding_again_replaces_the_old_chunk()
    {
        var twice = EditablePng.Embed(EditablePng.Embed(Png, "old", [9]), Json, Base);
        Assert.Single(Chunks(twice), c => c.Type == "clMK");
        Assert.Equal(Json, EditablePng.Read(twice)!.Json);
    }

    [Fact]
    public void Embedding_into_a_non_png_throws()
    {
        Assert.Throws<ArgumentException>(() => EditablePng.Embed([1, 2, 3], Json, Base));
    }
}
