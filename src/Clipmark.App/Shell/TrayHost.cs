using Clipmark.App.Interop;
using Clipmark.Core.Settings;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Shell;

/// <summary>Hidden window that owns the tray icon and the global hotkey.</summary>
internal sealed unsafe class TrayHost : Win32Window
{
    public const int CaptureHotkeyId = 1, RecordHotkeyId = 2;
    private const uint TrayId = 1, TrayCallback = WM_APP + 1;
    private const int CmdCapture = 1, CmdOpen = 2, CmdSettings = 3, CmdExit = 4, CmdRecord = 5, CmdModeBase = 100, CmdDelayBase = 200;

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
        AllowDarkMenus();
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

    private void ShowMenu(int x, int y)
    {
        var s = Settings?.Invoke() ?? new ClipmarkSettings();
        nint menu = CreatePopupMenu(), modes = CreatePopupMenu(), delays = CreatePopupMenu();
        try
        {
            foreach (var (mode, text, _) in MainPage.Modes)
                AppendMenu(modes, s.Mode == mode ? MF_CHECKED : MF_STRING, CmdModeBase + (int)mode, text);
            foreach (int d in ClipmarkSettings.DelayChoices)
                AppendMenu(delays, s.DelaySeconds == d ? MF_CHECKED : MF_STRING, CmdDelayBase + d, MainPage.DelayText(d));

            AppendMenu(menu, MF_STRING, CmdCapture, $"{Loc.Get("TrayNewCapture")}\t{s.Hotkey}");
            AppendMenu(menu, MF_STRING, CmdRecord, $"{Loc.Get("TrayNewRecording")}\t{s.RecordHotkey}");
            AppendMenu(menu, MF_POPUP, modes, Loc.Get("TrayMode"));
            AppendMenu(menu, MF_POPUP, delays, Loc.Get("TrayDelay"));
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdOpen, Loc.Get("TrayOpen"));
            AppendMenu(menu, MF_STRING, CmdSettings, Loc.Get("TraySettings"));
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdExit, Loc.Get("TrayExit"));
            // The top level never shows checks: drop the reserved check-mark column (submenus keep it).
            var info = new MENUINFO { cbSize = sizeof(MENUINFO), fMask = MIM_STYLE, dwStyle = MNS_NOCHECK };
            SetMenuInfo(menu, &info);

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
            case CmdRecord: RecordRequested?.Invoke(); break;
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
        UnregisterHotKey(Handle, CaptureHotkeyId);
        UnregisterHotKey(Handle, RecordHotkeyId);
        if (_icon != 0)
            DestroyIcon(_icon);
        base.Dispose();
    }
}
