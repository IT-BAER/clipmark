using Clipmark.Core.Ai;
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
        Assert.Equal("Ctrl+Shift+PrintScreen", s.RecordHotkey.ToString());
        Assert.True(s.RecordSystemAudio);
        Assert.False(s.RecordMicrophone);
        Assert.False(s.HistoryEnabled);
        Assert.Equal(AiModels.Default.Id, s.AiModel);
        Assert.False(s.AiConsent);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var s = new ClipmarkSettings
        {
            Hotkey = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x77),
            Mode = CaptureMode.Freeform, DelaySeconds = 5, CopyToClipboard = true,
            AutoSave = true, SaveFolder = @"D:\Shots", ShowToast = true,
            RecordHotkey = new Hotkey(HotkeyModifiers.Alt, 0x52), RecordSystemAudio = false, RecordMicrophone = true, HistoryEnabled = true,
            AiModel = "claude-opus-5-5", AiConsent = true,
        };
        Assert.Equal(s, SettingsStore.Parse(SettingsStore.Serialize(s)));
    }

    [Fact]
    public void Unknown_model_id_is_kept_as_read()
    {
        var s = SettingsStore.Parse("""{ "aiModel": "claude-future-9", "aiConsent": true }""");
        Assert.Equal("claude-future-9", s.AiModel);
        Assert.True(s.AiConsent);
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

    [Theory]
    [InlineData(null)]
    [InlineData("absent")]
    [InlineData("0")]
    [InlineData("1")]
    public void Print_screen_restore_value_round_trips(string? restore)
    {
        var s = new ClipmarkSettings { PrintScreenRestore = restore };
        Assert.Equal(restore, SettingsStore.Parse(SettingsStore.Serialize(s)).PrintScreenRestore);
    }

    [Fact]
    public void Print_screen_token_maps_to_the_registry_value()
    {
        Assert.Null(PrintScreenToken.ValueOf(PrintScreenToken.Absent));
        Assert.Equal(1, PrintScreenToken.ValueOf("1"));
    }

    [Fact]
    public void Unknown_print_screen_restore_value_is_ignored()
    {
        Assert.Null(SettingsStore.Parse("""{ "printScreenRestore": "maybe" }""").PrintScreenRestore);
    }

    [Fact]
    public void Settings_from_before_recording_get_recording_defaults()
    {
        var s = SettingsStore.Parse("""{ "hotkey": "Ctrl+F8", "mode": "Window" }""");
        Assert.Equal(new ClipmarkSettings().RecordHotkey, s.RecordHotkey);
        Assert.True(s.RecordSystemAudio);
        Assert.False(s.RecordMicrophone);
        Assert.False(s.HistoryEnabled);
        Assert.Equal(AiModels.Default.Id, s.AiModel);
        Assert.False(s.AiConsent);
    }

    [Fact]
    public void Invalid_fields_fall_back_one_by_one()
    {
        var s = SettingsStore.Parse("""{ "hotkey": "Ctrl+Banana", "mode": "Lasso", "delaySeconds": 7, "autoSave": true, "saveFolder": "" }""");
        Assert.Equal(new ClipmarkSettings { AutoSave = true }, s);
    }
}
