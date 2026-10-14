using Clipmark.App.Interop;
using Clipmark.Core.Capture;
using Clipmark.Core.Recording;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Recording;

internal enum RecordBarState { Idle, Countdown, Starting, Recording, Saving }

internal enum RecordBarButton { Record, Microphone, SystemAudio, Discard, Pause }

/// <summary>
/// Small topmost bar next to the recorded region: Record / Stop, Pause / Resume, timer, microphone, system audio, discard.
/// Kept out of the recording (WDA_EXCLUDEFROMCAPTURE) and never activated, so the recorded app keeps the focus.
/// </summary>
internal sealed unsafe partial class RecordBar : Win32Window
{
    private const uint TestClick = WM_APP + 4;
    private const int TimerLeft = 8 + 44 + 44;
    private static readonly uint Fill = Rgb(0x2B, 0x2B, 0x2B), Border = Rgb(0x45, 0x45, 0x45), White = Rgb(0xFF, 0xFF, 0xFF),
        Dim = Rgb(0x6E, 0x6E, 0x6E), Red = Rgb(0xFF, 0x3B, 0x30), Pressed = Rgb(0x48, 0x48, 0x48);

    private readonly PixelRect _monitor, _region;
    private RecordBarState _state;
    private string _timer = "00:00";
    private bool _microphone, _systemAudio, _paused, _microphoneLive, _systemAudioLive;
    private nint _tip;

    public event Action<RecordBarButton>? Clicked;

    public RecordBar(RecordingArea area, bool microphone, bool systemAudio)
        : base(WS_POPUP, WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "Clipmark recording bar")
    {
        (_monitor, _region, _microphone, _systemAudio) = (area.Monitor, area.Region, microphone, systemAudio);
        ExcludeFromCapture(Handle);
        int off = 1;
        DwmSetWindowAttribute(Handle, DWMWA_TRANSITIONS_FORCEDISABLED, &off, sizeof(int));
        // Move onto the region's monitor first so the window takes that monitor's DPI.
        SetWindowPos(Handle, HWND_TOPMOST, _monitor.X, _monitor.Y, 1, 1, SWP_NOACTIVATE);
    }

    public RecordBarState State
    {
        get => _state;
        set { _state = value; UpdateTip(); Repaint(); }
    }

    /// <summary>The recording is paused: Resume glyph, dimmed timer.</summary>
    public bool Paused
    {
        get => _paused;
        set { _paused = value; UpdateTip(); Repaint(); }
    }

    /// <summary>While recording: the microphone is part of the recording, so its button mutes it.</summary>
    public bool MicrophoneLive
    {
        get => _microphoneLive;
        set { _microphoneLive = value; Repaint(); }
    }

    /// <summary>While recording: system audio is part of the recording, so its button mutes it.</summary>
    public bool SystemAudioLive
    {
        get => _systemAudioLive;
        set { _systemAudioLive = value; Repaint(); }
    }

    public string Timer
    {
        get => _timer;
        set { if (_timer != value) { _timer = value; Repaint(); } }
    }

    public bool Microphone
    {
        get => _microphone;
        set { _microphone = value; Repaint(); }
    }

    public bool SystemAudio
    {
        get => _systemAudio;
        set { _systemAudio = value; Repaint(); }
    }

    /// <summary>Keeps the window out of screenshots and recordings (Debug: CLIPMARK_TEST_SHOWBAR=1 keeps it visible for review grabs).</summary>
    internal static void ExcludeFromCapture(nint hwnd)
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("CLIPMARK_TEST_SHOWBAR") == "1")
            return;
#endif
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
    }

    private int Dip(double v) => (int)Math.Round(v * GetDpiForWindow(Handle) / 96.0);

    private int Width => Dip(TimerLeft + 64 + 4 + 40 + 4 + 40 + 4 + 40 + 8);

    private int Height => Dip(52);

    /// <summary>Above the region, else below it, else inside its top edge; horizontally centered on it, within the monitor.</summary>
    public PixelRect Bounds
    {
        get
        {
            int w = Width, h = Height, gap = Dip(8);
            int x = Math.Clamp(_region.X + (_region.Width - w) / 2, _monitor.X, _monitor.Right - w);
            int y = _region.Y - gap - h;
            if (y < _monitor.Y)
                y = _region.Bottom + gap + h <= _monitor.Bottom ? _region.Bottom + gap : _region.Y + gap;
            return new PixelRect(x, y, w, h);
        }
    }

    public void Show()
    {
        var b = Bounds;
        // Clip to the rounded shape so the corners show what lies behind (the system owns the region afterwards).
        SetWindowRgn(Handle, CreateRoundRectRgn(0, 0, b.Width + 1, b.Height + 1, Dip(16), Dip(16)), true);
        SetWindowPos(Handle, HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        UpdateWindow(Handle);
        AddTip();
    }

    /// <summary>System tooltip on the Pause / Resume button (the control subclasses the bar for the mouse messages).</summary>
    private void AddTip()
    {
        if (_tip != 0)
            return;
        var icc = new INITCOMMONCONTROLSEX { dwSize = (uint)sizeof(INITCOMMONCONTROLSEX), dwICC = ICC_BAR_CLASSES };
        InitCommonControlsEx(&icc);
        _tip = CreateWindowEx(WS_EX_TOPMOST | WS_EX_NOACTIVATE, "tooltips_class32", null, WS_POPUP | TTS_ALWAYSTIP | TTS_NOPREFIX,
            0, 0, 0, 0, Handle, 0, GetModuleHandle(null), 0);
        if (_tip == 0)
            return;
        ExcludeFromCapture(_tip);
        SendTip(TTM_ADDTOOLW);
    }

    private void UpdateTip()
    {
        if (_tip != 0)
            SendTip(TTM_SETTOOLINFOW);
    }

    /// <summary>Text and rect of the tool; no text (no tooltip) while the button is disabled.</summary>
    private void SendTip(uint message)
    {
        fixed (char* text = _state != RecordBarState.Recording ? "" : Loc.Get(_paused ? "RecordResume" : "RecordPause"))
        {
            var info = new TTTOOLINFOW
            {
                cbSize = TipInfoSize, uFlags = TTF_SUBCLASS, hwnd = Handle, uId = 1,
                rect = ButtonRect(RecordBarButton.Pause), lpszText = (nint)text,
            };
            SendMessage(_tip, message, 0, (nint)(&info));
        }
    }

#if DEBUG
    /// <summary>Test scripts: the Pause / Resume tooltip text as the control holds it, or null without the control.</summary>
    internal string? TestTip()
    {
        if (_tip == 0)
            return null;
        var buffer = new char[80];
        fixed (char* text = buffer)
        {
            var info = new TTTOOLINFOW { cbSize = TipInfoSize, hwnd = Handle, uId = 1, lpszText = (nint)text };
            SendMessage(_tip, 0x0400 + 56, buffer.Length, (nint)(&info)); // TTM_GETTEXTW
        }
        return new string(buffer).TrimEnd('\0');
    }
#endif

    private const uint ICC_BAR_CLASSES = 0x4, TTS_ALWAYSTIP = 0x1, TTS_NOPREFIX = 0x2, TTF_SUBCLASS = 0x10,
        TTM_ADDTOOLW = 0x0400 + 50, TTM_SETTOOLINFOW = 0x0400 + 54;

    private struct INITCOMMONCONTROLSEX { public uint dwSize, dwICC; }

    // TTTOOLINFOW_V2_SIZE (no lpReserved): comctl32 5 (no v6 manifest) rejects the full size, version 6 takes both.
    private static readonly uint TipInfoSize = (uint)(sizeof(TTTOOLINFOW) - sizeof(nint));

    private struct TTTOOLINFOW
    {
        public uint cbSize, uFlags;
        public nint hwnd;
        public nuint uId;
        public RECT rect;
        public nint hinst, lpszText, lParam, lpReserved;
    }

    [System.Runtime.InteropServices.LibraryImport("comctl32.dll")]
    private static partial int InitCommonControlsEx(INITCOMMONCONTROLSEX* icc);

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    private void Repaint() => InvalidateRect(Handle, null, false);

    private RECT ButtonRect(RecordBarButton button)
    {
        int left = button switch
        {
            RecordBarButton.Record => 8,
            RecordBarButton.Pause => 8 + 44,
            RecordBarButton.Microphone => TimerLeft + 64 + 4,
            RecordBarButton.SystemAudio => TimerLeft + 64 + 4 + 44,
            _ => TimerLeft + 64 + 4 + 88,
        };
        return new RECT { Left = Dip(left), Top = Dip(6), Right = Dip(left + 40), Bottom = Dip(46) };
    }

    private bool Enabled(RecordBarButton button) => button switch
    {
        RecordBarButton.Record => _state is RecordBarState.Idle or RecordBarState.Countdown or RecordBarState.Recording,
        RecordBarButton.Discard => _state is not (RecordBarState.Starting or RecordBarState.Saving),
        RecordBarButton.Pause => _state is RecordBarState.Recording,
        // Before recording they toggle the setting; while recording they mute a source that is recording.
        RecordBarButton.Microphone => _state is RecordBarState.Idle or RecordBarState.Countdown || _state is RecordBarState.Recording && _microphoneLive,
        _ => _state is RecordBarState.Idle or RecordBarState.Countdown || _state is RecordBarState.Recording && _systemAudioLive,
    };

    private RecordBarButton? HitTest(int x, int y)
    {
        foreach (var button in Enum.GetValues<RecordBarButton>())
        {
            var r = ButtonRect(button);
            if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom)
                return button;
        }
        return null;
    }

    private RecordBarButton? _pressed;

    protected override nint? OnMessage(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                return 1;
            case WM_PAINT:
                Paint();
                return 0;
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;
            case WM_LBUTTONDOWN:
                _pressed = HitTest((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
                SetCapture(Handle);
                Repaint();
                return 0;
            case WM_LBUTTONUP:
                ReleaseCapture();
                var released = HitTest((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
                var pressed = _pressed;
                _pressed = null;
                Repaint();
                if (released is { } b && b == pressed)
                    Click(b);
                return 0;
#if DEBUG
            // Test scripts: WM_APP + 4 with the button index in wParam.
            case TestClick:
                Click((RecordBarButton)wParam);
                return 0;
#endif
        }
        return null;
    }

    private void Click(RecordBarButton button)
    {
        if (Enabled(button))
            Clicked?.Invoke(button);
    }

    private void Paint()
    {
        PAINTSTRUCT ps;
        nint dc = BeginPaint(Handle, &ps);
        nint back = CreateCompatibleDC(dc), bitmap = CreateCompatibleBitmap(dc, Width, Height), old = SelectObject(back, bitmap);
        try
        {
            var all = new RECT { Left = 0, Top = 0, Right = Width, Bottom = Height };
            FillBox(back, all, Fill, Border, Dip(8));
            foreach (var button in Enum.GetValues<RecordBarButton>())
                PaintButton(back, button);
            var timer = new RECT { Left = Dip(TimerLeft), Top = Dip(6), Right = Dip(TimerLeft + 64), Bottom = Dip(46) };
            string text = _state switch { RecordBarState.Starting => "…", RecordBarState.Saving => Loc.Get("RecordBarSaving"), _ => _timer };
            uint timerColor = _state != RecordBarState.Recording ? White : _paused ? Dim : Red;
            Text(back, timer, text, "Segoe UI", Dip(_state == RecordBarState.Countdown ? 20 : 14), timerColor);
            BitBlt(dc, 0, 0, Width, Height, back, 0, 0, SRCCOPY);
        }
        finally
        {
            SelectObject(back, old);
            DeleteObject(bitmap);
            DeleteDC(back);
            EndPaint(Handle, &ps);
        }
    }

    private void PaintButton(nint dc, RecordBarButton button)
    {
        var r = ButtonRect(button);
        bool enabled = Enabled(button);
        if (_pressed == button && enabled)
            FillBox(dc, r, Pressed, Pressed, Dip(4));
        uint color = enabled ? White : Dim;
        switch (button)
        {
            case RecordBarButton.Record when _state is RecordBarState.Recording or RecordBarState.Countdown:
                int half = Dip(7);
                int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
                FillBox(dc, new RECT { Left = cx - half, Top = cy - half, Right = cx + half, Bottom = cy + half }, color, color, Dip(2));
                break;
            case RecordBarButton.Record:
                int rad = Dip(8);
                FillCircle(dc, (r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2, rad, enabled ? Red : Dim);
                break;
            case RecordBarButton.Microphone:
                Text(dc, r, _microphone ? "" : "", "Segoe Fluent Icons", Dip(16), color);
                break;
            case RecordBarButton.SystemAudio:
                Text(dc, r, _systemAudio ? "" : "", "Segoe Fluent Icons", Dip(16), color);
                break;
            case RecordBarButton.Pause:
                Text(dc, r, _paused ? "" : "", "Segoe Fluent Icons", Dip(16), color);
                break;
            case RecordBarButton.Discard:
                Text(dc, r, "", "Segoe Fluent Icons", Dip(16), color);
                break;
        }
    }

    private static void FillBox(nint dc, RECT r, uint fill, uint border, int radius)
    {
        nint brush = CreateSolidBrush(fill), pen = CreatePen(PS_SOLID, 1, border);
        nint ob = SelectObject(dc, brush), op = SelectObject(dc, pen);
        RoundRect(dc, r.Left, r.Top, r.Right, r.Bottom, radius * 2, radius * 2);
        SelectObject(dc, ob);
        SelectObject(dc, op);
        DeleteObject(brush);
        DeleteObject(pen);
    }

    private static nint _gdiplus;

    /// <summary>Anti-aliased filled circle (GDI+); color is a GDI COLORREF.</summary>
    private static void FillCircle(nint dc, int cx, int cy, int radius, uint color)
    {
        if (_gdiplus == 0)
        {
            var input = new GdiplusStartupInput { Version = 1 };
            GdiplusStartup(out _gdiplus, &input, 0);
        }
        uint argb = 0xFF000000 | (color & 0xFF) << 16 | (color & 0xFF00) | (color >> 16 & 0xFF);
        GdipCreateFromHDC(dc, out nint graphics);
        GdipSetSmoothingMode(graphics, SmoothingModeAntiAlias);
        GdipCreateSolidFill(argb, out nint brush);
        GdipFillEllipse(graphics, brush, cx - radius, cy - radius, 2 * radius, 2 * radius);
        GdipDeleteBrush(brush);
        GdipDeleteGraphics(graphics);
    }

    private static void Text(nint dc, RECT r, string text, string face, int pixelHeight, uint color)
    {
        nint font = CreateFont(-pixelHeight, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, face);
        nint old = SelectObject(dc, font);
        var need = new RECT();
        DrawText(dc, text, text.Length, &need, DT_CALCRECT | DT_SINGLELINE | DT_NOPREFIX);
        if (need.Right > r.Right - r.Left)
        {
            // A long translation shrinks to fit the fixed slot (down to 60%) instead of being clipped.
            int fit = Math.Max(pixelHeight * 3 / 5, pixelHeight * (r.Right - r.Left) / need.Right);
            SelectObject(dc, old);
            DeleteObject(font);
            font = CreateFont(-fit, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, face);
            old = SelectObject(dc, font);
        }
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, color);
        DrawText(dc, text, text.Length, &r, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
        SelectObject(dc, old);
        DeleteObject(font);
    }
}

/// <summary>Thin red frame just outside the recorded region: four click-through windows kept out of the recording.</summary>
internal sealed class RegionOutline : IDisposable
{
    private readonly List<Edge> _edges = [];

    public RegionOutline(PixelRect region)
    {
        // One edge window first to learn the monitor's DPI for the thickness.
        var probe = new Edge();
        SetWindowPos(probe.Handle, HWND_TOPMOST, region.X, region.Y, 1, 1, SWP_NOACTIVATE);
        uint dpi = GetDpiForWindow(probe.Handle);
        probe.Dispose();
        int t = Math.Max(2, (int)Math.Round(2 * dpi / 96.0));
        PixelRect[] parts =
        [
            new(region.X - t, region.Y - t, region.Width + 2 * t, t), new(region.X - t, region.Bottom, region.Width + 2 * t, t),
            new(region.X - t, region.Y, t, region.Height), new(region.Right, region.Y, t, region.Height),
        ];
        foreach (var p in parts)
        {
            var edge = new Edge();
            SetWindowPos(edge.Handle, HWND_TOPMOST, p.X, p.Y, p.Width, p.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            _edges.Add(edge);
        }
    }

    public void Dispose()
    {
        foreach (var e in _edges)
            e.Dispose();
        _edges.Clear();
    }

    private sealed unsafe class Edge : Win32Window
    {
        public Edge() : base(WS_POPUP, WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT, "Clipmark recording outline")
        {
            SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);
            RecordBar.ExcludeFromCapture(Handle);
        }

        protected override nint? OnMessage(uint msg, nint wParam, nint lParam)
        {
            if (msg == WM_ERASEBKGND)
                return 1;
            if (msg != WM_PAINT)
                return null;
            PAINTSTRUCT ps;
            nint dc = BeginPaint(Handle, &ps);
            nint brush = CreateSolidBrush(Rgb(0xFF, 0x3B, 0x30));
            FillRect(dc, &ps.rcPaint, brush);
            DeleteObject(brush);
            EndPaint(Handle, &ps);
            return 0;
        }
    }
}
