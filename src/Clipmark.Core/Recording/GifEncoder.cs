namespace Clipmark.Core.Recording;

/// <summary>
/// Streams an animated GIF89a that loops forever. Each frame stores only the rectangle that changed since the pixels were
/// last written, with unchanged pixels transparent; a frame without change extends the previous frame's delay. The color
/// table is exact up to 255 colors, else a median cut over the changed pixels. Does not own the output stream.
/// </summary>
public sealed class GifEncoder : IDisposable
{
    /// <summary>Largest per-channel difference that still counts as unchanged; absorbs video compression noise.</summary>
    public const int DefaultTolerance = 2;

    private const int Bits = 6, Bins = 1 << (3 * Bits), Shift = 8 - Bits, Mask = (1 << Bits) - 1;

    private readonly Stream _out;
    private readonly int _w, _h, _tolerance;
    private readonly int[] _written;
    private readonly bool[] _changed;
    private readonly MemoryStream _pending = new();
    private int _pendingDelay = -1;
    private bool _finished;

    private readonly int[] _hist = new int[Bins];
    private readonly long[] _sumR = new long[Bins], _sumG = new long[Bins], _sumB = new long[Bins];
    private readonly short[] _nearest = new short[Bins];
    private readonly int[] _exactKeys = new int[512];
    private readonly byte[] _exactValues = new byte[512];
    private readonly ushort[] _codes = new ushort[4096 << 8];
    private readonly int[] _stamps = new int[4096 << 8];
    private int _generation;

    public GifEncoder(Stream output, int width, int height, int tolerance = DefaultTolerance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, ushort.MaxValue);
        _out = output;
        _w = width;
        _h = height;
        _tolerance = tolerance;
        _written = new int[width * height];
        Array.Fill(_written, -1);
        _changed = new bool[width * height];
        _out.Write("GIF89a"u8);
        WriteU16(_out, width);
        WriteU16(_out, height);
        _out.Write([0x70, 0, 0]); // no global color table, 8-bit color resolution
        _out.Write([0x21, 0xFF, 0x0B]);
        _out.Write("NETSCAPE2.0"u8);
        _out.Write([3, 1, 0, 0, 0]); // loop forever
    }

    /// <summary>Adds a top-down BGRA frame of the encoder's size, shown for <paramref name="delayCs"/> hundredths of a second.</summary>
    public void AddFrame(ReadOnlySpan<byte> bgra, int delayCs)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        if (bgra.Length != _w * _h * 4) throw new ArgumentException($"Expected {_w * _h * 4} bytes, got {bgra.Length}.", nameof(bgra));
        if (!FindChanges(bgra, out int minX, out int minY, out int maxX, out int maxY))
        {
            _pendingDelay = Math.Min(ushort.MaxValue, _pendingDelay + delayCs);
            return;
        }
        Flush();
        int count = BuildPalette(bgra, minX, minY, maxX, maxY, out var palette, out bool exact);
        int rw = maxX - minX + 1, rh = maxY - minY + 1;
        var indices = new byte[rw * rh];
        byte transparent = (byte)count;
        if (!exact) Array.Fill(_nearest, (short)-1);
        for (int y = minY, j = 0; y <= maxY; y++)
            for (int i = y * _w + minX, end = i + rw; i < end; i++, j++)
            {
                if (!_changed[i]) { indices[j] = transparent; continue; }
                int c = Rgb(bgra, i);
                indices[j] = exact ? ExactIndex(c) : Nearest(c, palette, count);
                _written[i] = c;
            }
        int bits = 1;
        while (1 << bits < count + 1) bits++;
        _pending.SetLength(0);
        _pending.WriteByte(0x2C);
        WriteU16(_pending, minX);
        WriteU16(_pending, minY);
        WriteU16(_pending, rw);
        WriteU16(_pending, rh);
        _pending.WriteByte((byte)(0x80 | (bits - 1)));
        var table = new byte[3 << bits];
        palette.AsSpan(0, count * 3).CopyTo(table);
        _pending.Write(table);
        WriteLzw(_pending, indices, Math.Max(2, bits));
        _pendingDelay = Math.Min(ushort.MaxValue, delayCs);
        _pendingTransparent = transparent;
    }

    private byte _pendingTransparent;

    /// <summary>Writes the last frame and the trailer.</summary>
    public void Dispose()
    {
        if (_finished) return;
        Flush();
        _out.WriteByte(0x3B);
        _out.Flush();
        _finished = true;
    }

    private void Flush()
    {
        if (_pendingDelay < 0) return;
        _out.Write([0x21, 0xF9, 0x04, (1 << 2) | 1]); // do not dispose, transparent index present
        WriteU16(_out, _pendingDelay);
        _out.Write([_pendingTransparent, 0]);
        _pending.Position = 0;
        _pending.CopyTo(_out);
        _pendingDelay = -1;
    }

    private static int Rgb(ReadOnlySpan<byte> bgra, int i) => bgra[i * 4 + 2] << 16 | bgra[i * 4 + 1] << 8 | bgra[i * 4];

    private bool FindChanges(ReadOnlySpan<byte> bgra, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = _w; minY = _h; maxX = -1; maxY = -1;
        for (int y = 0, i = 0; y < _h; y++)
            for (int x = 0; x < _w; x++, i++)
            {
                int c = Rgb(bgra, i), w = _written[i];
                bool changed = w < 0
                    || Math.Abs((c >> 16) - (w >> 16)) > _tolerance
                    || Math.Abs(((c >> 8) & 255) - ((w >> 8) & 255)) > _tolerance
                    || Math.Abs((c & 255) - (w & 255)) > _tolerance;
                _changed[i] = changed;
                if (!changed) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                maxY = y;
            }
        return maxX >= 0;
    }

    private int BuildPalette(ReadOnlySpan<byte> bgra, int minX, int minY, int maxX, int maxY, out byte[] palette, out bool exact)
    {
        Array.Fill(_exactKeys, -1);
        int count = 0;
        exact = true;
        for (int y = minY; y <= maxY && exact; y++)
            for (int i = y * _w + minX, end = y * _w + maxX; i <= end; i++)
            {
                if (!_changed[i]) continue;
                int c = Rgb(bgra, i), slot = Slot(c);
                if (_exactKeys[slot] == c) continue;
                if (count == 255) { exact = false; break; }
                _exactKeys[slot] = c;
                _exactValues[slot] = (byte)count++;
            }
        if (exact)
        {
            palette = new byte[count * 3];
            for (int s = 0; s < _exactKeys.Length; s++)
            {
                int c = _exactKeys[s];
                if (c < 0) continue;
                int p = _exactValues[s] * 3;
                palette[p] = (byte)(c >> 16); palette[p + 1] = (byte)(c >> 8); palette[p + 2] = (byte)c;
            }
            return count;
        }
        palette = MedianCut(bgra, minX, minY, maxX, maxY);
        return palette.Length / 3;
    }

    private int Slot(int c)
    {
        int s = (int)((uint)(c * -1640531535) >> 23);
        while (_exactKeys[s] >= 0 && _exactKeys[s] != c) s = (s + 1) & 511;
        return s;
    }

    private byte ExactIndex(int c) => _exactValues[Slot(c)];

    private byte Nearest(int c, byte[] palette, int count)
    {
        int r = c >> 16, g = (c >> 8) & 255, b = c & 255;
        int key = (r >> Shift) << (2 * Bits) | (g >> Shift) << Bits | b >> Shift;
        int v = _nearest[key];
        if (v >= 0) return (byte)v;
        int best = 0, bestDistance = int.MaxValue;
        for (int j = 0; j < count; j++)
        {
            int dr = palette[j * 3] - r, dg = palette[j * 3 + 1] - g, db = palette[j * 3 + 2] - b;
            int d = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
            if (d < bestDistance) { bestDistance = d; best = j; }
        }
        _nearest[key] = (short)best;
        return (byte)best;
    }

    private byte[] MedianCut(ReadOnlySpan<byte> bgra, int minX, int minY, int maxX, int maxY)
    {
        Array.Clear(_hist);
        Array.Clear(_sumR);
        Array.Clear(_sumG);
        Array.Clear(_sumB);
        for (int y = minY; y <= maxY; y++)
            for (int i = y * _w + minX, end = y * _w + maxX; i <= end; i++)
            {
                if (!_changed[i]) continue;
                int r = bgra[i * 4 + 2], g = bgra[i * 4 + 1], b = bgra[i * 4];
                int k = (r >> Shift) << (2 * Bits) | (g >> Shift) << Bits | b >> Shift;
                _hist[k]++; _sumR[k] += r; _sumG[k] += g; _sumB[k] += b;
            }
        var boxes = new List<Box> { Shrink(new Box(0, Mask, 0, Mask, 0, Mask)) };
        while (boxes.Count < 255)
        {
            int pick = -1;
            long bestScore = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                long score = boxes[i].Count * boxes[i].LongestSpan;
                if (score > bestScore) { bestScore = score; pick = i; }
            }
            if (pick < 0) break;
            var (low, high) = Split(boxes[pick]);
            boxes[pick] = low;
            boxes.Add(high);
        }
        var palette = new byte[boxes.Count * 3];
        for (int i = 0; i < boxes.Count; i++)
        {
            long n = 0, r = 0, g = 0, b = 0;
            var x = boxes[i];
            for (int cr = x.R0; cr <= x.R1; cr++)
                for (int cg = x.G0; cg <= x.G1; cg++)
                    for (int cb = x.B0; cb <= x.B1; cb++)
                    {
                        int k = cr << (2 * Bits) | cg << Bits | cb;
                        n += _hist[k]; r += _sumR[k]; g += _sumG[k]; b += _sumB[k];
                    }
            n = Math.Max(1, n);
            palette[i * 3] = (byte)(r / n); palette[i * 3 + 1] = (byte)(g / n); palette[i * 3 + 2] = (byte)(b / n);
        }
        return palette;
    }

    private (Box Low, Box High) Split(Box b)
    {
        int rs = b.R1 - b.R0, gs = b.G1 - b.G0, bs = b.B1 - b.B0;
        int axis = rs >= gs && rs >= bs ? 0 : gs >= bs ? 1 : 2;
        Span<long> slab = stackalloc long[1 << Bits];
        slab.Clear();
        for (int cr = b.R0; cr <= b.R1; cr++)
            for (int cg = b.G0; cg <= b.G1; cg++)
                for (int cb = b.B0; cb <= b.B1; cb++)
                    slab[axis == 0 ? cr : axis == 1 ? cg : cb] += _hist[cr << (2 * Bits) | cg << Bits | cb];
        int lo = axis == 0 ? b.R0 : axis == 1 ? b.G0 : b.B0, hi = axis == 0 ? b.R1 : axis == 1 ? b.G1 : b.B1;
        long half = b.Count / 2, acc = 0;
        int cut = lo;
        for (int v = lo; v < hi; v++)
        {
            acc += slab[v];
            cut = v;
            if (acc >= half) break;
        }
        var (l, h) = axis switch
        {
            0 => (b with { R1 = cut }, b with { R0 = cut + 1 }),
            1 => (b with { G1 = cut }, b with { G0 = cut + 1 }),
            _ => (b with { B1 = cut }, b with { B0 = cut + 1 }),
        };
        return (Shrink(l), Shrink(h));
    }

    /// <summary>Shrinks a box to its occupied bins and counts its pixels.</summary>
    private Box Shrink(Box b)
    {
        int r0 = Mask, r1 = 0, g0 = Mask, g1 = 0, b0 = Mask, b1 = 0;
        long n = 0;
        for (int cr = b.R0; cr <= b.R1; cr++)
            for (int cg = b.G0; cg <= b.G1; cg++)
                for (int cb = b.B0; cb <= b.B1; cb++)
                {
                    int c = _hist[cr << (2 * Bits) | cg << Bits | cb];
                    if (c == 0) continue;
                    n += c;
                    r0 = Math.Min(r0, cr); r1 = Math.Max(r1, cr);
                    g0 = Math.Min(g0, cg); g1 = Math.Max(g1, cg);
                    b0 = Math.Min(b0, cb); b1 = Math.Max(b1, cb);
                }
        return n == 0 ? b with { Count = 0 } : new Box(r0, r1, g0, g1, b0, b1, n);
    }

    private readonly record struct Box(int R0, int R1, int G0, int G1, int B0, int B1, long Count = 0)
    {
        public int LongestSpan => Math.Max(R1 - R0, Math.Max(G1 - G0, B1 - B0));
    }

    private void WriteLzw(Stream s, byte[] indices, int minCodeSize)
    {
        s.WriteByte((byte)minCodeSize);
        var block = new byte[256];
        int blockLength = 0, pendingBits = 0;
        ulong bitBuffer = 0;
        void Put(int code, int size)
        {
            bitBuffer |= (ulong)code << pendingBits;
            pendingBits += size;
            while (pendingBits >= 8)
            {
                block[++blockLength] = (byte)bitBuffer;
                bitBuffer >>= 8;
                pendingBits -= 8;
                if (blockLength == 255) { block[0] = 255; s.Write(block, 0, 256); blockLength = 0; }
            }
        }
        int clear = 1 << minCodeSize, eoi = clear + 1, next = eoi + 1, codeSize = minCodeSize + 1;
        _generation++;
        Put(clear, codeSize);
        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int k = indices[i], key = prefix << 8 | k;
            if (_stamps[key] == _generation) { prefix = _codes[key]; continue; }
            Put(prefix, codeSize);
            if (next == 4096)
            {
                Put(clear, codeSize);
                next = eoi + 1;
                codeSize = minCodeSize + 1;
                _generation++;
            }
            else
            {
                if (next >= 1 << codeSize) codeSize++;
                _codes[key] = (ushort)next++;
                _stamps[key] = _generation;
            }
            prefix = k;
        }
        Put(prefix, codeSize);
        Put(eoi, codeSize);
        if (pendingBits > 0) Put(0, 8 - pendingBits);
        if (blockLength > 0) { block[0] = (byte)blockLength; s.Write(block, 0, blockLength + 1); }
        s.WriteByte(0);
    }

    private static void WriteU16(Stream s, int v)
    {
        s.WriteByte((byte)v);
        s.WriteByte((byte)(v >> 8));
    }
}
