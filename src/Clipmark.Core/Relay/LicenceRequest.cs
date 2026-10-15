using System.Text.Json;
using Clipmark.Core.Licensing;

namespace Clipmark.Core.Relay;

/// <summary>Bodies of the releash licence endpoints (spec 18.6). Platform is always windows; no device label is sent.</summary>
public static class LicenceRequest
{
    public const string ActivatePath = "/v1/licences/" + RelayRequest.App + "/activate";
    public const string EntitlementPath = "/v1/licences/" + RelayRequest.App + "/entitlement";
    public const string DeactivatePath = "/v1/licences/" + RelayRequest.App + "/deactivate";
    public const string TrialPath = "/v1/licences/" + RelayRequest.App + "/trial";

    /// <summary>Body of entitlement and deactivate; a new array per call.</summary>
    public static byte[] EmptyObject => "{}"u8.ToArray();

    /// <summary>False only for text that decodes to a v1 key (76 bytes); a code is 20 bytes, and anything else goes as a code so the server answers code_invalid.</summary>
    public static bool IsActivationCode(string text) => LicenseKey.DecodedLength(text) != LicenseKey.KeyLength;

    public static byte[] BuildActivate(string codeOrKey, string x, string y, string keyStore, string appVersion)
    {
        string text = codeOrKey.Trim();
        return Build(w => w.WriteString(IsActivationCode(text) ? "code" : "licence_key", text), x, y, keyStore, appVersion);
    }

    public static byte[] BuildTrial(string x, string y, string keyStore, string appVersion) => Build(null, x, y, keyStore, appVersion);

    private static byte[] Build(Action<Utf8JsonWriter>? secret, string x, string y, string keyStore, string appVersion)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("platform", "windows");
            secret?.Invoke(w);
            w.WriteStartObject("public_key");
            w.WriteString("kty", "EC");
            w.WriteString("crv", "P-256");
            w.WriteString("x", x);
            w.WriteString("y", y);
            w.WriteEndObject();
            w.WriteString("key_store", keyStore);
            w.WriteString("app_version", appVersion);
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
