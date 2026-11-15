using Clipmark.App.Capture;
using Clipmark.Core.Capture;
using Clipmark.Core.Layout;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Printing;
using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX;
using Windows.Graphics.Printing;

namespace Clipmark.App.Output;

/// <summary>
/// The Windows print dialog that Snipping Tool opens, for a desktop window via PrintManagerInterop (no CoreWindow):
/// one page, the image shrunk to the printable area and centered, transparent pixels on white.
/// </summary>
internal static class ImagePrint
{
    private static readonly HashSet<nint> s_windows = [];
    private static CanvasPrintDocument? s_document;
    private static CanvasBitmap? s_bitmap;
    private static DispatcherQueue? s_dispatcher;
    private static Action<Exception>? s_failed;

    /// <summary>Raised when the dialog drew a preview page (test evidence that the dialog opened with the image).</summary>
    public static event Action? Previewed;

    /// <summary>Opens the print dialog for the image; <paramref name="failed"/> gets errors from the dialog's callbacks.</summary>
    public static void Show(nint hwnd, CapturedImage image, Action<Exception> failed)
    {
        if (!s_windows.Contains(hwnd))
        {
            PrintManagerInterop.GetForWindow(hwnd).PrintTaskRequested += OnPrintTaskRequested;
            s_windows.Add(hwnd);
        }
        Release();
        s_failed = failed;
        s_dispatcher = DispatcherQueue.GetForCurrentThread();
        var bgra = image.Bgra;
        if (image.HasTransparency)
            Pixels.OverWhite(bgra = (byte[])bgra.Clone());
        // Created on the UI thread; its events are raised there.
        var document = s_document = new CanvasPrintDocument();
        s_bitmap = CanvasBitmap.CreateFromBytes(document, bgra, image.Width, image.Height,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Ignore);
        document.PrintTaskOptionsChanged += (sender, _) => Guard(() => sender.SetPageCount(1));
        document.Preview += (_, args) => Guard(() =>
        {
            Draw(args.DrawingSession, args.PrintTaskOptions.GetPageDescription(args.PageNumber));
            Previewed?.Invoke();
        });
        document.Print += (_, args) => Guard(() =>
        {
            using var ds = args.CreateDrawingSession();
            Draw(ds, args.PrintTaskOptions.GetPageDescription(1));
        });
        ShowPrintUI(hwnd);
    }

    /// <remarks>
    /// Not PrintManagerInterop.ShowPrintUIForWindowAsync: under Native AOT its cast of the result to IAsyncOperation&lt;bool&gt;
    /// throws after the dialog opened. The operation is released unobserved, so its result and late errors are not reported.
    /// </remarks>
    private static unsafe void ShowPrintUI(nint hwnd)
    {
        Guid operation = new("cdb5efb3-5788-509d-9be1-71ccb8a3362a"); // IAsyncOperation<Boolean>
        using var interop = WinRT.ActivationFactory.Get("Windows.Graphics.Printing.PrintManager", new Guid("c5435a42-8d43-4e7b-a68a-ef311e392087"));
        nint self = interop.ThisPtr, result;
        // IPrintManagerInterop vtable: IInspectable (6 slots), GetForWindow, ShowPrintUIForWindowAsync.
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(void***)self)[7])(self, hwnd, &operation, &result));
        Marshal.Release(result);
    }

    private static void OnPrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args) => Guard(() =>
    {
        if (s_document is not { } document)
            return;
        var dispatcher = s_dispatcher;
        var task = args.Request.CreatePrintTask(App.DisplayName, source => Guard(() => source.SetSource(document)));
        task.Completed += (_, _) => dispatcher?.TryEnqueue(() =>
        {
            if (s_document == document)
                Release();
        });
    });

    private static void Draw(CanvasDrawingSession ds, PrintPageDescription page)
    {
        if (s_bitmap is not { } bitmap)
            return;
        var area = page.ImageableRect;
        var (x, y, w, h) = PrintLayout.Place((int)bitmap.SizeInPixels.Width, (int)bitmap.SizeInPixels.Height,
            area.X, area.Y, area.Width, area.Height);
        ds.DrawImage(bitmap, new Windows.Foundation.Rect(x, y, w, h), bitmap.Bounds, 1, CanvasImageInterpolation.HighQualityCubic);
    }

    /// <remarks>An exception leaving a WinRT event handler ends the process (WinUI fail-fast).</remarks>
    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            s_failed?.Invoke(ex);
        }
    }

    private static void Release()
    {
        s_bitmap?.Dispose();
        s_bitmap = null;
        s_document?.Dispose();
        s_document = null;
    }
}
