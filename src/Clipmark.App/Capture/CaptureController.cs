using Clipmark.App.Editor;
using Clipmark.App.Output;
using Clipmark.Core.Settings;
using Clipmark.Core.TextActions;

namespace Clipmark.App.Capture;

/// <summary>One capture: optional delay, freeze, overlay (unless all monitors), then clipboard / save / toast.</summary>
internal sealed class CaptureController(nint clipboardOwner)
{
    private bool _busy;

    /// <summary>Raised with the PNG path after a successful capture.</summary>
    public event Action<string>? Captured;

    /// <summary>Raised with a user-facing message when a step fails.</summary>
    public event Action<string>? Failed;

    /// <summary>Hides the caller's windows before the frame is taken; awaited.</summary>
    public Func<Task>? BeforeFreeze { get; set; }

    public async Task RunAsync(CaptureMode? modeOverride = null, int? delayOverride = null)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var settings = AppSettings.Current;
            if (BeforeFreeze is not null)
                await BeforeFreeze();
            int delay = delayOverride ?? settings.DelaySeconds;
            if (delay > 0)
                await Task.Delay(TimeSpan.FromSeconds(delay));

            CapturedImage? image;
            bool extract = false;
            using (var frame = ScreenCapture.CaptureDesktop())
            {
                var mode = modeOverride ?? settings.Mode;
                if (mode == CaptureMode.AllMonitors)
                    image = frame.Cut(frame.Screen.Bounds);
                else
                {
                    var session = new OverlaySession(frame, mode);
                    image = await session.RunAsync();
                    extract = session.ExtractText;
                    if (session.Mode != mode && session.Mode != CaptureMode.AllMonitors)
                        AppSettings.Update(s => s with { Mode = session.Mode }); // pill choice sticks, like Snipping Tool
                }
            }
            if (image is null)
                return;
            if (extract)
                await ExtractTextAsync(image);
            else
                await DeliverAsync(image, AppSettings.Current);
        }
        catch (Exception e)
        {
            Failed?.Invoke("Capture failed: " + e.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Text extractor: OCR text of the region to the clipboard, a toast says what happened; no editor, no file.</summary>
    private async Task ExtractTextAsync(CapturedImage image)
    {
        if (await TextRecognizer.RecognizeAsync(image, System.Numerics.Vector2.Zero) is not { } lines)
        {
            Failed?.Invoke("No text recognition language is installed (Settings > Time & language > Language & region).");
            return;
        }
        if (lines.Count == 0)
        {
            CaptureOutput.ShowTextToast("No text found in the selection");
            return;
        }
        if (CaptureOutput.CopyTextToClipboard(clipboardOwner, OcrText.Join(lines)))
            CaptureOutput.ShowTextToast("Text copied to clipboard");
        else
            Failed?.Invoke("The clipboard is in use by another app; the text was not copied.");
    }

    private async Task DeliverAsync(CapturedImage image, ClipmarkSettings settings)
    {
        var png = await CaptureOutput.EncodePngAsync(image);
        bool copied = settings.CopyToClipboard && CaptureOutput.CopyToClipboard(clipboardOwner, image, png);
        if (settings.CopyToClipboard && !copied)
            Failed?.Invoke("The clipboard is in use by another app; the capture was not copied.");

        string? saved = null;
        if (settings.AutoSave)
        {
            try
            {
                saved = await CaptureOutput.SaveAsync(settings.SaveFolder ?? CaptureOutput.DefaultFolder, png);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Failed?.Invoke("Auto-save failed: " + e.Message);
            }
        }
        string path = saved ?? await CaptureOutput.SaveAsync(CaptureOutput.TempFolder, png);
        if (settings.ShowToast)
            CaptureOutput.ShowToast(path, copied, saved);
        Captured?.Invoke(path);
    }
}
