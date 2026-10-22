using Clipmark.App.Interop;
using Clipmark.Core.Settings;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Shell;

/// <summary>Hidden window that owns the tray icon and the global hotkey.</summary>
internal sealed unsafe class TrayHost : Win32Window
{
    public const int CaptureHotkeyId = 1, RecordHotkeyId = 2;
    private const uint TrayId = 1, TrayCallback = WM_APP + 1;

    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private nint _icon;

    public event Action? CaptureRequested;
    public event Action? RecordRequested;

    /// <summary>Left click on the icon; the app starts a capture or stops a running recording.</summary>
    public event Action? IconClicked;
    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action<CaptureMode>? ModeChosen;
    public event Action<int>? DelayChosen;

    /// <summary>Supplies the current mode and delay for the menu check marks.</summary>
    public Func<ClipmarkSettings>? Settings { get; set; }

    public TrayHost() : base(WS_POPUP, WS_EX_TOOLWINDOW, "Clipmark tray")
    {
        AddIcon();
    }

    public bool TryRegisterHotkey(int id, Hotkey hotkey)
    {
        UnregisterHotKey(Handle, id);
        return RegisterHotKey(Handle, id, (uint)hotkey.Modifiers | MOD_NOREPEAT, (uint)hotkey.Key);
    }

    private void AddIcon()
    {
        uint dpi = GetDpiForWindow(Handle);
        if (_icon == 0)
            _icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), IMAGE_ICON,
                GetSystemMetricsForDpi(SM_CXSMICON, dpi), GetSystemMetricsForDpi(SM_CYSMICON, dpi), LR_LOADFROMFILE);
        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.uCallbackMessage = TrayCallback;
        data.hIcon = _icon;
        "Clipmark".AsSpan().CopyTo(new Span<char>(data.szTip, 127));
        Shell_NotifyIcon(NIM_ADD, &data);
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, &data);
    }

    private NOTIFYICONDATAW NewData() => new() { cbSize = sizeof(NOTIFYICONDATAW), hWnd = Handle, uID = TrayId };

    protected override nint? OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_HOTKEY && wParam is CaptureHotkeyId or RecordHotkeyId)
        {
            (wParam == CaptureHotkeyId ? CaptureRequested : RecordRequested)?.Invoke();
            return 0;
        }
        if (msg == _taskbarCreated)
        {
            AddIcon(); // Explorer restarted and lost the icon.
            return 0;
        }
        if (msg != TrayCallback)
            return null;

        // NOTIFYICON_VERSION_4: LOWORD(lParam) = event, wParam = anchor point in screen coordinates.
        switch ((uint)(lParam & 0xFFFF))
        {
            case NIN_SELECT or NIN_KEYSELECT:
                IconClicked?.Invoke();
                break;
            case WM_CONTEXTMENU:
                ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                break;
        }
        return 0;
    }

    private TrayMenu? _menu;

    /// <summary>The open menu's XamlRoot, for the test driver.</summary>
    internal Microsoft.UI.Xaml.XamlRoot? MenuRoot => _menu?.Root;

    internal Windows.Graphics.PointInt32 MenuHostPosition => _menu?.HostPosition ?? default;

    /// <summary>Opens the context menu at the screen point (physical px).</summary>
    internal void ShowMenu(int x, int y)
    {
        var s = Settings?.Invoke() ?? new ClipmarkSettings();
        // The point is on the taskbar: anchor 12 DIP inside the work area, so the menu opens away from the taskbar with the
        // gap of the shell menus (it opens from the anchor corner that keeps it on the screen).
        var info = new MONITORINFO { cbSize = sizeof(MONITORINFO) };
        if (GetMonitorInfo(MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST), ref info))
        {
            var work = info.rcWork;
            int gap = (int)(12 * GetDpiForWindow(Handle) / 96);
            x = Math.Clamp(x, work.Left + gap, work.Right - 1 - gap);
            y = Math.Clamp(y, work.Top + gap, work.Bottom - 1 - gap);
        }
        var menu = _menu ??= new TrayMenu();
        menu.Show(x, y, s.Theme,
        [
            menu.Item(Loc.Get("TrayOpen"), "\uE8A7", OpenRequested),
            menu.Item(Loc.Get("TraySettings"), "\uE713", SettingsRequested),
            new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator(),
            menu.Item(Loc.Get("TrayNewCapture"), "\uF406", CaptureRequested, s.Hotkey.ToString()),
            menu.Item(Loc.Get("TrayNewRecording"), "\uE714", RecordRequested, s.RecordHotkey.ToString()),
            menu.SubMenu(Loc.Get("TrayMode"), MainPage.Modes.First(m => m.Mode == s.Mode).Glyph,
                MainPage.Modes.Select(m => (m.Text, (string?)m.Glyph, m.Mode == s.Mode, (Action)(() => ModeChosen?.Invoke(m.Mode))))),
            menu.SubMenu(Loc.Get("TrayDelay"), "\uE916",
                ClipmarkSettings.DelayChoices.Select(d => (MainPage.DelayText(d), (string?)null, d == s.DelaySeconds, (Action)(() => DelayChosen?.Invoke(d))))),
            new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator(),
            menu.Item(Loc.Get("TrayExit"), "\uE7E8", ExitRequested),
        ]);
    }

    public override void Dispose()
    {
        _menu?.Close();
        var data = NewData();
        Shell_NotifyIcon(NIM_DELETE, &data);
        UnregisterHotKey(Handle, CaptureHotkeyId);
        UnregisterHotKey(Handle, RecordHotkeyId);
        if (_icon != 0)
            DestroyIcon(_icon);
        base.Dispose();
    }
}
