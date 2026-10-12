using System.Globalization;
using System.Text.Json;

namespace Clipmark.Core.Licensing;

/// <summary>Contents of license.json: the entered key and the trial timestamps.</summary>
public sealed record LicenseFile(string? Key, TrialState? Trial);

/// <summary>JSON without reflection (Native AOT). A broken key or trial field reads as missing.</summary>
public static class LicenseStore
{
    public static string Serialize(LicenseFile file)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("key", file.Key);
            w.WriteString("trialStarted", file.Trial?.StartedUtc.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("trialLastSeen", file.Trial?.LastSeenUtc.ToString("O", CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static LicenseFile Parse(string json)
    {
        var empty = new LicenseFile(null, null);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return empty; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return empty;

            string? Str(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            DateTime? Utc(string name) =>
                DateTimeOffset.TryParse(Str(name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.UtcDateTime : null;

            string? key = string.IsNullOrWhiteSpace(Str("key")) ? null : Str("key");
            var trial = Utc("trialStarted") is { } started && Utc("trialLastSeen") is { } lastSeen
                ? new TrialState(started, lastSeen) : null;
            return new LicenseFile(key, trial);
        }
    }

    public static LicenseFile Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : new LicenseFile(null, null);

    /// <summary>Writes a temp file first so a crash never leaves a half-written license file.</summary>
    public static void Save(string path, LicenseFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(file));
        File.Move(temp, path, overwrite: true);
    }
}
