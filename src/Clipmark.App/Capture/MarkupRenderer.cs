using Clipmark.App.Editor;
using Clipmark.Core.Editor;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX;

namespace Clipmark.App.Capture;

/// <summary>Burns pen strokes into a capture off screen with the editor's own stroke drawing.</summary>
internal static class MarkupRenderer
{
    public static CapturedImage Flatten(CapturedImage image, IReadOnlyList<Stroke> strokes)
    {
        var device = CanvasDevice.GetSharedDevice();
        var source = image.Bgra;
        if (image.HasTransparency)
        {
            source = (byte[])source.Clone();
            for (int i = 0; i < source.Length; i += 4)
                for (int c = 0; c < 3; c++)
                    source[i + c] = (byte)((source[i + c] * source[i + 3] + 127) / 255);
        }
        using var bitmap = CanvasBitmap.CreateFromBytes(device, source, image.Width, image.Height,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        using var target = new CanvasRenderTarget(device, image.Width, image.Height, 96);
        using (var ds = target.CreateDrawingSession())
        {
            ds.Clear(Microsoft.UI.Colors.Transparent);
            ds.DrawImage(bitmap);
            foreach (var s in strokes)
                EditorCanvas.DrawStroke(ds, s);
        }
        var (bgra, transparent) = EditorCanvas.Straight(target);
        return image with { Bgra = bgra, HasTransparency = transparent };
    }
}
