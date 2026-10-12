using System.Runtime.InteropServices;
using Clipmark.Core.Sharing;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Output;

/// <summary>Pastes the clipboard into a running AI agent (Claude, Codex, ChatGPT): brings its window to the front and sends the paste key.</summary>
internal static class AgentSend
{
    private const nint InvalidHandle = -1;

    /// <summary>Agent targets on this desktop, most recently active first.</summary>
    public static List<AgentTarget> FindTargets() => FindTargets(out _);

    /// <summary>Agent targets, and the process snapshot they came from (for <see cref="AgentIcons"/>).</summary>
    public static List<AgentTarget> FindTargets(out List<ProcessEntry> processes)
    {
        processes = Processes();
        return AgentTargets.Find(processes, Windows());
    }

    public static string PasteKeyText(AgentTarget target) => target.PasteWithAlt ? "Alt+V" : "Ctrl+V";

    /// <summary>Null when the paste key was sent, else why not. The capture must already be on the clipboard.</summary>
    /// <remarks>The paste lands wherever the target has keyboard focus; Windows Terminal pastes into its active tab.</remarks>
    public static async Task<string?> PasteAsync(AgentTarget target)
    {
        nint hwnd = target.Window;
        string fallback = $"The capture is on the clipboard: press {PasteKeyText(target)} in its prompt.";
        if (!IsWindow(hwnd))
            return $"{target.Label} is closed. {fallback}";
        if (IsElevated((uint)target.ProcessId) && !IsElevated((uint)Environment.ProcessId))
            return $"{target.Label} runs as administrator, so Clipmark cannot type into it. {fallback}";
        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
        for (int i = 0; i < 50 && GetForegroundWindow() != hwnd; i++)
            await Task.Delay(20);
        if (GetForegroundWindow() != hwnd)
            return $"Windows did not bring {target.Label} to the front. {fallback}";
        await Task.Delay(150); // the activated window moves keyboard focus back to its prompt
        return SendPaste(target.PasteWithAlt) ? null : $"Windows blocked the paste key. {fallback}";
    }

    private static unsafe bool SendPaste(bool alt)
    {
        ushort modifier = alt ? VK_MENU : VK_CONTROL;
        INPUT* inputs = stackalloc INPUT[4];
        inputs[0] = Key(modifier, up: false);
        inputs[1] = Key(VK_V, up: false);
        inputs[2] = Key(VK_V, up: true);
        inputs[3] = Key(modifier, up: true);
        return SendInput(4, inputs, sizeof(INPUT)) == 4;

        static INPUT Key(ushort vk, bool up) =>
            new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } } };
    }

    /// <summary>True when the process token is elevated, or when its token is closed to this process (an elevated process).</summary>
    private static unsafe bool IsElevated(uint processId)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
            return false;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out nint token))
                return Marshal.GetLastPInvokeError() == 5; // ERROR_ACCESS_DENIED
            try
            {
                int elevated = 0;
                return GetTokenInformation(token, TokenElevation, &elevated, sizeof(int), out _) && elevated != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static unsafe List<ProcessEntry> Processes()
    {
        var list = new List<ProcessEntry>();
        nint snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == InvalidHandle)
            return list;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = sizeof(PROCESSENTRY32W) };
            for (bool ok = Process32FirstW(snapshot, &e); ok; ok = Process32NextW(snapshot, &e))
            {
                string name = new(e.szExeFile);
                // Only agent processes need their path (VS Code extension check, icons); opening every process costs time.
                string path = AgentTargets.NeedsPath(name) ? ImagePath(e.th32ProcessID) : "";
                list.Add(new((int)e.th32ProcessID, (int)e.th32ParentProcessID, name, path));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return list;
    }

    private static unsafe string ImagePath(uint processId)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
            return "";
        try
        {
            char* buffer = stackalloc char[1024];
            int size = 1024;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, size) : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Visible, uncloaked, unowned top-level windows of other processes, topmost first.</summary>
    private static unsafe List<WindowEntry> Windows()
    {
        var list = new List<WindowEntry>();
        var handle = GCHandle.Alloc(list);
        try
        {
            EnumWindows(&OnWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return list;
    }

    [UnmanagedCallersOnly]
    private static unsafe int OnWindow(nint hwnd, nint data)
    {
        if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != 0 || ((uint)GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0)
            return 1;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId)
            return 1;
        int cloaked = 0;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0)
            return 1; // another virtual desktop, or a suspended UWP window
        char* cls = stackalloc char[64];
        char* title = stackalloc char[256];
        ((List<WindowEntry>)GCHandle.FromIntPtr(data).Target!).Add(
            new(hwnd, (int)pid, new string(cls, 0, GetClassName(hwnd, cls, 64)), new string(title, 0, GetWindowText(hwnd, title, 256))));
        return 1;
    }
}
