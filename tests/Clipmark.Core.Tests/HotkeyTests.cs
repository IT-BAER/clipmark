using Clipmark.Core.Settings;

namespace Clipmark.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+PrintScreen", HotkeyModifiers.Control, 0x2C)]
    [InlineData("Ctrl+Shift+F8", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x77)]
    [InlineData("Win+Alt+S", HotkeyModifiers.Win | HotkeyModifiers.Alt, 0x53)]
    [InlineData("PrintScreen", HotkeyModifiers.None, 0x2C)]
    [InlineData("Shift+7", HotkeyModifiers.Shift, 0x37)]
    public void Text_round_trips(string text, HotkeyModifiers modifiers, int key)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(new Hotkey(modifiers, key), hotkey);
        Assert.Equal(text, hotkey.ToString());
    }

    [Fact]
    public void Modifier_order_and_case_are_normalized()
    {
        Assert.True(Hotkey.TryParse(" shift + ctrl + f8 ", out var hotkey));
        Assert.Equal("Ctrl+Shift+F8", hotkey.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+A+B")]
    public void Invalid_text_is_rejected(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void Key_names_cover_virtual_keys_used_by_the_settings_picker()
    {
        Assert.Equal("Ctrl+F24", new Hotkey(HotkeyModifiers.Control, 0x87).ToString());
        Assert.Null(Hotkey.KeyName(0x10)); // Shift alone is not a hotkey key
    }
}
