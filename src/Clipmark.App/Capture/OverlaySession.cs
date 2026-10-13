using Clipmark.App.Interop;
using Clipmark.Core.Capture;
using Clipmark.Core.Settings;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Capture;

/// <summary>
/// Shows the frozen frame dimmed on every monitor and lets the user pick a region.
/// One GDI window per monitor; all coordinates are physical desktop pixels.
/// </summary>
internal sealed unsafe class OverlaySession : IDisposable
{
    private static readonly (CaptureMode Mode, char Glyph, string Name)[] PillButtons =
    [
        (CaptureMode.Rectangle, '\uF407', Loc.Get("ModeRectangle")), (CaptureMode.Window, '\uE737', Loc.Get("ModeWindow")),
        (CaptureMode.FullScreen, '\uE7F4', Loc.Get("ModeFullScreen")), (CaptureMode.AllMonitors, '\uEBC6', Loc.Get("ModeAllMonitors")),
        (CaptureMode.Freeform, '\uF408', Loc.Get("ModeFreeform")),
    ];
    private const int CloseButton = -1, ExtractButton = -2, PhotoButton = -3, VideoButton = -4;

    private readonly DesktopFrame _frame;
    private readonly TaskCompletionSource<CapturedImage?> _done = new();
    private readonly List<OverlayWindow> _windows = [];
    private readonly nint _brightDc, _dimDc, _dimBitmap, _dimOld, _brightOld;
    private readonly OverlayWindow _pillHost;

    private CaptureMode _mode;
    private PixelPoint? _dragStart;
    private PixelPoint _cursor;
    private List<PixelPoint>? _path;
    private PixelRect? _decorated; // desktop area that currently shows highlight, border or label
    private int? _hoverButton;
    private bool _extract, _video;
    private PixelRect? _picked;

    public CaptureMode Mode => _mode;

    /// <summary>Text extractor toggled on in the pill: the picked region goes to OCR and the clipboard, not to the editor.</summary>
    public bool ExtractText => _extract;

    /// <summary>Video chosen in the pill: the session picks a region to record and completes without an image.</summary>
    public bool Video => _video;

    /// <summary>The region picked in Video mode (desktop pixels), or null when cancelled.</summary>
    public PixelRect? PickedRegion => _picked;

    public OverlaySession(DesktopFrame frame, CaptureMode mode, bool video = false)
    {
        _frame = frame;
        _mode = mode;
        if (video)
            SetVideo(true);
        var b = frame.Screen.Bounds;

        nint screenDc = GetDC(0);
        _brightDc = CreateCompatibleDC(screenDc);
        _dimDc = CreateCompatibleDC(screenDc);
        var header = new BITMAPINFOHEADER { biSize = sizeof(BITMAPINFOHEADER), biWidth = b.Width, biHeight = -b.Height, biPlanes = 1, biBitCount = 32 };
        void* dimBits;
        _dimBitmap = CreateDIBSection(screenDc, &header, 0, &dimBits, 0, 0);
        ReleaseDC(0, screenDc);
        if (_dimBitmap == 0)
            throw new InvalidOperationException("CreateDIBSection failed for the dimmed frame.");
        Dim(frame.Bits, (byte*)dimBits, (long)frame.Stride * b.Height);
        _brightOld = SelectObject(_brightDc, frame.Bitmap);
        _dimOld = SelectObject(_dimDc, _dimBitmap);

        GetCursorPos(out var p);
        _cursor = new PixelPoint(p.X, p.Y);
        var pillMonitor = ScreenPick.MonitorAt(frame.Monitors, _cursor) ?? frame.Monitors[0];
        foreach (var m in frame.Monitors)
        {
            var w = new OverlayWindow(this, m);
            _windows.Add(w);
            if (m == pillMonitor)
                _pillHost = w;
        }
        _pillHost ??= _windows[0];
    }

    /// <summary>Shows the overlay; completes with the picked image, or null when cancelled.</summary>
    public Task<CapturedImage?> RunAsync()
    {
        foreach (var w in _windows)
            w.Show();
        SetForegroundWindow(_pillHost.Handle);
        SetFocus(_pillHost.Handle);
        return _done.Task;
    }

    private static void Dim(byte* src, byte* dst, long count)
    {
        // ~50 % towards black, like the Windows capture overlay.
        for (long i = 0; i < count; i++)
            dst[i] = (byte)(src[i] >> 1);
    }

    // ---- state changes -------------------------------------------------------------------

    internal void OnMouseMove(PixelPoint p)
    {
        _cursor = p;
        if (_path is not null && _path[^1] != p)
            _path.Add(p);
        SetHover(PillHit(p));
        Redecorate();
    }

    internal void OnLeftDown(OverlayWindow source, PixelPoint p)
    {
        _cursor = p;
        if (PillHit(p) is { } button)
        {
            if (!Enabled(button))
                return;
            if (button == CloseButton)
                Finish(null);
            else if (button is PhotoButton or VideoButton)
            {
                SetVideo(button == VideoButton);
                InvalidatePill();
                Redecorate();
            }
            else if (button == ExtractButton)
            {
                _extract = !_extract;
                InvalidatePill();
            }
            else if ((CaptureMode)button == CaptureMode.AllMonitors)
                Finish(_frame.Cut(_frame.Screen.Bounds));
            else
            {
                _mode = (CaptureMode)button;
                InvalidatePill();
                Redecorate();
            }
            return;
        }
        switch (_mode)
        {
            case CaptureMode.Rectangle:
                _dragStart = p;
                SetCapture(source.Handle);
                break;
            case CaptureMode.Freeform:
                _path = [p];
                SetCapture(source.Handle);
                break;
        }
    }

    internal void OnLeftUp(PixelPoint p)
    {
        _cursor = p;
        var bounds = _frame.Screen.Bounds;
        switch (_mode)
        {
            case CaptureMode.Rectangle when _dragStart is { } start:
                _dragStart = null;
                ReleaseCapture();
                if (Selection.FromDrag(start, p, bounds) is { } rect)
                    Pick(rect);
                else
                    Redecorate(); // click without drag: keep the overlay open
                break;
            case CaptureMode.Freeform when _path is { } path:
                _path = null;
                ReleaseCapture();
                if (FreeformMask.Create(path, bounds) is { } mask)
                    Finish(_frame.Cut(mask));
                else
                    Redecorate();
                break;
            case CaptureMode.Window or CaptureMode.FullScreen:
                if (Highlight() is { } picked)
                    Pick(picked);
                break;
        }
    }

    internal void CancelDrag()
    {
        if (_dragStart is null && _path is null)
            return;
        _dragStart = null;
        _path = null;
        ReleaseCapture();
        Redecorate();
    }

    internal void Cancel() => Finish(null);

    /// <summary>Video: freeform, all monitors and the text extractor are photo-only.</summary>
    private void SetVideo(bool video)
    {
        _video = video;
        if (video)
        {
            _extract = false;
            if (_mode is CaptureMode.Freeform or CaptureMode.AllMonitors)
                _mode = CaptureMode.Rectangle;
        }
    }

    private bool Enabled(int button) => !_video || button is not (ExtractButton or (int)CaptureMode.Freeform or (int)CaptureMode.AllMonitors);

    private void Pick(PixelRect rect)
    {
        if (_video)
        {
            _picked = rect;
            Finish(null);
        }
        else
            Finish(_frame.Cut(rect));
    }

    private void Finish(CapturedImage? result)
    {
        if (_done.Task.IsCompleted)
            return;
        Dispose();
        _done.SetResult(result);
    }

    // ---- what to draw ----------------------------------------------------------------------

    /// <summary>Area shown undimmed: drag rect, hovered window or hovered monitor.</summary>
    private PixelRect? Highlight()
    {
        if (PillHit(_cursor) is not null)
            return null;
        var bounds = _frame.Screen.Bounds;
        return _mode switch
        {
            CaptureMode.Rectangle when _dragStart is { } s => Selection.FromDrag(s, _cursor, bounds),
            CaptureMode.Window => ScreenPick.WindowAt(_frame.Windows, _cursor, bounds) ?? ScreenPick.MonitorAt(_frame.Monitors, _cursor),
            CaptureMode.FullScreen => ScreenPick.MonitorAt(_frame.Monitors, _cursor),
            _ => null,
        };
    }

    private string? SizeLabel(PixelRect r) => _mode == CaptureMode.Rectangle ? Loc.Format("SelectionSize", r.Width, r.Height) : null;

    /// <summary>Desktop area touched by highlight, border, size label and freeform path.</summary>
    private PixelRect? DecorationBounds()
    {
        PixelRect? area = null;
        if (Highlight() is { } h)
        {
            area = Inflate(h, Dip(4)); // border
            if (SizeLabel(h) is not null)
                area = Union(area, LabelRect(h));
        }
        if (_path is { Count: > 0 } path)
        {
            var box = PixelRect.FromCorners(path.Min(q => q.X), path.Min(q => q.Y), path.Max(q => q.X) + 1, path.Max(q => q.Y) + 1);
            area = Union(area, Inflate(box, Dip(4)));
        }
        return area;
    }

    private void Redecorate()
    {
        var next = DecorationBounds();
        if (next == _decorated && _path is null)
            return;
        Invalidate(_decorated);
        Invalidate(next);
        _decorated = next;
    }

    private void Invalidate(PixelRect? desktopRect)
    {
        if (desktopRect is not { } r)
            return;
        foreach (var w in _windows)
            w.Invalidate(r);
    }

    private static PixelRect Inflate(PixelRect r, int by) => new(r.X - by, r.Y - by, r.Width + 2 * by, r.Height + 2 * by);

    private static PixelRect? Union(PixelRect? a, PixelRect b) => a is not { } x ? b
        : PixelRect.FromCorners(Math.Min(x.X, b.X), Math.Min(x.Y, b.Y), Math.Max(x.Right, b.Right), Math.Max(x.Bottom, b.Bottom));

    // ---- pill -----------------------------------------------------------------------------

    private double PillScale => GetDpiForWindow(_pillHost.Handle) / 96.0;

    private int Dip(double v) => (int)Math.Round(v * PillScale);

    // Pill order: Photo, Video, a separator, the modes, a separator, Text extractor, Close.
    private const int ButtonCount = 9, FirstMode = 2, AfterModes = FirstMode + 5;

    private static int IdAt(int index) => index switch
    {
        0 => PhotoButton,
        1 => VideoButton,
        < AfterModes => (int)PillButtons[index - FirstMode].Mode,
        AfterModes => ExtractButton,
        _ => CloseButton,
    };

    private static int IndexOf(int id) => id switch
    {
        PhotoButton => 0,
        VideoButton => 1,
        ExtractButton => AfterModes,
        CloseButton => AfterModes + 1,
        _ => FirstMode + Array.FindIndex(PillButtons, p => (int)p.Mode == id),
    };

    private static (char Glyph, string Name) Face(int id) => id switch
    {
        CloseButton => ('', Loc.Get("PillClose")),
        ExtractButton => ('', Loc.Get("PillTextExtractor")),
        PhotoButton => ('\uE722', Loc.Get("PillPhoto")),
        VideoButton => ('\uE714', Loc.Get("PillVideo")),
        _ => (PillButtons[IndexOf(id) - FirstMode].Glyph, PillButtons[IndexOf(id) - FirstMode].Name),
    };

    /// <summary>Pill rect in desktop pixels, top-center on its monitor.</summary>
    private PixelRect PillRect()
    {
        int w = Dip(8 + ButtonCount * 44 + 18), h = Dip(52);
        var m = _pillHost.Monitor;
        return new PixelRect(m.X + (m.Width - w) / 2, m.Y + Dip(24), w, h);
    }

    private PixelRect ButtonRect(int index)
    {
        var pill = PillRect();
        int x = pill.X + Dip(8 + index * 44) + (index >= FirstMode ? Dip(9) : 0) + (index >= AfterModes ? Dip(9) : 0);
        return new PixelRect(x, pill.Y + Dip(6), Dip(40), Dip(40));
    }

    private int? PillHit(PixelPoint p)
    {
        if (!PillRect().Contains(p))
            return null;
        for (int i = 0; i < ButtonCount; i++)
            if (ButtonRect(i).Contains(p))
                return IdAt(i);
        return null;
    }

    internal bool IsOverPill(PixelPoint p) => PillRect().Contains(p);

    private void SetHover(int? button)
    {
        if (button == _hoverButton)
            return;
        _hoverButton = button;
        InvalidatePill();
    }

    private void InvalidatePill()
    {
        var pill = PillRect();
        Invalidate(new PixelRect(pill.X - Dip(160), pill.Y, pill.Width + Dip(320), pill.Height + Dip(40))); // includes a long translated tooltip
    }

    // ---- painting -------------------------------------------------------------------------

    private static readonly uint PillFill = Rgb(0x2B, 0x2B, 0x2B), PillBorder = Rgb(0x45, 0x45, 0x45),
        ButtonHover = Rgb(0x3A, 0x3A, 0x3A), ButtonSelected = Rgb(0x48, 0x48, 0x48), Accent = Rgb(0xFF, 0x3B, 0x30),
        White = Rgb(0xFF, 0xFF, 0xFF), LabelFill = Rgb(0x20, 0x20, 0x20), Disabled = Rgb(0x6E, 0x6E, 0x6E);

    /// <summary>Composes one window's part of the overlay into <paramref name="dc"/> (window-local coordinates).</summary>
    internal void Paint(OverlayWindow w, nint dc, RECT dirty)
    {
        var m = w.Monitor;
        var s = _frame.Screen.Bounds;
        int ox = m.X - s.X, oy = m.Y - s.Y; // monitor origin inside the frame bitmap

        BitBlt(dc, dirty.Left, dirty.Top, dirty.Right - dirty.Left, dirty.Bottom - dirty.Top, _dimDc, ox + dirty.Left, oy + dirty.Top, SRCCOPY);

        if (Highlight() is { } h && h.Intersect(m) is { } part)
        {
            BitBlt(dc, part.X - m.X, part.Y - m.Y, part.Width, part.Height, _brightDc, part.X - s.X, part.Y - s.Y, SRCCOPY);
            int t = Math.Max(1, Dip(1.5));
            DrawFrame(dc, h.X - m.X - t, h.Y - m.Y - t, h.Right - m.X + t, h.Bottom - m.Y + t, White, t);
            if (SizeLabel(h) is { } label)
                DrawLabel(w, dc, LabelRect(h), label);
        }

        if (_path is { Count: > 1 } path)
        {
            var points = new POINT[path.Count];
            for (int i = 0; i < path.Count; i++)
                points[i] = new POINT { X = path[i].X - m.X, Y = path[i].Y - m.Y };
            nint pen = CreatePen(PS_SOLID, Math.Max(2, Dip(2)), Accent);
            nint old = SelectObject(dc, pen);
            fixed (POINT* pts = points)
                Polyline(dc, pts, points.Length);
            SelectObject(dc, old);
            DeleteObject(pen);
        }

        if (w == _pillHost)
            PaintPill(dc, m);
    }

    private PixelRect LabelRect(PixelRect h)
    {
        int lw = Dip(96), lh = Dip(24);
        int y = h.Bottom + Dip(8);
        var monitor = ScreenPick.MonitorAt(_frame.Monitors, new PixelPoint(h.X, Math.Min(h.Bottom, _frame.Screen.Bounds.Bottom - 1)))
            ?? _frame.Monitors[0];
        if (y + lh > monitor.Bottom)
            y = h.Bottom - lh - Dip(8); // no room below: inside the selection
        return new PixelRect(h.X, y, lw, lh);
    }

    private void DrawLabel(OverlayWindow w, nint dc, PixelRect r, string text)
    {
        var m = w.Monitor;
        var local = new RECT { Left = r.X - m.X, Top = r.Y - m.Y, Right = r.Right - m.X, Bottom = r.Bottom - m.Y };
        FillRounded(dc, local, LabelFill, LabelFill, Dip(4));
        DrawCenteredText(dc, local, text, "Segoe UI", Dip(13), White);
    }

    private void PaintPill(nint dc, PixelRect m)
    {
        var pill = PillRect();
        var local = new RECT { Left = pill.X - m.X, Top = pill.Y - m.Y, Right = pill.Right - m.X, Bottom = pill.Bottom - m.Y };
        FillRounded(dc, local, PillFill, PillBorder, Dip(8));

        for (int i = 0; i < ButtonCount; i++)
        {
            var b = ButtonRect(i);
            var br = new RECT { Left = b.X - m.X, Top = b.Y - m.Y, Right = b.Right - m.X, Bottom = b.Bottom - m.Y };
            int id = IdAt(i);
            bool selected = id switch
            {
                ExtractButton => _extract,
                PhotoButton => !_video,
                VideoButton => _video,
                CloseButton => false,
                _ => (CaptureMode)id == _mode,
            };
            bool enabled = Enabled(id);
            if (selected || (enabled && _hoverButton == id))
                FillRounded(dc, br, selected ? ButtonSelected : ButtonHover, selected ? ButtonSelected : ButtonHover, Dip(4));
            if (selected)
            {
                var bar = new RECT { Left = br.Left + Dip(14), Top = br.Bottom - Dip(3), Right = br.Right - Dip(14), Bottom = br.Bottom - Dip(1) };
                FillRounded(dc, bar, Accent, Accent, Dip(2));
            }
            DrawCenteredText(dc, br, Face(id).Glyph.ToString(), "Segoe Fluent Icons", Dip(16), enabled ? White : Disabled);
        }

        // Separators after Photo / Video and before Text extractor and Close.
        foreach (int index in new[] { FirstMode, AfterModes })
        {
            var sep = ButtonRect(index);
            var line = new RECT { Left = sep.X - Dip(5) - m.X, Top = sep.Y + Dip(8) - m.Y, Right = sep.X - Dip(4) - m.X, Bottom = sep.Bottom - Dip(8) - m.Y };
            FillRounded(dc, line, PillBorder, PillBorder, 0);
        }

        if (_hoverButton is { } hover)
        {
            string name = Face(hover).Name;
            var b = ButtonRect(IndexOf(hover));
            int tw = TextWidth(dc, name, "Segoe UI", Dip(12)) + Dip(16), th = Dip(26);
            var tip = new RECT { Left = b.X + b.Width / 2 - tw / 2 - m.X, Top = pill.Bottom + Dip(6) - m.Y };
            tip.Right = tip.Left + tw;
            tip.Bottom = tip.Top + th;
            FillRounded(dc, tip, PillFill, PillBorder, Dip(4));
            DrawCenteredText(dc, tip, name, "Segoe UI", Dip(12), White);
        }
    }

    private static void FillRounded(nint dc, RECT r, uint fill, uint border, int radius)
    {
        nint brush = CreateSolidBrush(fill), pen = CreatePen(PS_SOLID, 1, border);
        nint oldBrush = SelectObject(dc, brush), oldPen = SelectObject(dc, pen);
        if (radius > 0)
            RoundRect(dc, r.Left, r.Top, r.Right, r.Bottom, radius * 2, radius * 2);
        else
            Rectangle(dc, r.Left, r.Top, r.Right, r.Bottom);
        SelectObject(dc, oldBrush);
        SelectObject(dc, oldPen);
        DeleteObject(brush);
        DeleteObject(pen);
    }

    private static void DrawFrame(nint dc, int left, int top, int right, int bottom, uint color, int thickness)
    {
        nint pen = CreatePen(PS_SOLID, thickness, color);
        nint oldPen = SelectObject(dc, pen), oldBrush = SelectObject(dc, GetStockObject(NULL_BRUSH));
        // GDI centers the pen on the outline; shift by half the width so it sits outside the selection.
        int half = thickness / 2;
        Rectangle(dc, left + half, top + half, right - half + 1, bottom - half + 1);
        SelectObject(dc, oldPen);
        SelectObject(dc, oldBrush);
        DeleteObject(pen);
    }

    private static int TextWidth(nint dc, string text, string face, int pixelHeight)
    {
        nint font = CreateFont(-pixelHeight, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5 /* CLEARTYPE_QUALITY */, 0, face);
        nint old = SelectObject(dc, font);
        var r = new RECT();
        DrawText(dc, text, text.Length, &r, DT_CALCRECT | DT_SINGLELINE | DT_NOPREFIX);
        SelectObject(dc, old);
        DeleteObject(font);
        return r.Right - r.Left;
    }

    private static void DrawCenteredText(nint dc, RECT r, string text, string face, int pixelHeight, uint color)
    {
        nint font = CreateFont(-pixelHeight, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5 /* CLEARTYPE_QUALITY */, 0, face);
        nint old = SelectObject(dc, font);
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, color);
        DrawText(dc, text, text.Length, &r, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
        SelectObject(dc, old);
        DeleteObject(font);
    }

    public void Dispose()
    {
        foreach (var w in _windows)
            w.Dispose();
        _windows.Clear();
        if (_brightDc != 0)
        {
            SelectObject(_brightDc, _brightOld);
            SelectObject(_dimDc, _dimOld);
            DeleteDC(_brightDc);
            DeleteDC(_dimDc);
            DeleteObject(_dimBitmap);
        }
    }
}

/// <summary>Borderless topmost window covering one monitor; paints via a back buffer.</summary>
internal sealed unsafe class OverlayWindow : Win32Window
{
    private const uint TestMove = 0x8001, TestDown = 0x8002, TestUp = 0x8003;
    private readonly OverlaySession _session;
    private nint _backDc, _backBitmap, _backOld;

    public PixelRect Monitor { get; }

    public OverlayWindow(OverlaySession session, PixelRect monitor)
        : base(WS_POPUP, WS_EX_TOPMOST | WS_EX_TOOLWINDOW, "Clipmark capture")
    {
        _session = session;
        Monitor = monitor;
        int off = 1;
        DwmSetWindowAttribute(Handle, DWMWA_TRANSITIONS_FORCEDISABLED, &off, sizeof(int));
    }

    public void Show()
    {
        SetWindowPos(Handle, HWND_TOPMOST, Monitor.X, Monitor.Y, Monitor.Width, Monitor.Height, SWP_SHOWWINDOW);
        UpdateWindow(Handle);
    }

    public void Invalidate(PixelRect desktopRect)
    {
        if (desktopRect.Intersect(Monitor) is not { } r)
            return;
        var local = new RECT { Left = r.X - Monitor.X, Top = r.Y - Monitor.Y, Right = r.Right - Monitor.X, Bottom = r.Bottom - Monitor.Y };
        InvalidateRect(Handle, &local, false);
    }

    private static PixelPoint Cursor()
    {
        GetCursorPos(out var p);
        return new PixelPoint(p.X, p.Y);
    }

    protected override nint? OnMessage(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                return 1;
            case WM_PAINT:
                Paint();
                return 0;
            case WM_SETCURSOR:
                SetCursor(LoadCursor(0, _session.IsOverPill(Cursor()) ? IDC_ARROW : IDC_CROSS));
                return 1;
            case WM_MOUSEMOVE:
                _session.OnMouseMove(Cursor());
                return 0;
            case WM_LBUTTONDOWN:
                _session.OnLeftDown(this, Cursor());
                return 0;
            case WM_LBUTTONUP:
                _session.OnLeftUp(Cursor());
                return 0;
            case WM_RBUTTONUP:
                _session.CancelDrag();
                return 0;
            case WM_CAPTURECHANGED:
                if (lParam != Handle)
                    _session.CancelDrag();
                return 0;
            case WM_KEYDOWN when wParam == VK_ESCAPE:
                _session.Cancel();
                return 0;
            case WM_CLOSE:
                _session.Cancel();
                return 0;
#if DEBUG
            // Test scripts: WM_APP + 1 / 2 / 3 = move / left down / left up at the desktop point in lParam (x low, y high word).
            case >= TestMove and <= TestUp:
                var at = new PixelPoint((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
                if (msg == TestMove) _session.OnMouseMove(at);
                else if (msg == TestDown) _session.OnLeftDown(this, at);
                else _session.OnLeftUp(at);
                return 0;
#endif
        }
        return null;
    }

    private void Paint()
    {
        PAINTSTRUCT ps;
        nint dc = BeginPaint(Handle, &ps);
        try
        {
            if (_backDc == 0)
            {
                _backDc = CreateCompatibleDC(dc);
                _backBitmap = CreateCompatibleBitmap(dc, Monitor.Width, Monitor.Height);
                _backOld = SelectObject(_backDc, _backBitmap);
            }
            var r = ps.rcPaint;
            _session.Paint(this, _backDc, r);
            BitBlt(dc, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, _backDc, r.Left, r.Top, SRCCOPY);
        }
        finally
        {
            EndPaint(Handle, &ps);
        }
    }

    public override void Dispose()
    {
        if (_backDc != 0)
        {
            SelectObject(_backDc, _backOld);
            DeleteObject(_backBitmap);
            DeleteDC(_backDc);
            _backDc = 0;
        }
        base.Dispose();
    }
}
