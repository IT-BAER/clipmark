using Clipmark.Core.Shell;
using Microsoft.Win32;

namespace Clipmark.App.Shell;

/// <summary>Shows the tray icon on the taskbar the first time this exe path runs (see <see cref="TrayIconEntry"/>).</summary>
internal static class TrayPromotion
{
    private const string RootKey = @"Control Panel\NotifyIconSettings";

    /// <summary>Windows writes the entry about 1.5 s after NIM_ADD (measured), so this polls for up to 15 s off the UI thread.</summary>
    public static void Start() => _ = Task.Run(async () =>
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(250);
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(RootKey);
                foreach (string name in root?.GetSubKeyNames() ?? [])
                {
                    using var entry = root!.OpenSubKey(name); // read-only: a locked foreign entry must not end the scan
                    if (entry is null || !TrayIconEntry.IsFor(entry.GetValue("ExecutablePath") as string, exe, programFiles))
                        continue;
                    if (TrayIconEntry.ShouldPromote(entry.GetValue("IsPromoted")))
                    {
                        using var own = root.OpenSubKey(name, writable: true);
                        own?.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                    }
                    return;
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return; // the icon only stays in the overflow
            }
        }
    });
}
