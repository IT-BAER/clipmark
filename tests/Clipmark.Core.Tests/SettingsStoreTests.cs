using Clipmark.Core.Settings;

namespace Clipmark.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Defaults_match_the_m2_decisions()
    {
        var s = new ClipmarkSettings();
        Assert.Equal("Ctrl+PrintScreen", s.Hotkey.ToString());
        Assert.Equal(CaptureMode.Rectangle, s.Mode);
        Assert.Equal(0, s.DelaySeconds);
        Assert.False(s.CopyToClipboard);
        Assert.False(s.AutoSave);
        Assert.Null(s.SaveFolder);
        Assert.False(s.ShowToast);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var s = new ClipmarkSettings
        {
            Hotkey = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x77),
            Mode = CaptureMode.Freeform, DelaySeconds = 5, CopyToClipboard = true,
            AutoSave = true, SaveFolder = @"D:\Shots", ShowToast = true,
        };
        Assert.Equal(s, SettingsStore.Parse(SettingsStore.Serialize(s)));
    }

    [Fact]
    public void Json_is_human_readable()
    {
        var json = SettingsStore.Serialize(new ClipmarkSettings());
        Assert.Contains("\"hotkey\": \"Ctrl+PrintScreen\"", json);
        Assert.Contains("\"mode\": \"Rectangle\"", json);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    public void Broken_file_gives_defaults(string json)
    {
        Assert.Equal(new ClipmarkSettings(), SettingsStore.Parse(json));
    }

    [Fact]
    public void Invalid_fields_fall_back_one_by_one()
    {
        var s = SettingsStore.Parse("""{ "hotkey": "Ctrl+Banana", "mode": "Lasso", "delaySeconds": 7, "autoSave": true, "saveFolder": "" }""");
        Assert.Equal(new ClipmarkSettings { AutoSave = true }, s);
    }
}
