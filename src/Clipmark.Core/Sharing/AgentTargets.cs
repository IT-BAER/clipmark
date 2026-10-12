namespace Clipmark.Core.Sharing;

/// <summary>The assistant behind a target: Claude (Anthropic) or Codex (OpenAI, also inside the ChatGPT app).</summary>
public enum Agent { Claude, Codex }

/// <summary>Where the agent runs; decides the paste key.</summary>
public enum AgentHost { DesktopApp, Terminal, VsCode }

/// <summary>A running process: <see cref="Name"/> is the image file name ("claude.exe"), <see cref="Path"/> its full path or "".</summary>
public sealed record ProcessEntry(int Id, int ParentId, string Name, string Path);

/// <summary>A candidate top-level window (visible, not a tool window, no owner) of another process.</summary>
public sealed record WindowEntry(nint Handle, int ProcessId, string ClassName, string Title);

/// <summary>
/// A window that holds an agent prompt box; <see cref="ProcessId"/> owns the window, <see cref="ExePath"/> is the agent's
/// own executable (the desktop app, or the CLI inside a terminal or VS Code) or "".
/// </summary>
public sealed record AgentTarget(nint Window, int ProcessId, Agent Agent, AgentHost Host, string Title, string ExePath = "")
{
    /// <summary>The Claude Code and Codex CLIs bind image paste to Alt+V on Windows; the apps and VS Code panels use Ctrl+V.</summary>
    public bool PasteWithAlt => Host == AgentHost.Terminal;

    public string Label
    {
        get
        {
            string name = Agent == Agent.Claude ? "Claude" : "Codex";
            string kind = Host switch
            {
                AgentHost.DesktopApp => Agent == Agent.Claude ? "Claude app" : "ChatGPT app",
                AgentHost.VsCode => $"VS Code ({name})",
                _ => $"Terminal ({name})",
            };
            return string.IsNullOrWhiteSpace(Title) ? kind : $"{kind}: {Title}";
        }
    }
}

public static class AgentTargets
{
    private sealed record Profile(Agent Agent, string DesktopExe, string CliExe, string ExtensionFolder, string Package, string PackageExe);

    // Package family and the exe inside it: the installed desktop app lends its icon to a CLI without one (codex.exe).
    private static readonly Profile[] Profiles =
    [
        new(Agent.Claude, "claude.exe", "claude.exe", @"\extensions\anthropic.claude-code", "Claude_pzs8sxrjxfjjc", @"app\claude.exe"),
        new(Agent.Codex, "ChatGPT.exe", "codex.exe", @"\extensions\openai.chatgpt", "OpenAI.Codex_2p2nqsd0c76g0", @"app\ChatGPT.exe"),
    ];

    /// <summary>The executable names whose full path <see cref="Find"/> and <see cref="IconSources"/> read.</summary>
    public static bool NeedsPath(string exe) => Profiles.Any(a => exe.Equals(a.DesktopExe, StringComparison.OrdinalIgnoreCase)
        || exe.Equals(a.CliExe, StringComparison.OrdinalIgnoreCase));

    /// <summary>Store package family of the agent's desktop app and the exe path inside its install folder.</summary>
    public static (string Family, string Exe) DesktopPackage(Agent agent) => Array.Find(Profiles, a => a.Agent == agent) is { } p
        ? (p.Package, p.PackageExe) : ("", "");

    /// <summary>Executables to take the target's icon from, best first: its own exe, then running desktop apps of the same agent.</summary>
    public static List<string> IconSources(AgentTarget target, IReadOnlyList<ProcessEntry> processes)
    {
        var desktopExe = Array.Find(Profiles, a => a.Agent == target.Agent)!.DesktopExe;
        return processes.Where(p => Is(p, desktopExe)).Select(p => p.Path).Prepend(target.ExePath)
            .Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private const string DesktopWindowClass = "Chrome_WidgetWin_1";
    private const int MaxDepth = 64; // parent ids can loop after PID reuse

    /// <summary>
    /// Agent targets, most recently active first; <paramref name="windows"/> is in Z-order, topmost first. A CLI below
    /// another agent process (desktop app sessions, helpers, one agent started by another) is skipped: the outermost
    /// agent stands for it.
    /// </summary>
    public static List<AgentTarget> Find(IReadOnlyList<ProcessEntry> processes, IReadOnlyList<WindowEntry> windows)
    {
        var byId = new Dictionary<int, ProcessEntry>();
        foreach (var p in processes)
            byId.TryAdd(p.Id, p);
        var found = new List<(int Z, AgentTarget Target)>();
        for (int z = 0; z < windows.Count; z++)
        {
            var w = windows[z];
            if (w.ClassName == DesktopWindowClass && byId.TryGetValue(w.ProcessId, out var owner)
                && Array.Find(Profiles, a => Is(owner, a.DesktopExe)) is { } desktop)
                found.Add((z, new(w.Handle, w.ProcessId, desktop.Agent, AgentHost.DesktopApp, w.Title, owner.Path)));
        }
        foreach (var p in processes)
        {
            if (Array.Find(Profiles, a => Is(p, a.CliExe)) is not { } cli
                || windows.Any(w => w.ProcessId == p.Id && w.ClassName == DesktopWindowClass))
                continue;
            if (HostWindow(p, byId, windows) is not { } z)
                continue;
            var host = p.Path.Contains(cli.ExtensionFolder, StringComparison.OrdinalIgnoreCase) ? AgentHost.VsCode : AgentHost.Terminal;
            var w = windows[z];
            found.Add((z, new(w.Handle, w.ProcessId, cli.Agent, host, w.Title, p.Path)));
        }
        return found.OrderBy(f => f.Z).Select(f => f.Target).DistinctBy(t => (t.Window, t.Agent, t.Host)).ToList();
    }

    /// <summary>Z index of the topmost window of the process or its nearest windowed ancestor; null below another agent process.</summary>
    private static int? HostWindow(ProcessEntry cli, Dictionary<int, ProcessEntry> byId, IReadOnlyList<WindowEntry> windows)
    {
        var current = cli;
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            if (depth > 0 && Profiles.Any(a => Is(current, a.DesktopExe) || Is(current, a.CliExe)))
                return null;
            for (int z = 0; z < windows.Count; z++)
            {
                if (windows[z].ProcessId == current.Id)
                    return z;
            }
            if (current.ParentId == current.Id || !byId.TryGetValue(current.ParentId, out var parent))
                return null;
            current = parent;
        }
        return null;
    }

    private static bool Is(ProcessEntry p, string exe) => p.Name.Equals(exe, StringComparison.OrdinalIgnoreCase);
}
