using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI.Text;

namespace Clipmark.App.Editor;

/// <summary>Segoe UI Emoji: color drawing into a box, and which emoji this PC's font can draw.</summary>
internal static class EmojiFont
{
    public const string Family = "Segoe UI Emoji";

    private static readonly Dictionary<string, bool> Drawable = [];
    private static CanvasFontFace? _face;

    /// <summary>Scales the glyph's ink to fill the box, centered.</summary>
    public static void Draw(CanvasDrawingSession ds, string emoji, Rect box)
    {
        using var layout = Layout(ds, emoji);
        var ink = layout.DrawBounds;
        if (ink.Width <= 0 || ink.Height <= 0)
            return;
        float scale = (float)(Math.Min(box.Width, box.Height) / Math.Max(ink.Width, ink.Height));
        var transform = ds.Transform;
        ds.Transform = Matrix3x2.CreateTranslation(-(float)(ink.X + ink.Width / 2), -(float)(ink.Y + ink.Height / 2))
            * Matrix3x2.CreateScale(scale)
            * Matrix3x2.CreateTranslation((float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2)) * transform;
        ds.DrawTextLayout(layout, 0, 0, Microsoft.UI.Colors.Black);
        ds.Transform = transform;
    }

    /// <summary>False when the font lacks a code point (newer emoji, subdivision flags) or draws a flag as two letters.</summary>
    public static bool CanDraw(string emoji)
    {
        if (Drawable.TryGetValue(emoji, out bool known))
            return known;
        _face ??= CanvasFontSet.GetSystemFontSet()
            .GetMatchingFonts(Family, new FontWeight { Weight = 400 }, FontStretch.Normal, FontStyle.Normal).Fonts[0];
        bool ok = true, flag = false;
        for (int i = 0; i < emoji.Length && ok; i += char.IsSurrogatePair(emoji, i) ? 2 : 1)
        {
            int cp = char.ConvertToUtf32(emoji, i);
            flag |= cp is >= 0x1F1E6 and <= 0x1F1FF;
            ok = cp is 0x200D or 0xFE0F or 0x20E3 || _face.HasCharacter((uint)cp);
        }
        if (ok && flag)
        {
            using var layout = Layout(CanvasDevice.GetSharedDevice(), emoji);
            ok = layout.ClusterMetrics.Length == 1;
        }
        return Drawable[emoji] = ok;
    }

    // Color glyphs need the option on the layout: the format's Options alone draws them monochrome (measured 15 October 2026).
    private static CanvasTextLayout Layout(ICanvasResourceCreator creator, string emoji)
    {
        using var format = new CanvasTextFormat { FontFamily = Family, FontSize = 100, WordWrapping = CanvasWordWrapping.NoWrap };
        return new CanvasTextLayout(creator, emoji, format, 0, 0) { Options = CanvasDrawTextOptions.EnableColorFont };
    }
}
