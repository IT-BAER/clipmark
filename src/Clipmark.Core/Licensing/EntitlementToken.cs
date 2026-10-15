using System.Security.Cryptography;
using System.Text.Json;

namespace Clipmark.Core.Licensing;

/// <summary>Result of an offline entitlement check; anything but <see cref="Valid"/> means not licensed.</summary>
public enum TokenVerdict { Valid, Expired, IssuedInFuture, UnknownKid, WrongApp, WrongDev, BadSignature, Malformed }

/// <summary>The signed payload of an entitlement (releash spec 18.5). Times are Unix seconds; <see cref="Grace"/> is seconds.</summary>
public sealed record EntitlementClaims(string Kid, string App, string Lic, string Plan, string[] Feat, int Seats, string Dev, long Iat, long Exp, long Grace);

/// <summary>
/// Verifies <c>rl1.&lt;payload&gt;.&lt;sig&gt;</c>: base64url without padding, ECDSA P-256/SHA-256 over the raw payload bytes,
/// 64-byte IEEE P1363 signature. Order: parse, signature, app, dev, iat, exp; the first failure decides.
/// </summary>
public static class EntitlementToken
{
    public const string Prefix = "rl1";

    /// <summary>Tolerance for a server clock ahead of this PC.</summary>
    public const long IssuedSkewSeconds = 300;

    public static TokenVerdict Verify(string? token, IReadOnlyDictionary<string, ECDsa> keys, string app, string dev, long nowUnix, out EntitlementClaims? claims)
    {
        claims = null;
        if (!TrySplit(token, out byte[] payload, out byte[] signature) || Parse(payload) is not { } parsed)
            return TokenVerdict.Malformed;
        if (!keys.TryGetValue(parsed.Kid, out var key))
            return TokenVerdict.UnknownKid;
        if (signature.Length != 64 || !key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            return TokenVerdict.BadSignature;
        if (parsed.App != app)
            return TokenVerdict.WrongApp;
        if (parsed.Dev != dev)
            return TokenVerdict.WrongDev;
        if (parsed.Iat > nowUnix + IssuedSkewSeconds)
            return TokenVerdict.IssuedInFuture;
        if (nowUnix > parsed.Exp + parsed.Grace)
            return TokenVerdict.Expired;
        claims = parsed;
        return TokenVerdict.Valid;
    }

    /// <summary>The claims without any check, for scheduling and diagnostics only; never unlocks anything.</summary>
    public static EntitlementClaims? ReadClaims(string? token) =>
        TrySplit(token, out byte[] payload, out _) ? Parse(payload) : null;

    /// <summary>The vector's verdict name, e.g. "issued_in_future".</summary>
    public static string Name(TokenVerdict verdict) => verdict switch
    {
        TokenVerdict.Valid => "valid",
        TokenVerdict.Expired => "expired",
        TokenVerdict.IssuedInFuture => "issued_in_future",
        TokenVerdict.UnknownKid => "unknown_kid",
        TokenVerdict.WrongApp => "wrong_app",
        TokenVerdict.WrongDev => "wrong_dev",
        TokenVerdict.BadSignature => "bad_signature",
        _ => "malformed",
    };

    /// <summary>kid to public key from base64 SubjectPublicKeyInfo.</summary>
    public static Dictionary<string, ECDsa> LoadKeys(IReadOnlyDictionary<string, string> spki)
    {
        var keys = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
        foreach (var (kid, text) in spki)
        {
            var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(text), out _);
            keys[kid] = key;
        }
        return keys;
    }

    private static bool TrySplit(string? token, out byte[] payload, out byte[] signature)
    {
        payload = signature = [];
        string[] parts = (token ?? "").Split('.');
        if (parts.Length != 3 || parts[0] != Prefix || Base64UrlDecode(parts[1]) is not { } p || Base64UrlDecode(parts[2]) is not { } s)
            return false;
        (payload, signature) = (p, s);
        return true;
    }

    /// <summary>Strict base64url: URL alphabet only, no padding; null otherwise.</summary>
    private static byte[]? Base64UrlDecode(string text)
    {
        if (text.Length == 0 || text.Length % 4 == 1)
            return null;
        foreach (char c in text)
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
                return null;
        string standard = text.Replace('-', '+').Replace('_', '/');
        try { return Convert.FromBase64String(standard.PadRight(standard.Length + (4 - standard.Length % 4) % 4, '=')); }
        catch (FormatException) { return null; }
    }

    private static EntitlementClaims? Parse(byte[] payload)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(payload); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || Long(r, "v") != 1
                || Str(r, "kid") is not { } kid || Str(r, "app") is not { } app || Str(r, "lic") is not { } lic
                || Str(r, "plan") is not { } plan || Str(r, "dev") is not { } dev
                || Long(r, "seats") is not { } seats || seats is < 0 or > int.MaxValue
                || Long(r, "iat") is not { } iat || Long(r, "exp") is not { } exp || Long(r, "grace") is not { } grace || grace < 0
                || Features(r) is not { } feat)
                return null;
            return new EntitlementClaims(kid, app, lic, plan, feat, (int)seats, dev, iat, exp, grace);
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : null;

    /// <summary>A string array; null or missing reads as no features (a trial without trial_features).</summary>
    private static string[]? Features(JsonElement e)
    {
        if (!e.TryGetProperty("feat", out var v) || v.ValueKind == JsonValueKind.Null)
            return [];
        if (v.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return null;
            list.Add(item.GetString()!);
        }
        return [.. list];
    }
}
