using Clipmark.Core.Editor;
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
        Assert.False(s.AiConsentRelay);
        Assert.False(s.CloseAfterCopy);
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
            AiModel = "claude-opus-5-5", AiConsent = true, AiConsentRelay = true,
            RedactEmail = false, RedactPhone = false, RedactIp = false, RedactBank = false, RedactSecrets = false, OcrRemoveLineBreaks = true, ColorFormat = ColorFormat.Hsl,
            CloseAfterCopy = true, RecentEmoji = "🔥 👍🏽 🏳️‍🌈",
        };
        Assert.Equal(s, SettingsStore.Parse(SettingsStore.Serialize(s)));
    }

    [Fact]
    public void Recent_emoji_start_with_the_Snipping_Tool_set_and_a_pick_moves_to_the_front()
    {
        var d = new ClipmarkSettings();
        Assert.Equal(18, d.Recent.Count);
        Assert.Equal(["❤️", "⭐", "❓"], d.Recent.Take(3));
        var s = d.WithRecent("😭").WithRecent("🦄");
        Assert.Equal(["🦄", "😭", "❤️"], s.Recent.Take(3));
        Assert.Equal(18, s.Recent.Count);
        Assert.Single(s.Recent, "😭");
    }

    [Fact]
    public void Recent_emoji_from_a_file_drop_empty_and_overlong_entries()
    {
        var s = SettingsStore.Parse("{ \"recentEmoji\": \"🔥  " + new string('x', 65) + " ⭐\" }");
        Assert.Equal(["🔥", "⭐"], s.Recent);
        Assert.Equal(new ClipmarkSettings().RecentEmoji, SettingsStore.Parse("""{ "recentEmoji": 5 }""").RecentEmoji);
    }

    [Fact]
    public void Text_action_options_default_and_old_files_keep_the_defaults()
    {
        var s = SettingsStore.Parse("""{ "autoSave": true }""");
        Assert.True(s.RedactEmail);
        Assert.True(s.RedactPhone);
        Assert.True(s.RedactIp);
        Assert.True(s.RedactBank);
        Assert.True(s.RedactSecrets);
        Assert.False(s.OcrRemoveLineBreaks);
        Assert.False(s.CloseAfterCopy);
    }

    [Fact]
    public void Color_format_defaults_to_hex_and_an_unknown_value_falls_back()
    {
        Assert.Equal(ColorFormat.Hex, SettingsStore.Parse("""{ "autoSave": true }""").ColorFormat);
        Assert.Equal(ColorFormat.Rgb, SettingsStore.Parse("""{ "colorFormat": "Rgb" }""").ColorFormat);
        Assert.Equal(ColorFormat.Hex, SettingsStore.Parse("""{ "colorFormat": "cmyk" }""").ColorFormat);
        Assert.Equal(ColorFormat.Hex, SettingsStore.Parse("""{ "colorFormat": "7" }""").ColorFormat);
    }

    [Fact]
    public void Unknown_model_id_is_kept_as_read()
    {
        var s = SettingsStore.Parse("""{ "aiModel": "claude-future-9", "aiConsent": true }""");
        Assert.Equal("claude-future-9", s.AiModel);
        Assert.True(s.AiConsent);
        Assert.False(s.AiConsentRelay);
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

    [Fact]
    public void Language_round_trips()
    {
        var s = new ClipmarkSettings { Language = "de-DE" };
        Assert.Equal("de-DE", SettingsStore.Parse(SettingsStore.Serialize(s)).Language);
    }

    [Fact]
    public void Missing_language_means_the_windows_default()
    {
        Assert.Null(new ClipmarkSettings().Language);
        Assert.Null(SettingsStore.Parse("""{ "hotkey": "Ctrl+F8" }""").Language);
    }

    [Theory]
    [InlineData("xx-INVALID")]
    [InlineData("de")]
    [InlineData("")]
    public void Unknown_language_reads_as_the_windows_default(string language)
    {
        Assert.Null(SettingsStore.Parse($$"""{ "language": "{{language}}" }""").Language);
    }

    [Fact]
    public void Ai_effort_round_trips_and_defaults_to_medium()
    {
        Assert.Equal("medium", new ClipmarkSettings().AiEffort);
        Assert.Equal("medium", SettingsStore.Parse("""{ "hotkey": "Ctrl+F8" }""").AiEffort);
        Assert.Equal("high", SettingsStore.Parse(SettingsStore.Serialize(new ClipmarkSettings { AiEffort = "high" })).AiEffort);
        Assert.Equal("low", SettingsStore.Parse("""{ "aiEffort": "low" }""").AiEffort);
        Assert.Equal("xhigh", SettingsStore.Parse("""{ "aiEffort": "xhigh" }""").AiEffort);
    }

    [Theory]
    [InlineData("max")]
    [InlineData("HIGH")]
    [InlineData("")]
    public void Unknown_ai_effort_reads_as_medium(string effort)
    {
        Assert.Equal("medium", SettingsStore.Parse($$"""{ "aiEffort": "{{effort}}" }""").AiEffort);
    }

    [Fact]
    public void Ui_languages_are_unique_tags_with_native_names()
    {
        Assert.Equal(16, UiLanguages.All.Count);
        Assert.Equal("en-US", UiLanguages.All[0].Tag);
        Assert.Equal(UiLanguages.All.Count, UiLanguages.All.Select(l => l.Tag).Distinct().Count());
        Assert.All(UiLanguages.All, l => Assert.True(UiLanguages.IsValid(l.Tag)));
        Assert.False(UiLanguages.IsValid(null));
    }

    [Fact]
    public void Border_theme_and_copy_edits_default_and_round_trip()
    {
        var d = new ClipmarkSettings();
        Assert.False(d.Border);
        Assert.Equal(new Rgba(0, 0, 0, 255), d.BorderColor);
        Assert.Equal(2, d.BorderWidth);
        Assert.Equal("system", d.Theme);
        Assert.True(d.CopyEdits);
        var back = SettingsStore.Parse(SettingsStore.Serialize(new ClipmarkSettings
            { Border = true, BorderColor = new Rgba(255, 128, 0, 255), BorderWidth = 7, Theme = "dark", CopyEdits = false }));
        Assert.True(back.Border);
        Assert.Equal(new Rgba(255, 128, 0, 255), back.BorderColor);
        Assert.Equal(7, back.BorderWidth);
        Assert.Equal("dark", back.Theme);
        Assert.False(back.CopyEdits);
        Assert.Equal("light", SettingsStore.Parse("""{ "theme": "light" }""").Theme);
    }

    [Theory]
    [InlineData("""{ "borderWidth": 0 }""", 1)]
    [InlineData("""{ "borderWidth": -4 }""", 1)]
    [InlineData("""{ "borderWidth": 99 }""", 20)]
    [InlineData("""{ "borderWidth": 20 }""", 20)]
    [InlineData("""{ "borderWidth": "x" }""", 2)]
    [InlineData("""{ "borderWidth": 2.5 }""", 2)]
    [InlineData("{}", 2)]
    public void Border_width_clamps_to_1_to_20_and_invalid_reads_as_2(string json, int expected) =>
        Assert.Equal(expected, SettingsStore.Parse(json).BorderWidth);

    [Theory]
    [InlineData("""{ "borderColor": "#GG0000" }""")]
    [InlineData("""{ "borderColor": "red" }""")]
    [InlineData("""{ "borderColor": "#12345" }""")]
    [InlineData("""{ "borderColor": 5 }""")]
    [InlineData("{}")]
    public void Invalid_border_color_reads_as_black(string json) =>
        Assert.Equal(new Rgba(0, 0, 0, 255), SettingsStore.Parse(json).BorderColor);

    [Fact]
    public void Border_color_reads_hex_in_either_case() =>
        Assert.Equal(new Rgba(0xAB, 0xCD, 0xEF, 255), SettingsStore.Parse("""{ "borderColor": "#abcdef" }""").BorderColor);

    [Theory]
    [InlineData("""{ "theme": "Dark" }""")]
    [InlineData("""{ "theme": "blue" }""")]
    [InlineData("""{ "theme": 1 }""")]
    [InlineData("{}")]
    public void Unknown_or_missing_theme_reads_as_system(string json) =>
        Assert.Equal("system", SettingsStore.Parse(json).Theme);

    [Fact]
    public void Missing_copy_edits_reads_as_on() => Assert.True(SettingsStore.Parse("""{ "autoSave": true }""").CopyEdits);
}
