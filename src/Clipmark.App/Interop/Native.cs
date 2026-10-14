using System.Runtime.InteropServices;

namespace Clipmark.App.Interop;

// Source-generated P/Invoke only: Native AOT has no runtime marshalling stubs.
internal static unsafe partial class Native
{
    public const int SRCCOPY = 0x00CC0020;
    public const int CAPTUREBLT = 0x40000000;

    public const uint WM_DESTROY = 0x0002, WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_ERASEBKGND = 0x0014,
        WM_SETCURSOR = 0x0020, WM_KEYDOWN = 0x0100, WM_COMMAND = 0x0111, WM_MOUSEMOVE = 0x0200,
        WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_HOTKEY = 0x0312,
        WM_CONTEXTMENU = 0x007B, WM_NULL = 0x0000, WM_APP = 0x8000, WM_CAPTURECHANGED = 0x0215, WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int VK_ESCAPE = 0x1B;

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    public const int SW_HIDE = 0, SW_SHOWNA = 8, SW_SHOW = 5;
    public const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public static readonly nint HWND_TOPMOST = -1;
    public const int GWL_EXSTYLE = -20;
    public const int IDC_ARROW = 32512, IDC_CROSS = 32515, IDC_HAND = 32649;

    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4, NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401;

    public const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_NONOTIFY = 0x80;

    public const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    public const int SM_CXSMICON = 49, SM_CYSMICON = 50;

    public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3, DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    public const int TRANSPARENT = 1, PS_SOLID = 0, PS_GEOMETRIC = 0x10000, NULL_BRUSH = 5;
    public const uint DT_CENTER = 0x1, DT_VCENTER = 0x4, DT_SINGLELINE = 0x20, DT_NOPREFIX = 0x800, DT_CALCRECT = 0x400;

    public const uint CF_DIB = 8, CF_UNICODETEXT = 13, CF_HDROP = 15, GMEM_MOVEABLE = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public int cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore, fIncUpdate;
        public fixed byte rgbReserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONDATAW
    {
        public int cbSize;
        public nint hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState, dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    // Monitors and capture
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(nint hdc, nint clip, delegate* unmanaged<nint, nint, RECT*, nint, int> callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll")] public static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] public static partial nint MonitorFromPoint(POINT point, uint flags);
    [LibraryImport("user32.dll")] public static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] public static partial int ReleaseDC(nint hwnd, nint hdc);
    [LibraryImport("gdi32.dll")] public static partial nint CreateCompatibleDC(nint hdc);
    [LibraryImport("gdi32.dll")] public static partial nint CreateCompatibleBitmap(nint hdc, int width, int height);
    [LibraryImport("gdi32.dll")] public static partial nint SelectObject(nint hdc, nint obj);
    [LibraryImport("gdi32.dll")] public static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* info, uint usage, void** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, int rop);

    [LibraryImport("gdi32.dll")]
    public static partial int GetDIBits(nint hdc, nint bitmap, uint start, uint lines, void* bits, BITMAPINFOHEADER* info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);

    // GDI drawing
    [LibraryImport("gdi32.dll")] public static partial nint CreateSolidBrush(uint color);
    [LibraryImport("gdi32.dll")] public static partial nint CreatePen(int style, int width, uint color);
    [LibraryImport("gdi32.dll")] public static partial nint GetStockObject(int index);
    [LibraryImport("gdi32.dll")] public static partial int SetBkMode(nint hdc, int mode);
    [LibraryImport("gdi32.dll")] public static partial uint SetTextColor(nint hdc, uint color);
    [LibraryImport("user32.dll")] public static partial int FillRect(nint hdc, RECT* rect, nint brush);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Rectangle(nint hdc, int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RoundRect(nint hdc, int left, int top, int right, int bottom, int w, int h);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Polyline(nint hdc, POINT* points, int count);

    [StructLayout(LayoutKind.Sequential)]
    public struct LOGBRUSH { public uint Style, Color; public nuint Hatch; }

    /// <summary>Geometric pen: with <see cref="PS_GEOMETRIC"/> alone it has round caps and joins.</summary>
    [LibraryImport("gdi32.dll")] public static partial nint ExtCreatePen(uint style, uint width, LOGBRUSH* brush, uint styleCount, uint* styles);

    [LibraryImport("user32.dll")] public static partial short GetKeyState(int key);

    // GDI+ flat API: GDI itself draws curves without anti-aliasing.
    [StructLayout(LayoutKind.Sequential)]
    public struct GdiplusStartupInput { public uint Version; public nint DebugEventCallback; public int SuppressBackgroundThread, SuppressExternalCodecs; }

    public const int SmoothingModeAntiAlias = 4;

    [LibraryImport("gdiplus.dll")] public static partial int GdiplusStartup(out nint token, GdiplusStartupInput* input, nint output);
    [LibraryImport("gdiplus.dll")] public static partial int GdipCreateFromHDC(nint hdc, out nint graphics);
    [LibraryImport("gdiplus.dll")] public static partial int GdipSetSmoothingMode(nint graphics, int mode);
    [LibraryImport("gdiplus.dll")] public static partial int GdipCreateSolidFill(uint argb, out nint brush);
    [LibraryImport("gdiplus.dll")] public static partial int GdipFillEllipse(nint graphics, nint brush, float x, float y, float width, float height);
    [LibraryImport("gdiplus.dll")] public static partial int GdipDeleteBrush(nint brush);
    [LibraryImport("gdiplus.dll")] public static partial int GdipDeleteGraphics(nint graphics);

    [LibraryImport("gdi32.dll")] public static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int w, int h);
    [LibraryImport("user32.dll")] public static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    public const uint WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, LWA_ALPHA = 0x2;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFont(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);

    [LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawText(nint hdc, string text, int count, RECT* rect, uint format);

    // Windows
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")]
    public static partial ushort RegisterClassEx(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(uint exStyle, string className, string? title, uint style,
        int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(nint hwnd, RECT* rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateWindow(nint hwnd);

    [LibraryImport("user32.dll")] public static partial nint BeginPaint(nint hwnd, PAINTSTRUCT* ps);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EndPaint(nint hwnd, PAINTSTRUCT* ps);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandle(string? name);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")] public static partial nint LoadCursor(nint instance, nint name);
    [LibraryImport("user32.dll")] public static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT p);

    [LibraryImport("user32.dll")] public static partial nint SetCapture(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")] public static partial nint SetFocus(nint hwnd);
    [LibraryImport("user32.dll")] public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll")] public static partial uint GetDpiForWindow(nint hwnd);
    [LibraryImport("user32.dll")] public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    // Window enumeration
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] public static partial int GetClassName(nint hwnd, char* name, int max);
    [LibraryImport("user32.dll")] public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("dwmapi.dll")] public static partial int DwmGetWindowAttribute(nint hwnd, int attribute, void* value, int size);
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(nint hwnd, int attribute, void* value, int size);

    // Hotkey
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hwnd, int id);

    // Tray icon and menu
    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIcon(uint message, NOTIFYICONDATAW* data);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadImage(nint instance, string name, uint type, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public int fIcon, xHotspot, yHotspot;
        public nint hbmMask, hbmColor;
    }

    /// <summary>Icons extracted, 0 when the file has no icon resource, uint.MaxValue when it cannot be read.</summary>
    [LibraryImport("user32.dll", EntryPoint = "PrivateExtractIconsW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PrivateExtractIcons(string file, int index, int cx, int cy, nint* icons, uint* ids, uint count, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetIconInfo(nint icon, ICONINFO* info);

    public struct DATA_BLOB
    {
        public uint cbData;
        public byte* pbData;
    }

    public const uint CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CryptProtectData(DATA_BLOB* dataIn, char* description, DATA_BLOB* entropy, nint reserved,
        nint prompt, uint flags, DATA_BLOB* dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CryptUnprotectData(DATA_BLOB* dataIn, char** description, DATA_BLOB* entropy, nint reserved,
        nint prompt, uint flags, DATA_BLOB* dataOut);

    [LibraryImport("kernel32.dll")] public static partial nint LocalFree(nint mem);

    [LibraryImport("user32.dll")] public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenu(nint menu, uint flags, nint id, string? text);

    [LibraryImport("user32.dll")] public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    // Processes and keyboard input (Send to AI)
    public const uint TH32CS_SNAPPROCESS = 0x2, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x8;
    public const int TokenElevation = 20, GW_OWNER = 4, SW_RESTORE = 9;
    public const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2;
    public const ushort VK_CONTROL = 0x11, VK_MENU = 0x12, VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESSENTRY32W
    {
        public int dwSize;
        public uint cntUsage, th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[260];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nuint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nuint dwExtraInfo; }

    // MOUSEINPUT sizes the union as in winuser.h (40 bytes on 64-bit, 28 on x86).
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public INPUTUNION u; }

    [LibraryImport("kernel32.dll")] public static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32FirstW(nint snapshot, PROCESSENTRY32W* entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32NextW(nint snapshot, PROCESSENTRY32W* entry);

    [LibraryImport("kernel32.dll")] public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(nint process, uint flags, char* name, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeConsole();

    [LibraryImport("kernel32.dll", EntryPoint = "GetConsoleTitleW")] public static partial uint GetConsoleTitle(char* title, uint size);
    [LibraryImport("kernel32.dll")] public static partial nint GetStdHandle(int stdHandle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetStdHandle(int stdHandle, nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint token, int infoClass, void* info, int length, out int returned);

    [LibraryImport("user32.dll")] public static partial nint GetWindow(nint hwnd, uint cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")] public static partial int GetWindowText(nint hwnd, char* text, int max);
    [LibraryImport("user32.dll")] public static partial uint SendInput(uint count, INPUT* inputs, int size);

    public const uint MIM_STYLE = 0x10, MNS_NOCHECK = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct MENUINFO
    {
        public int cbSize;
        public uint fMask, dwStyle, cyMax;
        public nint hbrBack;
        public uint dwContextHelpID;
        public nuint dwMenuData;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetMenuInfo(nint menu, MENUINFO* info);

    [LibraryImport("kernel32.dll")] public static partial nint GetProcAddress(nint module, nint ordinal);

    /// <summary>Lets Win32 popup menus follow the Windows dark/light app mode.</summary>
    public static void AllowDarkMenus()
    {
        // Undocumented uxtheme ordinals, stable since Windows 10 1903: 135 SetPreferredAppMode, 136 FlushMenuThemes.
        nint uxtheme = NativeLibrary.Load("uxtheme.dll", typeof(Native).Assembly, DllImportSearchPath.System32);
        nint setPreferredAppMode = GetProcAddress(uxtheme, 135), flushMenuThemes = GetProcAddress(uxtheme, 136);
        if (setPreferredAppMode == 0 || flushMenuThemes == 0)
            return;
        ((delegate* unmanaged<int, int>)setPreferredAppMode)(1); // 1 = AllowDark
        ((delegate* unmanaged<void>)flushMenuThemes)();
    }

    // Clipboard
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")] public static partial nint SetClipboardData(uint format, nint data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterClipboardFormat(string name);

    [LibraryImport("kernel32.dll")] public static partial nint GlobalAlloc(uint flags, nuint bytes);
    [LibraryImport("kernel32.dll")] public static partial void* GlobalLock(nint mem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint mem);

    [LibraryImport("kernel32.dll")] public static partial nint GlobalFree(nint mem);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WindowsCreateString(string text, uint length, out nint hstring);

    [LibraryImport("combase.dll")] public static partial int WindowsDeleteString(nint hstring);

    [LibraryImport("combase.dll")]
    public static partial int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);

    // Capture exclusion (recording bar and outline stay out of the video)
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SetCurrentProcessExplicitAppUserModelID(string appId);

    [LibraryImport("kernel32.dll")] public static partial int GetCurrentPackageFullName(ref uint length, char* name);

    // COM
    public const uint COINIT_MULTITHREADED = 0x0, CLSCTX_ALL = 0x17;
    [LibraryImport("ole32.dll")] public static partial int CoInitializeEx(nint reserved, uint coInit);
    [LibraryImport("ole32.dll")] public static partial void CoUninitialize();
    [LibraryImport("ole32.dll")] public static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);
    [LibraryImport("ole32.dll")] public static partial void CoTaskMemFree(nint p);

    // Credential Manager
    public const uint CRED_TYPE_GENERIC = 1, CRED_PERSIST_LOCAL_MACHINE = 2;
    public const int ERROR_NOT_FOUND = 1168;

    /// <summary>LastWritten (FILETIME) is two DWORDs so the layout matches on x86 too.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CREDENTIALW
    {
        public uint Flags, Type;
        public char* TargetName, Comment;
        public uint LastWrittenLow, LastWrittenHigh;
        public uint CredentialBlobSize;
        public byte* CredentialBlob;
        public uint Persist, AttributeCount;
        public nint Attributes;
        public char* TargetAlias, UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredWrite(CREDENTIALW* credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredRead(string target, uint type, uint flags, out CREDENTIALW* credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll")] public static partial void CredFree(void* buffer);

    public static uint Rgb(byte r, byte g, byte b) => (uint)(r | g << 8 | b << 16);
}
