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
/// | drag [shift] [ctrl] [hold] X,Y X,Y... (image px; a point may be @rotate, @start, @end or @CropHandleName of the selection, plus an optional +DX,DY;
/// hold = no release, to screenshot the drag) | release | hover X,Y (eyedropper loupe) | dblclick X,Y | type TEXT | nokey NAME [ctrl] (must not be handled)
/// | assert count N | assert editing | key NAME [shift] [ctrl] (VirtualKey name, runs the page key handling)
/// | shot FILE (window, plus FILE-popupN.png per open popup) | save FILE (export PNG) | wait MS
/// | saveas FILE (Save as without the picker; .jpg = JPEG) | close (WM_CLOSE to the window) | assert empty|clean|dirty
/// | assert selected N | assert crop X,Y,W,H | set NAME VALUE (types into a NumberBox, then moves focus so it evaluates)
/// | saveeditable FILE | textwait (text actions finished) | assert status TEXT (text actions status contains) | assert redactions N
/// | assert editable (opened with live objects) | assert message TEXT (open error bar contains)
/// | record X,Y,W,H MS FILE [system] [mic] (records that desktop region straight to FILE; warnings go to DIR\warnings.txt)
/// | openvideo FILE | assert video [FILE] (a recording is shown) | savevideo FILE (Save as without the picker).
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
            case "openvideo":
                _page.ShowVideo(Path.GetFullPath(arg));
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
                var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool shift = words.Contains("shift"), ctrl = words.Contains("ctrl");
                static Vector2 Parse(string xy) =>
                    new(float.Parse(xy.Split(',')[0], CultureInfo.InvariantCulture), float.Parse(xy.Split(',')[1], CultureInfo.InvariantCulture));
                var points = words.Where(w => w.Contains(',') || w.StartsWith('@'))
                    .Select(w => w.StartsWith('@')
                        ? (editor.SelectionHandle(w[1..].Split('+')[0]) ?? throw new InvalidOperationException($"no handle {w}"))
                            + (w.Contains('+') ? Parse(w.Split('+')[1]) : Vector2.Zero)
                        : Parse(w))
                    .ToArray();
                editor.Press(points[0], shift, ctrl);
                foreach (var p in points.Skip(1))
                    editor.Drag(p, shift);
                if (!words.Contains("hold"))
                    editor.Release();
                break;
            case "hover":
                var at3 = arg.Split(',');
                (_page.Editor ?? throw new InvalidOperationException("no capture shown")).Hover(
                    new Vector2(float.Parse(at3[0], CultureInfo.InvariantCulture), float.Parse(at3[1], CultureInfo.InvariantCulture)));
                await Task.Delay(100);
                break;
            case "dblclick":
                var at2 = arg.Split(',');
                (_page.Editor ?? throw new InvalidOperationException("no capture shown")).DoubleClick(
                    new Vector2(float.Parse(at2[0], CultureInfo.InvariantCulture), float.Parse(at2[1], CultureInfo.InvariantCulture)));
                await Task.Delay(200);
                break;
            case "type":
                // type TEXT: replaces the text being edited; \n is a line break.
                var textBox = _page.Editor?.TextEditor ?? throw new InvalidOperationException("no text being edited");
                textBox.Text = arg.Replace("\\n", "\r");
                break;
            case "nokey":
                var noKeyWords = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (_page.RunKey(Enum.Parse<Windows.System.VirtualKey>(noKeyWords[0]),
                    noKeyWords.Contains("ctrl") ? Windows.System.VirtualKeyModifiers.Control : Windows.System.VirtualKeyModifiers.None))
                    throw new InvalidOperationException("key was handled");
                break;
            case "assert" when arg.StartsWith("count "):
                int count = _page.Editor?.Document?.Objects.Count ?? 0;
                if (count != int.Parse(arg[6..], CultureInfo.InvariantCulture))
                    throw new InvalidOperationException($"{count} objects");
                break;
            case "assert" when arg == "editing":
                if (_page.Editor?.IsEditingText != true)
                    throw new InvalidOperationException("not editing text");
                break;
            case "release":
                (_page.Editor ?? throw new InvalidOperationException("no capture shown")).Release();
                break;
            case "shot":
                await RenderAsync((UIElement)_page.XamlRoot.Content, arg);
                int n = 0;
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(_page.XamlRoot))
                {
                    string popupPath = Path.ChangeExtension(arg, null) + $"-popup{n++}.png";
                    try
                    {
                        if (popup.Child is not null)
                            await RenderAsync(popup.Child, popupPath);
                    }
                    catch (ArgumentException e) // RenderTargetBitmap cannot capture some content (ColorPicker ring)
                    {
                        File.WriteAllText(Path.ChangeExtension(popupPath, ".txt"), e.Message);
                    }
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
            case "saveeditable":
                if (!await _page.SaveToAsync(Path.GetFullPath(arg), editable: true))
                    throw new InvalidOperationException("not saved");
                break;
            case "textwait":
                // Waits until text actions finished reading the image (up to 20 s).
                for (int t = 0; _page.TextActionsBusy; t += 100)
                {
                    if (t > 20000)
                        throw new InvalidOperationException("text recognition did not finish");
                    await Task.Delay(100);
                }
                break;
            case "assert" when arg.StartsWith("status "):
                string status = (Find("Text actions status") as TextBlock)?.Text ?? throw new InvalidOperationException("no status");
                if (!status.Contains(arg[7..], StringComparison.Ordinal))
                    throw new InvalidOperationException($"status is '{status}'");
                break;
            case "assert" when arg.StartsWith("redactions "):
                int redactions = _page.Editor?.Document?.Redactions.Count ?? 0;
                if (redactions != int.Parse(arg[11..], CultureInfo.InvariantCulture))
                    throw new InvalidOperationException($"{redactions} redactions");
                break;
            case "assert" when arg.StartsWith("message "):
                var bar = Descendants(_page.XamlRoot.Content).OfType<InfoBar>().First();
                if (!bar.IsOpen || bar.Message?.Contains(arg[8..], StringComparison.Ordinal) != true)
                    throw new InvalidOperationException($"message is '{(bar.IsOpen ? bar.Message : "(closed)")}'");
                break;
            case "assert" when arg.StartsWith("video"):
                // assert video [FILE]: a recording is shown (and it is FILE).
                if (_page.VideoPath is not { } shown || (arg.Length > 6 && !string.Equals(shown, Path.GetFullPath(arg[6..]), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"video shown is '{_page.VideoPath}'");
                break;
            case "savevideo":
                if (!await _page.SaveVideoToAsync(Path.GetFullPath(arg)))
                    throw new InvalidOperationException("not saved");
                break;
            case "assert" when arg == "editable":
                if (_page.Editor?.FromEditable != true)
                    throw new InvalidOperationException("not opened as editable");
                break;
            case "close":
                Interop.Native.PostMessage(Win32Interop.GetWindowFromWindowId(_page.XamlRoot.ContentIslandEnvironment.AppWindowId),
                    Interop.Native.WM_CLOSE, 0, 0);
                await Task.Delay(500);
                break;
            case "key":
                var keyWords = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var modifiers = Windows.System.VirtualKeyModifiers.None;
                if (keyWords.Contains("shift")) modifiers |= Windows.System.VirtualKeyModifiers.Shift;
                if (keyWords.Contains("ctrl")) modifiers |= Windows.System.VirtualKeyModifiers.Control;
                if (!_page.RunKey(Enum.Parse<Windows.System.VirtualKey>(keyWords[0]), modifiers))
                    throw new InvalidOperationException("key not handled");
                await Task.Delay(100);
                break;
            case "wheel":
                // wheel DELTA X,Y [shift]: the canvas wheel handling with a pointer at image px X,Y.
                var wheelWords = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var at = wheelWords[1].Split(',');
                _page.Wheel(int.Parse(wheelWords[0], CultureInfo.InvariantCulture), wheelWords.Contains("shift"),
                    new Vector2(float.Parse(at[0], CultureInfo.InvariantCulture), float.Parse(at[1], CultureInfo.InvariantCulture)));
                break;
            case "color":
                // color #RRGGBB: sets the open style flyout's color ring.
                var picker = Descendants(VisualTreeHelper.GetOpenPopupsForXamlRoot(_page.XamlRoot).First().Child).OfType<ColorPicker>().First();
                var hex = Convert.FromHexString(arg.TrimStart('#'));
                picker.Color = Windows.UI.Color.FromArgb(255, hex[0], hex[1], hex[2]);
                await Task.Delay(100);
                break;
            case "assert" when arg.StartsWith("has "):
                if (Find(arg[4..]) is null)
                    throw new InvalidOperationException("element not found");
                break;
            case "set":
                // NAME may contain spaces: the value is the last word. The NumberBox evaluates on losing focus.
                int cut = arg.LastIndexOf(' ');
                var box = Find(arg[..cut]) as NumberBox ?? throw new InvalidOperationException("number box not found");
                box.Focus(FocusState.Programmatic);
                await Task.Delay(100);
                var input = Descendants(box).OfType<TextBox>().First();
                input.Text = arg[(cut + 1)..];
                ((Control?)Find("Apply crop") ?? throw new InvalidOperationException("no focus target")).Focus(FocusState.Programmatic);
                await Task.Delay(200);
                break;
            case "assert" when arg.StartsWith("crop "):
                var frame = _page.Editor?.CropFrame ?? throw new InvalidOperationException("not cropping");
                string actual = $"{frame.X},{frame.Y},{frame.Width},{frame.Height}";
                if (actual != arg[5..])
                    throw new InvalidOperationException($"crop frame is {actual}");
                break;
            case "assert" when arg.StartsWith("selected "):
                int selected = _page.Editor?.Selection?.Selected.Count ?? 0;
                if (selected != int.Parse(arg[9..], CultureInfo.InvariantCulture))
                    throw new InvalidOperationException($"{selected} selected");
                break;
            case "assert":
                string state = _page.Editor?.Document is not { } doc ? "empty" : doc.IsDirty ? "dirty" : "clean";
                if (state != arg)
                    throw new InvalidOperationException($"state is {state}");
                break;
            case "record":
                var recordWords = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var xywh = recordWords[0].Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                var area = Clipmark.Core.Recording.RecordingRegion.Fit(new Clipmark.Core.Capture.PixelRect(xywh[0], xywh[1], xywh[2], xywh[3]),
                    Capture.ScreenCapture.EnumerateMonitors()) ?? throw new InvalidOperationException("region is on no monitor");
                var recorder = await Recording.ScreenRecorder.StartAsync(area, Path.GetFullPath(recordWords[2]),
                    recordWords.Contains("system"), recordWords.Contains("mic"));
                await Task.Delay(int.Parse(recordWords[1], CultureInfo.InvariantCulture));
                await recorder.StopAsync();
                File.WriteAllLines(Path.Combine(_dir, "warnings.txt"), recorder.Warnings);
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

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child))
                yield return d;
        }
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
