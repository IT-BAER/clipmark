namespace Clipmark.Core.Capture;

public static class Pixels
{
    /// <summary>Blends BGRA pixels with straight alpha onto white in place and makes them opaque (for formats without alpha).</summary>
    public static void OverWhite(byte[] bgra)
    {
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            for (int c = 0; c < 3; c++)
                bgra[i + c] = (byte)((bgra[i + c] * a + 255 * (255 - a)) / 255);
            bgra[i + 3] = 255;
        }
    }
}
