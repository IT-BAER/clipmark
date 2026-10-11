using System.Text.Json;

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
