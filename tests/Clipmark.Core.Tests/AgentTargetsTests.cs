using Clipmark.Core.Sharing;

namespace Clipmark.Core.Tests;

public class AgentTargetsTests
{
    private const string DesktopExe = @"C:\Program Files\WindowsApps\Claude_2.31226.1.0_x64__pzs8sxrjxfjjc\app\claude.exe";
    private const string EmbeddedCli = @"C:\Users\u\AppData\Roaming\Claude\claude-code\2.1.295\d2e29494ec83\claude.exe";
    private const string Cli = @"C:\Users\u\.local\bin\claude.exe";
    private const string ExtensionCli = @"C:\Users\u\.vscode\extensions\anthropic.claude-code-2.1.295-win32-x64\resources\native-binary\claude.exe";
    private const string CodexCli = @"C:\Users\u\AppData\Roaming\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe";
    private const string CodexExtension = @"C:\Users\u\.vscode\extensions\openai.chatgpt-26.1007.21434-win32-x64\bin\windows-x86_64\codex.exe";
    private const string ChatGptExe = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1002.7124.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe";

    private static ProcessEntry P(int id, int parent, string name, string path = "") => new(id, parent, name, path);

    // Desktop app with one embedded session that runs an MCP helper which starts another CLI (measured chain).
    private static readonly ProcessEntry[] Desktop =
    [
        P(23964, 43060, "claude.exe", DesktopExe), P(49512, 23964, "claude.exe", DesktopExe),
        P(41468, 23964, "claude.exe", EmbeddedCli), P(20328, 41468, "cmd.exe"), P(33956, 20328, "node.exe"),
        P(60388, 33956, "claude.exe", Cli),
    ];

    // CLI in Windows Terminal with a child claude.exe (measured chain).
    private static readonly ProcessEntry[] TerminalCli =
    [
        P(44708, 900, "WindowsTerminal.exe"), P(21144, 44708, "OpenConsole.exe"), P(23568, 44708, "pwsh.exe"),
        P(71000, 23568, "claude.exe", Cli), P(57264, 71000, "claude.exe", Cli),
    ];

    // ChatGPT desktop app with its embedded Codex, and the npm Codex CLI in Windows Terminal (measured chains).
    private static readonly ProcessEntry[] Codex =
    [
        P(73824, 46792, "ChatGPT.exe", ChatGptExe), P(69268, 73824, "codex.exe", @"C:\Users\u\AppData\Local\OpenAI\Codex\bin\9691020b546a15b2\codex.exe"),
        P(44708, 900, "WindowsTerminal.exe"), P(75356, 44708, "pwsh.exe"), P(20024, 75356, "node.exe"), P(75224, 20024, "codex.exe", CodexCli),
    ];

    private static readonly WindowEntry DesktopWindow = new(132836, 23964, "Chrome_WidgetWin_1", "Clipmark repository setup");
    private static readonly WindowEntry TerminalWindow = new(199454, 44708, "CASCADIA_HOSTING_WINDOW_CLASS", "cm-probe");
    private static readonly WindowEntry ChatGptWindow = new(788898, 73824, "Chrome_WidgetWin_1", "ChatGPT");

    [Fact]
    public void Desktop_app_window_is_a_target_and_its_embedded_sessions_are_not()
    {
        var targets = AgentTargets.Find(Desktop, [DesktopWindow]);

        Assert.Equal([new AgentTarget(132836, 23964, Agent.Claude, AgentHost.DesktopApp, "Clipmark repository setup", DesktopExe)], targets);
    }

    [Fact]
    public void Cli_in_windows_terminal_targets_the_terminal_window_once()
    {
        var targets = AgentTargets.Find(TerminalCli, [TerminalWindow]);

        Assert.Equal([new AgentTarget(199454, 44708, Agent.Claude, AgentHost.Terminal, "cm-probe", Cli, AgentProcessId: 71000)], targets);
    }

    [Fact]
    public void Chatgpt_app_and_codex_cli_are_codex_targets_and_the_embedded_codex_is_not()
    {
        var targets = AgentTargets.Find(Codex, [ChatGptWindow, TerminalWindow]);

        Assert.Equal(
        [
            new AgentTarget(788898, 73824, Agent.Codex, AgentHost.DesktopApp, "ChatGPT", ChatGptExe),
            new AgentTarget(199454, 44708, Agent.Codex, AgentHost.Terminal, "cm-probe", CodexCli, AgentProcessId: 75224),
        ], targets);
    }

    [Fact]
    public void Codex_started_by_a_claude_session_belongs_to_that_session()
    {
        ProcessEntry[] processes = [.. TerminalCli, P(80, 71000, "node.exe"), P(81, 80, "codex.exe", CodexCli)];

        var targets = AgentTargets.Find(processes, [TerminalWindow]);

        Assert.Equal([Agent.Claude], targets.Select(t => t.Agent));
    }

    [Fact]
    public void Topmost_window_comes_first()
    {
        var targets = AgentTargets.Find([.. Desktop, .. TerminalCli], [TerminalWindow, DesktopWindow]);

        Assert.Equal([AgentHost.Terminal, AgentHost.DesktopApp], targets.Select(t => t.Host));
    }

    [Fact]
    public void Vs_code_extensions_and_vs_code_terminal_are_separate_targets_on_the_same_window()
    {
        ProcessEntry[] processes =
        [
            P(100, 1, "Code.exe"), P(101, 100, "Code.exe"), P(102, 101, "claude.exe", ExtensionCli),
            P(103, 100, "Code.exe"), P(104, 103, "pwsh.exe"), P(105, 104, "claude.exe", Cli),
            P(106, 101, "codex.exe", CodexExtension),
        ];
        var window = new WindowEntry(500, 100, "Chrome_WidgetWin_1", "clipmark - Visual Studio Code");

        var targets = AgentTargets.Find(processes, [window]);

        Assert.Equal([(Agent.Claude, AgentHost.VsCode), (Agent.Claude, AgentHost.Terminal), (Agent.Codex, AgentHost.VsCode)],
            targets.Select(t => (t.Agent, t.Host)));
        Assert.All(targets, t => Assert.Equal(500, t.Window));
    }

    [Fact]
    public void Classic_console_window_owned_by_the_shell_is_the_host()
    {
        ProcessEntry[] processes = [P(10, 1, "explorer.exe"), P(11, 10, "cmd.exe"), P(12, 11, "claude.exe", Cli)];
        WindowEntry[] windows = [new(600, 11, "ConsoleWindowClass", "Command Prompt - claude"), new(601, 10, "CabinetWClass", "Downloads")];

        Assert.Equal([new AgentTarget(600, 11, Agent.Claude, AgentHost.Terminal, "Command Prompt - claude", Cli, AgentProcessId: 12)], AgentTargets.Find(processes, windows));
    }

    // Windows Terminal titles its window after the active tab; the agent's console title is its own tab (measured).
    private static readonly AgentTarget TerminalTarget =
        new(199454, 44708, Agent.Claude, AgentHost.Terminal, "V8-Other", Cli, AgentProcessId: 71000, ConsoleTitle: "✳ Claude Code");

    [Fact]
    public void Terminal_label_names_the_agents_own_tab()
    {
        Assert.Equal("Terminal (Claude): ✳ Claude Code", TerminalTarget.Label);
        Assert.Equal("Terminal (Claude): V8-Other", (TerminalTarget with { ConsoleTitle = "" }).Label);
    }

    [Fact]
    public void Another_active_windows_terminal_tab_is_detected()
    {
        Assert.True(TerminalTarget.ShowsOtherTab("CASCADIA_HOSTING_WINDOW_CLASS", "V8-Other"));
        Assert.False(TerminalTarget.ShowsOtherTab("CASCADIA_HOSTING_WINDOW_CLASS", "✳ Claude Code"));
        Assert.False(TerminalTarget.ShowsOtherTab("ConsoleWindowClass", "V8-Other")); // one console per window
        Assert.False((TerminalTarget with { ConsoleTitle = "" }).ShowsOtherTab("CASCADIA_HOSTING_WINDOW_CLASS", "V8-Other"));
        Assert.False((TerminalTarget with { Host = AgentHost.VsCode }).ShowsOtherTab("CASCADIA_HOSTING_WINDOW_CLASS", "V8-Other"));
    }

    [Fact]
    public void Nothing_is_found_without_an_agent_window_or_with_a_parent_loop()
    {
        ProcessEntry[] orphan = [P(20, 999, "claude.exe", Cli)];
        ProcessEntry[] loop = [P(30, 31, "pwsh.exe"), P(31, 30, "cmd.exe"), P(32, 30, "codex.exe", CodexCli)];

        Assert.Empty(AgentTargets.Find(orphan, [TerminalWindow]));
        Assert.Empty(AgentTargets.Find(loop, [TerminalWindow]));
        Assert.Empty(AgentTargets.Find(TerminalCli, []));
    }

    [Fact]
    public void Only_the_clis_paste_with_alt_and_labels_name_the_app()
    {
        var targets = AgentTargets.Find([.. Desktop, .. TerminalCli], [TerminalWindow, DesktopWindow]);

        Assert.Equal([true, false], targets.Select(t => t.PasteWithAlt));
        Assert.Equal(["Terminal (Claude): cm-probe", "Claude app: Clipmark repository setup"], targets.Select(t => t.Label));
        Assert.Equal("ChatGPT app: ChatGPT", new AgentTarget(1, 1, Agent.Codex, AgentHost.DesktopApp, "ChatGPT").Label);
        Assert.Equal("VS Code (Codex)", new AgentTarget(1, 1, Agent.Codex, AgentHost.VsCode, " ").Label);
    }

    [Fact]
    public void Icon_sources_are_the_agent_exe_then_the_running_desktop_app_of_the_same_agent()
    {
        ProcessEntry[] processes = [.. Codex, .. TerminalCli];
        var targets = AgentTargets.Find(processes, [ChatGptWindow, TerminalWindow]);

        Assert.Equal([ChatGptExe], AgentTargets.IconSources(targets[0], processes));
        Assert.Equal([CodexCli, ChatGptExe], AgentTargets.IconSources(targets[1], processes));
        Assert.Equal([Cli], AgentTargets.IconSources(new AgentTarget(1, 1, Agent.Claude, AgentHost.Terminal, "", Cli), TerminalCli));
        Assert.Empty(AgentTargets.IconSources(new AgentTarget(1, 1, Agent.Codex, AgentHost.VsCode, ""), [P(5, 1, "ChatGPT.exe")]));
    }

    [Fact]
    public void Two_clis_in_one_terminal_window_are_one_target()
    {
        ProcessEntry[] processes = [.. TerminalCli, P(72000, 23568, "claude.exe", EmbeddedCli)];

        Assert.Single(AgentTargets.Find(processes, [TerminalWindow]));
    }
}
