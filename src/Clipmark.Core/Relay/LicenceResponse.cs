using System.Globalization;
using System.Text.Json;
using Clipmark.Core.Ai;

namespace Clipmark.Core.Relay;

/// <summary>201/200 of activate. <see cref="EvictedInstallId"/> is set when another PC lost its seat.</summary>
public sealed record ActivateResult(string Entitlement, string InstallId, string LicenceId, string Plan, int Seats, DateTimeOffset? ExpiresAt, string? EvictedInstallId);

/// <summary>200 of trial.</summary>
public sealed record TrialResult(string Entitlement, DateTimeOffset? EndsAt);

/// <summary>Success bodies of the licence endpoints. A body without a non-empty entitlement is no result (never trust a bare 2xx).</summary>
public static class LicenceResponse
{
    public static ActivateResult? ParseActivate(string json) => Read(json, (root, token) =>
    {
        var lic = root.TryGetProperty("licence", out var l) && l.ValueKind == JsonValueKind.Object ? l : default;
        return new ActivateResult(token, ChatStreamEvent.Str(root, "install_id") ?? "",
            lic.ValueKind == JsonValueKind.Object ? ChatStreamEvent.Str(lic, "id") ?? "" : "",
            lic.ValueKind == JsonValueKind.Object ? ChatStreamEvent.Str(lic, "plan") ?? "" : "",
            RelayRegistration.Int(lic, "seats"),
            lic.ValueKind == JsonValueKind.Object ? Time(lic, "expires_at") : null,
            ChatStreamEvent.Str(root, "evicted_install_id") is { Length: > 0 } evicted ? evicted : null);
    });

    public static TrialResult? ParseTrial(string json) => Read(json, (root, token) => new TrialResult(token, Time(root, "ends_at")));

    public static string? ParseEntitlement(string json) => Read(json, (_, token) => token);

    private static T? Read<T>(string json, Func<JsonElement, string, T> map) where T : class
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object && ChatStreamEvent.Str(root, "entitlement") is { Length: > 0 } token
                ? map(root, token) : null;
        }
    }

    private static DateTimeOffset? Time(JsonElement e, string name) =>
        DateTimeOffset.TryParse(ChatStreamEvent.Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
