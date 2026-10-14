using Clipmark.App.Editor;
using Clipmark.App.Output;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;
using Clipmark.Core.Recording;
using Clipmark.Core.Settings;
using Clipmark.Core.TextActions;

namespace Clipmark.App.Capture;

/// <summary>One capture: optional delay, freeze, overlay (unless all monitors), then clipboard / save / toast.</summary>
internal sealed class CaptureController(nint clipboardOwner)
{
    private bool _busy;

    /// <summary>Raised with the PNG path after a successful capture.</summary>
    public event Action<string>? Captured;

    /// <summary>Raised when the pill was in Video mode and a region was picked.</summary>
    public event Action<RecordingArea>? RecordRequested;

    /// <summary>Raised with a user-facing message when a step fails.</summary>
    public event Action<string>? Failed;

    /// <summary>Hides the caller's windows before the frame is taken; awaited.</summary>
    public Func<Task>? BeforeFreeze { get; set; }

    /// <summary>Video starts the overlay in Video mode (no delay); the pill can still switch between photo and video.</summary>
    public async Task RunAsync(CaptureMode? modeOverride = null, int? delayOverride = null, bool video = false)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var settings = AppSettings.Current;
            if (BeforeFreeze is not null)
                await BeforeFreeze();
            int delay = video ? 0 : delayOverride ?? settings.DelaySeconds;
            if (delay > 0)
                await Task.Delay(TimeSpan.FromSeconds(delay));

            CapturedImage? image;
            bool extract = false;
            RecordingArea? record = null;
            Rgba? picked = null;
            IReadOnlyList<IReadOnlyList<PixelPoint>> marks = [];
            var origin = default(PixelPoint);
            using (var frame = ScreenCapture.CaptureDesktop())
            {
                var mode = modeOverride ?? settings.Mode;
                if (mode == CaptureMode.AllMonitors && !video)
                    image = frame.Cut(frame.Screen.Bounds);
                else
                {
                    var session = new OverlaySession(frame, mode, video);
                    image = await session.RunAsync();
                    extract = session.ExtractText;
                    picked = session.PickedColor;
                    marks = session.Marks;
                    origin = session.CutOrigin;
                    if (session.PickedRegion is { } region)
                        record = RecordingRegion.Fit(region, frame.Monitors);
                    if (!session.Video && session.Mode != mode && session.Mode != CaptureMode.AllMonitors)
                        AppSettings.Update(s => s with { Mode = session.Mode }); // pill choice sticks, like Snipping Tool
                }
            }
            if (record is { } area)
            {
                RecordRequested?.Invoke(area);
                return;
            }
            if (picked is { } color)
            {
                CopyColor(color);
                return;
            }
            if (image is null)
                return;
            if (extract)
                await ExtractTextAsync(image);
            else
            {
                var current = AppSettings.Current;
                await DeliverAsync(image, current, QuickMarkup.ToStrokes(marks, origin, image.Width, image.Height, current.Border ? current.BorderWidth : 0));
            }
        }
        catch (Exception e)
        {
            Failed?.Invoke(Loc.Format("CaptureFailed", e.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Color picker: the clicked pixel as text in the chosen format to the clipboard, a toast says what was copied.</summary>
    private void CopyColor(Rgba color)
    {
        string text = ColorSample.Format(color, AppSettings.Current.ColorFormat);
        if (CaptureOutput.CopyTextToClipboard(clipboardOwner, text))
            CaptureOutput.ShowTextToast(Loc.Format("ToastColorCopied", text));
        else
            Failed?.Invoke(Loc.Get("ClipboardBusyColor"));
    }

    /// <summary>Text extractor: OCR text of the region to the clipboard, a toast says what happened; no editor, no file.</summary>
    private async Task ExtractTextAsync(CapturedImage image)
    {
        if (await TextRecognizer.RecognizeAsync(image, System.Numerics.Vector2.Zero) is not { } lines)
        {
            Failed?.Invoke(Loc.Get("OcrLanguageMissing"));
            return;
        }
        if (lines.Count == 0)
        {
            CaptureOutput.ShowTextToast(Loc.Get("ToastNoText"));
            return;
        }
        if (CaptureOutput.CopyTextToClipboard(clipboardOwner, OcrText.Join(lines, AppSettings.Current.OcrRemoveLineBreaks)))
            CaptureOutput.ShowTextToast(Loc.Get("ToastTextCopied"));
        else
            Failed?.Invoke(Loc.Get("ClipboardBusyText"));
    }

    /// <summary>
    /// <paramref name="strokes"/> (quick markup, already shifted by the border width) are burned into the image that is copied and saved;
    /// the file the editor opens and the history item are an editable PNG with the strokes as pen objects.
    /// </summary>
    internal async Task DeliverAsync(CapturedImage image, ClipmarkSettings settings, IReadOnlyList<Stroke>? strokes = null)
    {
        if (settings.Border)
        {
            var (bgra, width, height) = Pixels.AddBorder(image.Bgra, image.Width, image.Height, settings.BorderWidth, settings.BorderColor);
            image = image with { Bgra = bgra, Width = width, Height = height };
        }
        var capture = image;
        bool marked = strokes is { Count: > 0 };
        if (marked)
            image = MarkupRenderer.Flatten(image, strokes!);
        var png = await CaptureOutput.EncodePngAsync(image);
        var editable = marked
            ? EditablePng.Embed(png, DocumentJson.Serialize(QuickMarkup.Document(image.Width, image.Height, strokes!)), await CaptureOutput.EncodePngAsync(capture))
            : png;
        bool copied = settings.CopyToClipboard && CaptureOutput.CopyToClipboard(clipboardOwner, image, png);
        if (settings.CopyToClipboard && !copied)
            Failed?.Invoke(Loc.Get("ClipboardBusyCapture"));

        string? saved = null;
        if (settings.AutoSave)
        {
            try
            {
                saved = await CaptureOutput.SaveAsync(settings.SaveFolder ?? CaptureOutput.DefaultFolder, png);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Failed?.Invoke(Loc.Format("AutoSaveFailed", e.Message));
            }
        }
        string path = saved is not null && !marked ? saved : await CaptureOutput.SaveAsync(CaptureOutput.TempFolder, editable);
        await CaptureHistory.AddAsync(path, editable);
        if (settings.ShowToast)
            CaptureOutput.ShowToast(path, copied, saved);
        Captured?.Invoke(path);
    }
}
