namespace Clipmark.Core.Settings;

/// <summary>Values match the Win32 MOD_* flags of RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>Global hotkey: modifiers plus a Win32 virtual-key code. Text form "Win+Ctrl+Alt+Shift+Key".</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int Key)
{
    private static readonly (HotkeyModifiers Flag, string Name)[] ModifierNames =
        [(HotkeyModifiers.Win, "Win"), (HotkeyModifiers.Control, "Ctrl"), (HotkeyModifiers.Alt, "Alt"), (HotkeyModifiers.Shift, "Shift")];

    private static readonly Dictionary<int, string> SpecialKeys = new()
    {
        [0x13] = "Pause", [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End",
        [0x24] = "Home", [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete",
    };

    /// <summary>Display name of a virtual key, or null when it cannot be a hotkey key.</summary>
    public static string? KeyName(int key) => key switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F),
        _ => SpecialKeys.GetValueOrDefault(key),
    };

    private static int? KeyFromName(string name)
    {
        for (int key = 0; key <= 0xFF; key++)
            if (string.Equals(KeyName(key), name, StringComparison.OrdinalIgnoreCase))
                return key;
        return null;
    }

    public override string ToString()
    {
        var modifiers = Modifiers;
        var parts = ModifierNames.Where(m => modifiers.HasFlag(m.Flag)).Select(m => m.Name).ToList();
        parts.Add(KeyName(Key) ?? $"0x{Key:X2}");
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        var parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries);
        if (KeyFromName(parts[^1]) is not { } key)
            return false;
        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var match = ModifierNames.FirstOrDefault(m => string.Equals(m.Name, part, StringComparison.OrdinalIgnoreCase));
            if (match.Name is null)
                return false;
            modifiers |= match.Flag;
        }
        hotkey = new Hotkey(modifiers, key);
        return true;
    }
}
