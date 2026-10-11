using System.Buffers.Binary;
using System.Text;

namespace Clipmark.Core.Editor;

/// <summary>Editable data inside a PNG: the document JSON and the unflattened base image as a PNG of its own.</summary>
public sealed record EditableData(string Json, byte[] BasePng);

/// <summary>
/// Stores <see cref="EditableData"/> in a private chunk "clMK" before IEND. The name marks it ancillary (viewers ignore it),
/// private, and unsafe to copy (editors that change the pixels drop it). Payload: version byte, JSON byte length (uint32 BE),
/// JSON (UTF-8), base PNG.
/// </summary>
public static class EditablePng
{
    private const byte PayloadVersion = 1;
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] ChunkType = "clMK"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>The PNG with the data chunk inserted before IEND, replacing an earlier one.</summary>
    public static byte[] Embed(byte[] png, string json, byte[] basePng)
    {
        var chunks = Walk(png) ?? throw new ArgumentException("Not a complete PNG file.", nameof(png));
        byte[] text = Encoding.UTF8.GetBytes(json);
        var payload = new byte[5 + text.Length + basePng.Length];
        payload[0] = PayloadVersion;
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(1), (uint)text.Length);
        text.CopyTo(payload, 5);
        basePng.CopyTo(payload, 5 + text.Length);

        using var output = new MemoryStream(png.Length + payload.Length + 12);
        output.Write(Signature);
        foreach (var (at, length, type) in chunks)
        {
            if (type.SequenceEqual(ChunkType)) continue;
            if (type.SequenceEqual("IEND"u8)) WriteChunk(output, payload);
            output.Write(png, at, 12 + length);
        }
        return output.ToArray();
    }

    /// <summary>The embedded data, or null for a plain, damaged or non-PNG file.</summary>
    public static EditableData? Read(byte[] file)
    {
        foreach (var (at, length, type) in Walk(file) ?? [])
        {
            if (!type.SequenceEqual(ChunkType)) continue;
            var body = file.AsSpan(at + 4, 4 + length); // type + data
            if (Crc32(body) != BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 8 + length))) return null;
            var data = body[4..];
            if (data.Length < 5 || data[0] != PayloadVersion) return null;
            uint textLength = BinaryPrimitives.ReadUInt32BigEndian(data[1..]);
            if (textLength > data.Length - 5) return null;
            try
            {
                return new EditableData(StrictUtf8.GetString(data.Slice(5, (int)textLength)), data[(5 + (int)textLength)..].ToArray());
            }
            catch (DecoderFallbackException) { return null; }
        }
        return null;
    }

    public static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in bytes) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    // Chunks (offset of the length field, data length, type) up to and including IEND; null when the file is not a well-formed PNG.
    private static List<(int At, int Length, byte[] Type)>? Walk(byte[] file)
    {
        if (file.Length < Signature.Length || !file.AsSpan(0, Signature.Length).SequenceEqual(Signature)) return null;
        var chunks = new List<(int, int, byte[])>();
        for (int at = Signature.Length; at <= file.Length - 12;)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
            if (length > (uint)(file.Length - at - 12)) return null;
            var type = file[(at + 4)..(at + 8)];
            chunks.Add((at, (int)length, type));
            if (type.AsSpan().SequenceEqual("IEND"u8)) return chunks;
            at += 12 + (int)length;
        }
        return null;
    }

    private static void WriteChunk(Stream output, byte[] data)
    {
        byte[] body = [.. ChunkType, .. data];
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(body));
        output.Write(number);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
