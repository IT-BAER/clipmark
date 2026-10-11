#if DEBUG
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Clipmark.App.Output;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Clipmark.App;

/// <summary>
/// Debug test mode (CLIPMARK_TEST_DIR): the cloaked app polls DIR\cmd.txt, runs it, deletes it and writes DIR\result.txt.
/// Commands: open FILE | click NAME (automation name or menu item text) | expand NAME (open a split/drop-down menu)
/// | drag [shift] X,Y X,Y... (image px)
/// | shot FILE (window, plus FILE-popupN.png per open popup) | save FILE (export PNG) | wait MS
/// | saveas FILE (Save as without the picker; .jpg = JPEG) | close (WM_CLOSE to the window) | assert empty|clean|dirty.
/// </summary>
internal sealed class TestDriver
{
    private readonly MainPage _page;
    private readonly string _dir;
    private readonly DispatcherQueueTimer _timer;
    private bool _busy;

    public TestDriver(MainPage page, string dir)
    {
        _page = page;
        _dir = dir;
        _timer = page.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private async Task PollAsync()
    {
        string cmd = Path.Combine(_dir, "cmd.txt");
        if (_busy || !File.Exists(cmd))
            return;
        _busy = true;
        string line = "", result = "ok";
        try
        {
            string[] lines = File.ReadAllLines(cmd);
            File.Delete(cmd);
            foreach (string raw in lines)
            {
                line = raw;
                var parts = raw.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && !parts[0].StartsWith('#'))
                    await RunAsync(parts[0], parts.Length > 1 ? parts[1] : "");
            }
        }
        catch (Exception e)
        {
            result = $"error: {line}: {e.Message}";
        }
        File.WriteAllText(Path.Combine(_dir, "result.txt"), result);
        _busy = false;
    }

    private async Task RunAsync(string command, string arg)
    {
        switch (command)
        {
            case "open":
                _page.ShowImage(Path.GetFullPath(arg));
                await Task.Delay(500);
                break;
            case "click":
                Click(Find(arg) ?? throw new InvalidOperationException("element not found"), open: false);
                await Task.Delay(300);
                break;
            case "expand":
                Click(Find(arg) ?? throw new InvalidOperationException("element not found"), open: true);
                await Task.Delay(300);
                break;
            case "drag":
                var editor = _page.Editor ?? throw new InvalidOperationException("no capture shown");
                bool shift = arg.StartsWith("shift ");
                var points = arg[(shift ? 6 : 0)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split(','))
                    .Select(p => new Vector2(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture)))
                    .ToArray();
                editor.Press(points[0], shift);
                foreach (var p in points.Skip(1))
                    editor.Drag(p, shift);
                editor.Release();
                break;
            case "shot":
                await RenderAsync((UIElement)_page.XamlRoot.Content, arg);
                int n = 0;
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(_page.XamlRoot))
                {
                    if (popup.Child is not null)
                        await RenderAsync(popup.Child, Path.ChangeExtension(arg, null) + $"-popup{n++}.png");
                }
                break;
            case "save":
                var image = _page.Editor?.Export() ?? throw new InvalidOperationException("no capture shown");
                await File.WriteAllBytesAsync(arg, await CaptureOutput.EncodePngAsync(image));
                break;
            case "saveas":
                if (!await _page.SaveToAsync(Path.GetFullPath(arg)))
                    throw new InvalidOperationException("not saved");
                break;
            case "close":
                Interop.Native.PostMessage(Win32Interop.GetWindowFromWindowId(_page.XamlRoot.ContentIslandEnvironment.AppWindowId),
                    Interop.Native.WM_CLOSE, 0, 0);
                await Task.Delay(500);
                break;
            case "assert":
                string state = _page.Editor?.Document is not { } doc ? "empty" : doc.IsDirty ? "dirty" : "clean";
                if (state != arg)
                    throw new InvalidOperationException($"state is {state}");
                break;
            case "wait":
                await Task.Delay(int.Parse(arg, CultureInfo.InvariantCulture));
                break;
            default:
                throw new FormatException("unknown command");
        }
    }

    /// <summary>First visible element in the window or an open popup whose automation name (or menu item text) matches.</summary>
    private FrameworkElement? Find(string name)
    {
        var roots = new List<DependencyObject> { _page.XamlRoot.Content };
        roots.AddRange(VisualTreeHelper.GetOpenPopupsForXamlRoot(_page.XamlRoot).Select(p => (DependencyObject)p.Child));
        var queue = new Queue<DependencyObject>(roots);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is FrameworkElement { Visibility: Visibility.Visible } fe)
            {
                if (AutomationProperties.GetName(fe) == name || (fe is MenuFlyoutItem item && item.Text == name)
                    || (fe is Button { Content: string text } && text == name))
                    return fe;
            }
            else if (node is UIElement { Visibility: Visibility.Collapsed })
                continue;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    private static void Click(FrameworkElement element, bool open)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(element);
        if (!element.IsLoaded || (element is Control { IsEnabled: false }))
            throw new InvalidOperationException("element is disabled");
        if (open)
            ((IExpandCollapseProvider?)peer.GetPattern(PatternInterface.ExpandCollapse)
                ?? throw new InvalidOperationException("element cannot expand")).Expand();
        else if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
            invoke.Invoke();
        else if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
            toggle.Toggle();
        else if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand)
            expand.Expand();
        else
            throw new InvalidOperationException("element has no invoke, toggle or expand pattern");
    }

    /// <summary>Renders the element over the theme background; the window backdrop is not part of the XAML tree.</summary>
    private static async Task RenderAsync(UIElement element, string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        byte background = ((FrameworkElement)element).ActualTheme == ElementTheme.Dark ? (byte)0x20 : (byte)0xF3;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int inverse = 255 - pixels[i + 3];
            for (int c = 0; c < 3; c++)
                pixels[i + c] = (byte)(pixels[i + c] + background * inverse / 255); // premultiplied "over"
            pixels[i + 3] = 255;
        }
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var file = File.Create(path);
        await stream.AsStreamForRead().CopyToAsync(file);
    }
}
#endif
