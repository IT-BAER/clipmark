using Clipmark.Core.Settings;
using Microsoft.Win32;

namespace Clipmark.App.Shell;

/// <summary>
/// Per-user Windows settings Clipmark changes on request: the Run entry (start with Windows) and the Windows option that
/// makes Print Screen open Snipping Tool. Debug: CLIPMARK_TEST_REGROOT moves both under HKCU\{root} for scripts.
/// </summary>
internal static class SystemIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunValue = "Clipmark";
    private const string KeyboardKey = @"Control Panel\Keyboard", PrintScreenValue = "PrintScreenKeyForSnippingEnabled";

    public static readonly Hotkey PrintScreen = new(HotkeyModifiers.None, 0x2C);

    private static string Path(string key)
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("CLIPMARK_TEST_REGROOT") is { Length: > 0 } root)
            return root + "\\" + key;
#endif
        return key;
    }

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Path(RunKey));
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(Path(RunKey));
            if (value)
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --tray");
            else
                key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Turns the Windows option off so Print Screen reaches Clipmark, remembers the old value for <see cref="RestorePrintScreen"/>
    /// and makes Print Screen the capture hotkey.
    /// </summary>
    public static void TakeOverPrintScreen()
    {
        if (AppSettings.Current.PrintScreenRestore is not null)
            return;
        using var key = Registry.CurrentUser.CreateSubKey(Path(KeyboardKey));
        string restore = key.GetValue(PrintScreenValue) is int old ? old.ToString(System.Globalization.CultureInfo.InvariantCulture) : PrintScreenToken.Absent;
        AppSettings.Update(s => s with { PrintScreenRestore = restore, Hotkey = PrintScreen });
        key.SetValue(PrintScreenValue, 0, RegistryValueKind.DWord);
    }

    /// <summary>Puts the Windows option back as it was before the takeover; a Print Screen hotkey goes back to the default.</summary>
    public static void RestorePrintScreen()
    {
        if (AppSettings.Current.PrintScreenRestore is not { } restore)
            return;
        using (var key = Registry.CurrentUser.CreateSubKey(Path(KeyboardKey)))
        {
            if (PrintScreenToken.ValueOf(restore) is { } value)
                key.SetValue(PrintScreenValue, value, RegistryValueKind.DWord);
            else
                key.DeleteValue(PrintScreenValue, throwOnMissingValue: false);
        }
        AppSettings.Update(s => s with
        {
            PrintScreenRestore = null,
            Hotkey = s.Hotkey == PrintScreen ? new ClipmarkSettings().Hotkey : s.Hotkey,
        });
    }

    /// <summary>`--restore-system`: undoes both changes and removes the Start menu shortcut, for removing a portable copy.</summary>
    public static void RestoreAll()
    {
        RestorePrintScreen();
        StartsWithWindows = false;
#if DEBUG
        if (Environment.GetEnvironmentVariable("CLIPMARK_TEST_REGROOT") is { Length: > 0 })
            return; // test runs keep the developer's shortcut
#endif
        StartMenuShortcut.Remove();
    }
}
