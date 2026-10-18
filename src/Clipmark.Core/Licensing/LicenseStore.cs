using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clipmark.Core.Licensing;

/// <summary>
/// Contents of license.json: a v1 key awaiting activation and the server state: entitlement token, install id
/// (device key thumbprint), key store, times; the highest time seen (clock rollback guard) and the end of a stored server rate limit.
/// </summary>
public sealed record LicenseFile(string? Key, string? Entitlement = null, string? InstallId = null, string? KeyStore = null,
    DateTime? ActivatedUtc = null, DateTime? LastRefreshUtc = null, DateTime? HighWaterUtc = null, DateTime? RetryAfterUtc = null);

/// <summary>
/// JSON without reflection (Native AOT). A broken field reads as missing; unknown fields are ignored.
/// The v1 key is stored DPAPI-protected (CurrentUser) as keyProtected; a plain "key" from older files is still read.
/// </summary>
public static class LicenseStore
{
    private static readonly byte[] KeyEntropy = "Clipmark.license.key"u8.ToArray();

    public static string Serialize(LicenseFile file)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("keyProtected", Protect(file.Key));
            w.WriteString("entitlement", file.Entitlement);
            w.WriteString("installId", file.InstallId);
            w.WriteString("keyStore", file.KeyStore);
            w.WriteString("activatedUtc", file.ActivatedUtc?.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("lastRefreshUtc", file.LastRefreshUtc?.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("highWaterUtc", file.HighWaterUtc?.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("retryAfterUtc", file.RetryAfterUtc?.ToString("O", CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static LicenseFile Parse(string json)
    {
        var empty = new LicenseFile(null);
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

            string? NonEmpty(string name) => string.IsNullOrWhiteSpace(Str(name)) ? null : Str(name);
            string? key = Str("keyProtected") is { } blob ? Unprotect(blob) : NonEmpty("key");
            return new LicenseFile(key, NonEmpty("entitlement"), NonEmpty("installId"), NonEmpty("keyStore"),
                Utc("activatedUtc"), Utc("lastRefreshUtc"), Utc("highWaterUtc"), Utc("retryAfterUtc"));
        }
    }

    private static string? Protect(string? key)
    {
        if (key is null)
            return null;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), KeyEntropy, DataProtectionScope.CurrentUser));
    }

    /// <summary>Null when the blob is from another Windows user or machine (copied file) or is damaged.</summary>
    private static string? Unprotect(string blob)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            string key = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(blob), KeyEntropy, DataProtectionScope.CurrentUser));
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch (Exception e) when (e is CryptographicException or FormatException) { return null; }
    }

    public static LicenseFile Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : new LicenseFile(null);

    /// <summary>Writes a temp file first so a crash never leaves a half-written license file.</summary>
    public static void Save(string path, LicenseFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(file));
        File.Move(temp, path, overwrite: true);
    }
}
