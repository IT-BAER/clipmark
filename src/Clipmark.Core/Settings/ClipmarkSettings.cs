using System.Text.Json;
using Clipmark.Core.Ai;

namespace Clipmark.Core.Settings;

public enum CaptureMode { Rectangle, Window, FullScreen, AllMonitors, Freeform }

public sealed record ClipmarkSettings
{
    public static readonly int[] DelayChoices = [0, 3, 5, 10];

    public Hotkey Hotkey { get; init; } = new(HotkeyModifiers.Control, 0x2C);
    public CaptureMode Mode { get; init; } = CaptureMode.Rectangle;
    public int DelaySeconds { get; init; }
    public bool CopyToClipboard { get; init; }
    public bool AutoSave { get; init; }

    /// <summary>Null = the app default (Pictures\Screenshots).</summary>
    public string? SaveFolder { get; init; }

    public bool ShowToast { get; init; }

    public Hotkey RecordHotkey { get; init; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x2C);
    public bool RecordSystemAudio { get; init; } = true;
    public bool RecordMicrophone { get; init; }

    /// <summary>Keep an encrypted history of recent captures (Premium, opt-in).</summary>
    public bool HistoryEnabled { get; init; }

    /// <summary>Model id for Ask AI; kept as read, resolve it with <see cref="AiModels.Find"/>.</summary>
    public string AiModel { get; init; } = AiModels.Default.Id;

    /// <summary>The user accepted sending captures to Anthropic.</summary>
    public bool AiConsent { get; init; }

    /// <summary>Set while Clipmark owns Print Screen: the Windows option's value before (see <see cref="PrintScreenToken"/>).</summary>
    public string? PrintScreenRestore { get; init; }

    /// <summary>UI language tag from <see cref="UiLanguages"/>; null = the Windows display language.</summary>
    public string? Language { get; init; }
}

/// <summary>The languages the UI offers: BCP-47 tag and the language's own name.</summary>
public static class UiLanguages
{
    public static readonly IReadOnlyList<(string Tag, string NativeName)> All =
    [
        ("en-US", "English"), ("de-DE", "Deutsch"), ("fr-FR", "Français"), ("ja-JP", "日本語"), ("zh-Hans", "简体中文"),
        ("es-ES", "Español"), ("it-IT", "Italiano"), ("pt-BR", "Português (Brasil)"), ("ru-RU", "Русский"), ("pl-PL", "Polski"),
        ("nl-NL", "Nederlands"), ("ko-KR", "한국어"), ("zh-Hant", "繁體中文"), ("tr-TR", "Türkçe"), ("sv-SE", "Svenska"),
        ("uk-UA", "Українська"),
    ];

    public static bool IsValid(string? tag) => All.Any(l => l.Tag == tag);
}

/// <summary>The Windows "Print Screen opens screen capture" registry value as stored for restoring: "absent" or the number.</summary>
public static class PrintScreenToken
{
    public const string Absent = "absent";

    /// <summary>The DWORD to write back, or null to delete the value.</summary>
    public static int? ValueOf(string token) => int.TryParse(token, System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : null;

    public static bool IsValid(string? token) => token == Absent || int.TryParse(token, System.Globalization.CultureInfo.InvariantCulture, out _);
}

/// <summary>JSON without reflection (Native AOT). Each invalid or missing field falls back to its default.</summary>
public static class SettingsStore
{
    public static string Serialize(ClipmarkSettings s)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions
            {
                Indented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keeps "Ctrl+X" readable
            }))
        {
            w.WriteStartObject();
            w.WriteString("hotkey", s.Hotkey.ToString());
            w.WriteString("mode", s.Mode.ToString());
            w.WriteNumber("delaySeconds", s.DelaySeconds);
            w.WriteBoolean("copyToClipboard", s.CopyToClipboard);
            w.WriteBoolean("autoSave", s.AutoSave);
            w.WriteString("saveFolder", s.SaveFolder);
            w.WriteBoolean("showToast", s.ShowToast);
            w.WriteString("recordHotkey", s.RecordHotkey.ToString());
            w.WriteBoolean("recordSystemAudio", s.RecordSystemAudio);
            w.WriteBoolean("recordMicrophone", s.RecordMicrophone);
            w.WriteBoolean("historyEnabled", s.HistoryEnabled);
            w.WriteString("aiModel", s.AiModel);
            w.WriteBoolean("aiConsent", s.AiConsent);
            w.WriteString("printScreenRestore", s.PrintScreenRestore);
            w.WriteString("language", s.Language);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static ClipmarkSettings Parse(string json)
    {
        var d = new ClipmarkSettings();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return d; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return d;

            string? Str(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            bool Bool(string name, bool fallback) =>
                root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

            int delay = root.TryGetProperty("delaySeconds", out var dv) && dv.TryGetInt32(out var n)
                && ClipmarkSettings.DelayChoices.Contains(n) ? n : d.DelaySeconds;
            return new ClipmarkSettings
            {
                Hotkey = Hotkey.TryParse(Str("hotkey"), out var hk) ? hk : d.Hotkey,
                Mode = Enum.TryParse<CaptureMode>(Str("mode"), out var mode) && Enum.IsDefined(mode) ? mode : d.Mode,
                DelaySeconds = delay,
                CopyToClipboard = Bool("copyToClipboard", d.CopyToClipboard),
                AutoSave = Bool("autoSave", d.AutoSave),
                SaveFolder = string.IsNullOrWhiteSpace(Str("saveFolder")) ? null : Str("saveFolder"),
                ShowToast = Bool("showToast", d.ShowToast),
                RecordHotkey = Hotkey.TryParse(Str("recordHotkey"), out var rhk) ? rhk : d.RecordHotkey,
                RecordSystemAudio = Bool("recordSystemAudio", d.RecordSystemAudio),
                RecordMicrophone = Bool("recordMicrophone", d.RecordMicrophone),
                HistoryEnabled = Bool("historyEnabled", d.HistoryEnabled),
                AiModel = string.IsNullOrWhiteSpace(Str("aiModel")) ? d.AiModel : Str("aiModel")!,
                AiConsent = Bool("aiConsent", d.AiConsent),
                PrintScreenRestore = PrintScreenToken.IsValid(Str("printScreenRestore")) ? Str("printScreenRestore") : null,
                Language = UiLanguages.IsValid(Str("language")) ? Str("language") : null,
            };
        }
    }

    public static ClipmarkSettings Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : new ClipmarkSettings();

    /// <summary>Writes a temp file first so a crash never leaves a half-written settings file.</summary>
    public static void Save(string path, ClipmarkSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(s));
        File.Move(temp, path, overwrite: true);
    }
}
