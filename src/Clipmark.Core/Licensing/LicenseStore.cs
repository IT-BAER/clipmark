using System.Globalization;
using System.Text.Json;

namespace Clipmark.Core.Licensing;

/// <summary>
/// Contents of license.json (and of each local trial copy): a v1 key awaiting activation, the local trial with its
/// <see cref="TrialSeal"/> tag, and the server state: entitlement token, install id (device key thumbprint), key store, times.
/// </summary>
public sealed record LicenseFile(string? Key, TrialState? Trial, string? TrialTag = null, string? Entitlement = null,
    string? InstallId = null, string? KeyStore = null, DateTime? ActivatedUtc = null, DateTime? LastRefreshUtc = null);

/// <summary>JSON without reflection (Native AOT). A broken key or trial field reads as missing.</summary>
public static class LicenseStore
{
    public static string Serialize(LicenseFile file)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            // The local trial is a migration path only; a server entitlement replaces it.
            var trial = file.Entitlement is null ? file.Trial : null;
            w.WriteString("key", file.Key);
            w.WriteString("trialStarted", trial?.StartedUtc.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("trialLastSeen", trial?.LastSeenUtc.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("trialTag", file.Entitlement is null ? file.TrialTag : null);
            w.WriteString("entitlement", file.Entitlement);
            w.WriteString("installId", file.InstallId);
            w.WriteString("keyStore", file.KeyStore);
            w.WriteString("activatedUtc", file.ActivatedUtc?.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("lastRefreshUtc", file.LastRefreshUtc?.ToString("O", CultureInfo.InvariantCulture));
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
            string? NonEmpty(string name) => string.IsNullOrWhiteSpace(Str(name)) ? null : Str(name);
            return new LicenseFile(key, trial, Str("trialTag"), NonEmpty("entitlement"), NonEmpty("installId"), NonEmpty("keyStore"),
                Utc("activatedUtc"), Utc("lastRefreshUtc"));
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
