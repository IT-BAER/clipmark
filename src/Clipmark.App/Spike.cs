using System.Diagnostics;
using System.Runtime.InteropServices;
using Clipmark.App.Capture;
using Clipmark.App.Interop;

namespace Clipmark.App;

/// <summary>
/// M0 measurement hooks. `--spike-capture DIR` captures and exits; `--spike-startup FILE`
/// writes the first-frame time (UTC ticks) and exits. Removed after M0.
/// </summary>
internal static class Spike
{
    private const int HotkeyId = 1;
    private const uint VK_F8 = 0x77;
    private static Action? s_onHotkey;

    public static string? Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static async Task<string> CaptureToFolderAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var watch = Stopwatch.StartNew();
        var frame = ScreenCapture.CaptureDesktop();
        long captureMs = watch.ElapsedMilliseconds;
        for (int i = 0; i < frame.Monitors.Count; i++)
            await frame.SavePngAsync(frame.Monitors[i], Path.Combine(folder, $"monitor-{i}.png"));
        long totalMs = watch.ElapsedMilliseconds;
        var layout = string.Join("; ", frame.Monitors.Select(m => $"{m.X},{m.Y} {m.Width}x{m.Height}"));
        var report = $"virtual={frame.Screen.Bounds.Width}x{frame.Screen.Bounds.Height} monitors=[{layout}] capture_ms={captureMs} capture_plus_png_ms={totalMs}";
        await File.WriteAllTextAsync(Path.Combine(folder, "report.txt"), report);
        return report;
    }

    /// <summary>Ctrl+Shift+F8 while the window exists (spike only; the real app uses a tray window).</summary>
    public static unsafe bool RegisterHotkey(nint hwnd, Action onHotkey)
    {
        s_onHotkey = onHotkey;
        Native.SetWindowSubclass(hwnd, &SubclassProc, HotkeyId, 0);
        return Native.RegisterHotKey(hwnd, HotkeyId, Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT, VK_F8);
    }

    [UnmanagedCallersOnly]
    private static nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nint id, nint data)
    {
        if (msg == Native.WM_HOTKEY && wParam == HotkeyId)
        {
            s_onHotkey?.Invoke();
            return 0;
        }
        return Native.DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
