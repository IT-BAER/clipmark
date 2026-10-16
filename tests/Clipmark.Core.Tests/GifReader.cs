namespace Clipmark.Core.Tests;

/// <summary>Minimal GIF89a reader for tests: composites frames (disposal 0 / 1) onto a BGRA canvas.</summary>
internal static class GifReader
{
    public sealed record Frame(byte[] Canvas, int DelayCs, int X, int Y, int Width, int Height, int ColorTableSize);

    public sealed record Gif(int Width, int Height, int? LoopCount, List<Frame> Frames);

    public static Gif Read(byte[] d)
    {
        if (System.Text.Encoding.ASCII.GetString(d, 0, 6) != "GIF89a") throw new InvalidDataException("signature");
        int w = U16(d, 6), h = U16(d, 8), p = 13;
        byte[]? global = null;
        if ((d[10] & 0x80) != 0) { int n = 3 << ((d[10] & 7) + 1); global = d[p..(p + n)]; p += n; }
        var canvas = new byte[w * h * 4];
        var frames = new List<Frame>();
        int? loop = null;
        int delay = 0, trans = -1;
        while (true)
        {
            byte b = d[p++];
            if (b == 0x3B) return new Gif(w, h, loop, frames);
            if (b == 0x21)
            {
                byte label = d[p++];
                if (label == 0xF9)
                {
                    if (d[p] != 4) throw new InvalidDataException("gce size");
                    trans = (d[p + 1] & 1) != 0 ? d[p + 4] : -1;
                    delay = U16(d, p + 2);
                    p += 6;
                    continue;
                }
                if (label == 0xFF && d[p] == 11 && System.Text.Encoding.ASCII.GetString(d, p + 1, 11) == "NETSCAPE2.0")
                {
                    p += 12;
                    if (d[p] == 3 && d[p + 1] == 1) loop = U16(d, p + 2);
                }
                p = SkipBlocks(d, p);
                continue;
            }
            if (b != 0x2C) throw new InvalidDataException($"block 0x{b:X2} at {p - 1}");
            int fx = U16(d, p), fy = U16(d, p + 2), fw = U16(d, p + 4), fh = U16(d, p + 6), packed = d[p + 8];
            p += 9;
            byte[]? table = global;
            int tableSize = 0;
            if ((packed & 0x80) != 0) { tableSize = 1 << ((packed & 7) + 1); table = d[p..(p + 3 * tableSize)]; p += 3 * tableSize; }
            if (table == null) throw new InvalidDataException("no color table");
            if ((packed & 0x40) != 0) throw new NotSupportedException("interlaced");
            int min = d[p++];
            var data = new List<byte>();
            while (d[p] != 0) { data.AddRange(d.AsSpan(p + 1, d[p]).ToArray()); p += d[p] + 1; }
            p++;
            var idx = Lzw(data.ToArray(), min, fw * fh);
            for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                {
                    int i = idx[y * fw + x];
                    if (i == trans) continue;
                    int o = ((fy + y) * w + fx + x) * 4;
                    canvas[o] = table[i * 3 + 2]; canvas[o + 1] = table[i * 3 + 1]; canvas[o + 2] = table[i * 3]; canvas[o + 3] = 255;
                }
            frames.Add(new Frame((byte[])canvas.Clone(), delay, fx, fy, fw, fh, tableSize));
            delay = 0; trans = -1;
        }
    }

    private static int U16(byte[] d, int p) => d[p] | d[p + 1] << 8;

    private static int SkipBlocks(byte[] d, int p)
    {
        while (d[p] != 0) p += d[p] + 1;
        return p + 1;
    }

    private static byte[] Lzw(byte[] data, int min, int count)
    {
        var output = new byte[count];
        var prefix = new int[4096];
        var suffix = new byte[4096];
        var first = new byte[4096];
        var length = new int[4096];
        int clear = 1 << min, eoi = clear + 1;
        for (int i = 0; i < clear; i++) { prefix[i] = -1; suffix[i] = (byte)i; first[i] = (byte)i; length[i] = 1; }
        int size = min + 1, next = eoi + 1, old = -1, pos = 0, bit = 0;
        void Emit(int code)
        {
            int n = length[code];
            if (pos + n > count) throw new InvalidDataException("too many pixels");
            for (int c = code, j = pos + n - 1; c >= 0; c = prefix[c], j--) output[j] = suffix[c];
            pos += n;
        }
        while (true)
        {
            if (bit + size > data.Length * 8) throw new InvalidDataException("truncated");
            int code = 0;
            for (int k = 0; k < size; k++, bit++) code |= ((data[bit >> 3] >> (bit & 7)) & 1) << k;
            if (code == clear) { size = min + 1; next = eoi + 1; old = -1; continue; }
            if (code == eoi) break;
            if (old < 0) { Emit(code); old = code; continue; }
            if (code > next) throw new InvalidDataException("bad code");
            if (next < 4096)
            {
                prefix[next] = old;
                suffix[next] = first[code == next ? old : code];
                first[next] = first[old];
                length[next] = length[old] + 1;
                next++;
                if (next == 1 << size && size < 12) size++;
            }
            Emit(code);
            old = code;
        }
        if (pos != count) throw new InvalidDataException($"pixels {pos} != {count}");
        return output;
    }
}
