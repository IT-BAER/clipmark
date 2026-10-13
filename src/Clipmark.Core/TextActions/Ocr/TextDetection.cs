namespace Clipmark.Core.TextActions.Ocr;

/// <summary>An axis-aligned text box in image pixels.</summary>
public readonly record struct TextBox(double X0, double Y0, double X1, double Y1)
{
    /// <summary>Reading order: rows of 10 px (round half to even, as Python round), then left to right.</summary>
    public static IEnumerable<T> InReadingOrder<T>(IEnumerable<T> items, Func<T, TextBox> box) =>
        items.OrderBy(i => Math.Round(box(i).Y0 / 10, MidpointRounding.ToEven)).ThenBy(i => box(i).X0);
}

/// <summary>A connected component of the detection mask: bounding box in map pixels.</summary>
public readonly record struct Component(int X, int Y, int Width, int Height);

/// <summary>DB (differentiable binarization) post-processing of the detector probability map.</summary>
public static class TextDetection
{
    /// <summary>Input size for the detector: scale into [minSide, maxSide] (long side capped at 4000), multiples of 32.</summary>
    public static (int Width, int Height) InputSize(int width, int height, int maxSide, int minSide)
    {
        double s = 1.0;
        if (Math.Max(width, height) > maxSide) s = (double)maxSide / Math.Max(width, height);
        if (Math.Min(width, height) * s < minSide) s = Math.Min((double)minSide / Math.Min(width, height), 4000.0 / Math.Max(width, height));
        static int Snap(double v) => Math.Max(32, (int)Math.Round(v / 32, MidpointRounding.ToEven) * 32);
        return (Snap(width * s), Snap(height * s));
    }

    /// <summary>prob &gt; threshold, optionally dilated by a 2x2 kernel anchored at (1,1): out(x,y) = max of in(x-1..x, y-1..y).</summary>
    public static bool[] Mask(ReadOnlySpan<float> prob, int width, int height, float threshold, bool dilate)
    {
        var mask = new bool[width * height];
        for (int i = 0; i < mask.Length; i++) mask[i] = prob[i] > threshold;
        if (!dilate) return mask;
        var outMask = new bool[mask.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                outMask[i] = mask[i] || (x > 0 && mask[i - 1]) || (y > 0 && (mask[i - width] || (x > 0 && mask[i - width - 1])));
            }
        return outMask;
    }

    /// <summary>8-connected components in raster order of their first pixel.</summary>
    public static List<Component> Components(bool[] mask, int width, int height)
    {
        var seen = new bool[mask.Length];
        var found = new List<Component>();
        var stack = new Stack<int>();
        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || seen[start]) continue;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % width, y = i / width;
                (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
                for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                    for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                    {
                        int n = ny * width + nx;
                        if (mask[n] && !seen[n]) { seen[n] = true; stack.Push(n); }
                    }
            }
            found.Add(new Component(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
        }
        return found;
    }

    /// <summary>
    /// Boxes in image pixels in reading order: components with min side &gt;= 3 and mean probability &gt;= boxThreshold,
    /// grown by d = w*h*unclip/(2(w+h)) (kept when min side + 2d &gt;= 5), scaled by image/map size and clamped.
    /// </summary>
    public static List<TextBox> Boxes(ReadOnlySpan<float> prob, int mapWidth, int mapHeight, IEnumerable<Component> components,
        int imageWidth, int imageHeight, float boxThreshold, double unclip)
    {
        double sx = (double)imageWidth / mapWidth, sy = (double)imageHeight / mapHeight;
        var boxes = new List<TextBox>();
        foreach (var c in components)
        {
            if (Math.Min(c.Width, c.Height) < 3) continue;
            double sum = 0;
            for (int y = c.Y; y < c.Y + c.Height; y++)
                for (int x = c.X; x < c.X + c.Width; x++)
                    sum += prob[y * mapWidth + x];
            if (sum / (c.Width * c.Height) < boxThreshold) continue;
            double d = c.Width * c.Height * unclip / (2 * (c.Width + c.Height));
            if (Math.Min(c.Width, c.Height) + 2 * d < 5) continue;
            boxes.Add(new TextBox(Math.Max(0, (c.X - d) * sx), Math.Max(0, (c.Y - d) * sy),
                Math.Min(imageWidth, (c.X + c.Width + d) * sx), Math.Min(imageHeight, (c.Y + c.Height + d) * sy)));
        }
        return [.. TextBox.InReadingOrder(boxes, b => b)];
    }
}
