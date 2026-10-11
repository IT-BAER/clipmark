using System.Runtime.InteropServices;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Interop;

/// <summary>Plain Win32 window on the UI thread; the WinUI message loop pumps its messages.</summary>
internal abstract unsafe class Win32Window : IDisposable
{
    private const string ClassName = "Clipmark.Win32Window";
    private static readonly Dictionary<nint, Win32Window> s_windows = [];
    private static bool s_registered;

    public nint Handle { get; }

    protected Win32Window(uint style, uint exStyle, string? title = null)
    {
        RegisterClassOnce();
        Handle = CreateWindowEx(exStyle, ClassName, title, style, 0, 0, 0, 0, 0, 0, GetModuleHandle(null), 0);
        if (Handle == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        s_windows[Handle] = this;
    }

    /// <summary>Return null to fall through to DefWindowProc.</summary>
    protected abstract nint? OnMessage(uint msg, nint wParam, nint lParam);

    public virtual void Dispose()
    {
        if (s_windows.Remove(Handle))
            DestroyWindow(Handle);
    }

    private static void RegisterClassOnce()
    {
        if (s_registered)
            return;
        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursor(0, IDC_ARROW),
                lpszClassName = name,
            };
            if (RegisterClassEx(&wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");
        }
        s_registered = true;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (s_windows.TryGetValue(hwnd, out var window) && window.OnMessage(msg, wParam, lParam) is { } result)
                return result;
        }
        catch (Exception e)
        {
            // An exception must not unwind through native frames; that would kill the process.
            System.Diagnostics.Debug.WriteLine(e);
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
