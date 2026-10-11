using Clipmark.App.Interop;
using Clipmark.Core.Settings;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Shell;

/// <summary>Hidden window that owns the tray icon and the global hotkey.</summary>
internal sealed unsafe class TrayHost : Win32Window
{
    private const int HotkeyId = 1;
    private const uint TrayId = 1, TrayCallback = WM_APP + 1;
    private const int CmdCapture = 1, CmdOpen = 2, CmdSettings = 3, CmdExit = 4, CmdModeBase = 100, CmdDelayBase = 200;

    private static readonly (CaptureMode Mode, string Text)[] Modes =
    [
        (CaptureMode.Rectangle, "Rectangle"), (CaptureMode.Window, "Window"), (CaptureMode.FullScreen, "Full screen"),
        (CaptureMode.AllMonitors, "All monitors"), (CaptureMode.Freeform, "Freeform"),
    ];

    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private nint _icon;

    public event Action? CaptureRequested;
    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action<CaptureMode>? ModeChosen;
    public event Action<int>? DelayChosen;

    /// <summary>Supplies the current mode and delay for the menu check marks.</summary>
    public Func<ClipmarkSettings>? Settings { get; set; }

    public TrayHost() : base(WS_POPUP, WS_EX_TOOLWINDOW, "Clipmark tray") => AddIcon();

    public bool TryRegisterHotkey(Hotkey hotkey)
    {
        UnregisterHotKey(Handle, HotkeyId);
        return RegisterHotKey(Handle, HotkeyId, (uint)hotkey.Modifiers | MOD_NOREPEAT, (uint)hotkey.Key);
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
        if (msg == WM_HOTKEY && wParam == HotkeyId)
        {
            CaptureRequested?.Invoke();
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
                CaptureRequested?.Invoke();
                break;
            case WM_CONTEXTMENU:
                ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                break;
        }
        return 0;
    }

    private void ShowMenu(int x, int y)
    {
        var s = Settings?.Invoke() ?? new ClipmarkSettings();
        nint menu = CreatePopupMenu(), modes = CreatePopupMenu(), delays = CreatePopupMenu();
        try
        {
            foreach (var (mode, text) in Modes)
                AppendMenu(modes, s.Mode == mode ? MF_CHECKED : MF_STRING, CmdModeBase + (int)mode, text);
            foreach (int d in ClipmarkSettings.DelayChoices)
                AppendMenu(delays, s.DelaySeconds == d ? MF_CHECKED : MF_STRING, CmdDelayBase + d, d == 0 ? "No delay" : $"{d} seconds");

            AppendMenu(menu, MF_STRING, CmdCapture, $"New capture\t{s.Hotkey}");
            AppendMenu(menu, MF_POPUP, modes, "Mode");
            AppendMenu(menu, MF_POPUP, delays, "Delay");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdOpen, "Open Clipmark");
            AppendMenu(menu, MF_STRING, CmdSettings, "Settings");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdExit, "Exit");

            // Without foreground the menu does not close on an outside click (documented TrackPopupMenu quirk).
            SetForegroundWindow(Handle);
            int cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, x, y, Handle, 0);
            PostMessage(Handle, WM_NULL, 0, 0);
            Dispatch(cmd);
        }
        finally
        {
            DestroyMenu(menu); // destroys the submenus too
        }
    }

    private void Dispatch(int cmd)
    {
        switch (cmd)
        {
            case CmdCapture: CaptureRequested?.Invoke(); break;
            case CmdOpen: OpenRequested?.Invoke(); break;
            case CmdSettings: SettingsRequested?.Invoke(); break;
            case CmdExit: ExitRequested?.Invoke(); break;
            case >= CmdDelayBase: DelayChosen?.Invoke(cmd - CmdDelayBase); break;
            case >= CmdModeBase: ModeChosen?.Invoke((CaptureMode)(cmd - CmdModeBase)); break;
        }
    }

    public override void Dispose()
    {
        var data = NewData();
        Shell_NotifyIcon(NIM_DELETE, &data);
        UnregisterHotKey(Handle, HotkeyId);
        if (_icon != 0)
            DestroyIcon(_icon);
        base.Dispose();
    }
}
