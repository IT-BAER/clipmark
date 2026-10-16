using System.Text.Json;
using Clipmark.Core.Ai;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Settings;

public enum CaptureMode { Rectangle, Window, FullScreen, AllMonitors, Freeform }

public enum ColorFormat { Hex, Rgb, Hsl }

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

    /// <summary>Ask AI <c>output_config.effort</c>, one of <see cref="AiEfforts.All"/>.</summary>
    public string AiEffort { get; init; } = AiEfforts.Default;

    /// <summary>The user accepted sending captures to Anthropic.</summary>
    public bool AiConsent { get; init; }

    /// <summary>The user accepted sending captures through IT-BAER's relay to Anthropic (Premium without an own key).</summary>
    public bool AiConsentRelay { get; init; }

    /// <summary>Set while Clipmark owns Print Screen: the Windows option's value before (see <see cref="PrintScreenToken"/>).</summary>
    public string? PrintScreenRestore { get; init; }

    /// <summary>UI language tag from <see cref="UiLanguages"/>; null = the Windows display language.</summary>
    public string? Language { get; init; }

    /// <summary>Quick redact covers email addresses.</summary>
    public bool RedactEmail { get; init; } = true;

    /// <summary>Quick redact covers phone numbers.</summary>
    public bool RedactPhone { get; init; } = true;

    /// <summary>Text extractor joins lines with spaces instead of line breaks.</summary>
    public bool OcrRemoveLineBreaks { get; init; }

    /// <summary>Every capture gets a border of <see cref="BorderColor"/> and <see cref="BorderWidth"/> px around it.</summary>
    public bool Border { get; init; }

    public Rgba BorderColor { get; init; } = new(0, 0, 0, 255);

    /// <summary>1 to 20 px.</summary>
    public int BorderWidth { get; init; } = 2;

    /// <summary>One of <see cref="Themes"/>: "system" follows Windows.</summary>
    public string Theme { get; init; } = "system";

    /// <summary>Edits in the editor are copied to the clipboard after a short pause.</summary>
    public bool CopyEdits { get; init; } = true;

    /// <summary>The window closes to the tray after the capture is copied (Copy, Ctrl+C, or the copy after a capture).</summary>
    public bool CloseAfterCopy { get; init; }

    /// <summary>How a picked color is copied.</summary>
    public ColorFormat ColorFormat { get; init; }

    public static readonly string[] Themes = ["system", "light", "dark"];
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
            w.WriteString("aiEffort", s.AiEffort);
            w.WriteBoolean("aiConsent", s.AiConsent);
            w.WriteBoolean("aiConsentRelay", s.AiConsentRelay);
            w.WriteString("printScreenRestore", s.PrintScreenRestore);
            w.WriteString("language", s.Language);
            w.WriteBoolean("redactEmail", s.RedactEmail);
            w.WriteBoolean("redactPhone", s.RedactPhone);
            w.WriteBoolean("ocrRemoveLineBreaks", s.OcrRemoveLineBreaks);
            w.WriteBoolean("border", s.Border);
            w.WriteString("borderColor", ColorSample.Hex(s.BorderColor));
            w.WriteNumber("borderWidth", s.BorderWidth);
            w.WriteString("theme", s.Theme);
            w.WriteBoolean("copyEdits", s.CopyEdits);
            w.WriteBoolean("closeAfterCopy", s.CloseAfterCopy);
            w.WriteString("colorFormat", s.ColorFormat.ToString());
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
                AiEffort = AiEfforts.All.Contains(Str("aiEffort")) ? Str("aiEffort")! : d.AiEffort,
                AiConsent = Bool("aiConsent", d.AiConsent),
                AiConsentRelay = Bool("aiConsentRelay", d.AiConsentRelay),
                PrintScreenRestore = PrintScreenToken.IsValid(Str("printScreenRestore")) ? Str("printScreenRestore") : null,
                Language = UiLanguages.IsValid(Str("language")) ? Str("language") : null,
                RedactEmail = Bool("redactEmail", d.RedactEmail),
                RedactPhone = Bool("redactPhone", d.RedactPhone),
                OcrRemoveLineBreaks = Bool("ocrRemoveLineBreaks", d.OcrRemoveLineBreaks),
                Border = Bool("border", d.Border),
                BorderColor = Str("borderColor") is { Length: 7 } hex && hex[0] == '#'
                    && int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out int rgb)
                    ? new Rgba((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255) : d.BorderColor,
                BorderWidth = root.TryGetProperty("borderWidth", out var bw) && bw.ValueKind == JsonValueKind.Number && bw.TryGetInt32(out int width) ? Math.Clamp(width, 1, 20) : d.BorderWidth,
                Theme = ClipmarkSettings.Themes.Contains(Str("theme")) ? Str("theme")! : d.Theme,
                CopyEdits = Bool("copyEdits", d.CopyEdits),
                CloseAfterCopy = Bool("closeAfterCopy", d.CloseAfterCopy),
                ColorFormat = Enum.TryParse<ColorFormat>(Str("colorFormat"), out var colorFormat) && Enum.IsDefined(colorFormat) ? colorFormat : d.ColorFormat,
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
