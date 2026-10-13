namespace Clipmark.Core.TextActions.Ocr;

/// <summary>Pixel helpers for the OCR models: OpenCV-exact bilinear resize, crops and tensor packing.</summary>
public static class OcrImage
{
    /// <summary>
    /// Interleaved 8-bit resize, bit-exact with OpenCV INTER_LINEAR (half-pixel centers, edge clamp, 11-bit fixed-point
    /// weights and the SIMD vertical rounding of resize.cpp).
    /// </summary>
    public static byte[] Resize(byte[] src, int width, int height, int channels, int dstWidth, int dstHeight)
    {
        var (xOfs, xA0, xA1) = Weights(width, dstWidth, clampFirst: true);
        var (yOfs, yB0, yB1) = Weights(height, dstHeight, clampFirst: false);
        int rowLen = dstWidth * channels;
        var rows = new int[height][];
        int[] Row(int y)
        {
            y = Math.Clamp(y, 0, height - 1);
            if (rows[y] is { } cached) return cached;
            var row = new int[rowLen];
            int srcRow = y * width * channels;
            for (int dx = 0; dx < dstWidth; dx++)
            {
                int sx = xOfs[dx], o = srcRow + sx * channels, d = dx * channels;
                if (sx >= width - 1)
                    for (int c = 0; c < channels; c++) row[d + c] = src[o + c] * 2048;
                else
                    for (int c = 0; c < channels; c++) row[d + c] = src[o + c] * xA0[dx] + src[o + channels + c] * xA1[dx];
            }
            return rows[y] = row;
        }
        var dst = new byte[rowLen * dstHeight];
        for (int dy = 0; dy < dstHeight; dy++)
        {
            int[] s0 = Row(yOfs[dy]), s1 = Row(yOfs[dy] + 1);
            int b0 = yB0[dy], b1 = yB1[dy], o = dy * rowLen;
            for (int i = 0; i < rowLen; i++)
                dst[o + i] = (byte)Math.Clamp((((s0[i] >> 4) * b0 >> 16) + ((s1[i] >> 4) * b1 >> 16) + 2) >> 2, 0, 255);
        }
        return dst;
    }

    private static (int[] Ofs, int[] W0, int[] W1) Weights(int srcSize, int dstSize, bool clampFirst)
    {
        double scale = 1.0 / ((double)dstSize / srcSize);
        var (ofs, w0, w1) = (new int[dstSize], new int[dstSize], new int[dstSize]);
        for (int d = 0; d < dstSize; d++)
        {
            float f = (float)((d + 0.5) * scale - 0.5);
            int s = (int)MathF.Floor(f);
            f -= s;
            // Columns clamp the offset itself; rows keep it and clamp the two source rows when read.
            if (clampFirst && s < 0) (f, s) = (0, 0);
            if (clampFirst && s >= srcSize - 1) (f, s) = (0, srcSize - 1);
            (ofs[d], w0[d], w1[d]) = (s, (short)MathF.Round((1f - f) * 2048f), (short)MathF.Round(f * 2048f));
        }
        return (ofs, w0, w1);
    }

    /// <summary>BGRA pixels of [x0,x1) x [y0,y1); rotate turns them 90 degrees counter-clockwise (numpy rot90).</summary>
    public static byte[] Crop(byte[] bgra, int width, int x0, int y0, int x1, int y1, bool rotate)
    {
        int w = x1 - x0, h = y1 - y0;
        var dst = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = ((y0 + y) * width + x0 + x) * 4;
                // rot90: output row (w-1-x), column y.
                int d = rotate ? ((w - 1 - x) * h + y) * 4 : (y * w + x) * 4;
                Buffer.BlockCopy(bgra, s, dst, d, 4);
            }
        return dst;
    }

    /// <summary>Writes (v/255-0.5)/0.5 as RGB planes of a [3,H,tensorWidth] tensor at offset, columns 0..width.</summary>
    public static void Normalize(byte[] bgra, int width, int height, float[] tensor, int offset, int tensorWidth)
    {
        int plane = height * tensorWidth;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int s = (y * width + x) * 4, d = offset + y * tensorWidth + x;
                tensor[d] = (bgra[s + 2] / 255f - 0.5f) / 0.5f;
                tensor[d + plane] = (bgra[s + 1] / 255f - 0.5f) / 0.5f;
                tensor[d + 2 * plane] = (bgra[s] / 255f - 0.5f) / 0.5f;
            }
    }
}
