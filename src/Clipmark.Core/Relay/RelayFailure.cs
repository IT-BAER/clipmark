using System.Globalization;
using System.Text.Json;
using Clipmark.Core.Ai;

namespace Clipmark.Core.Relay;

/// <summary>One rejected relay call: the error catalogue fields (releash spec 10) and the sentence shown to the user. Never throws on a malformed body.</summary>
public sealed record RelayFailure(int Status, string? Code, string? Detail, int? RetryAfter, DateTimeOffset? ResetsAt, long? ServerTime)
{
    public static RelayFailure Parse(int status, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new RelayFailure(status, null, null, null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var e) || e.ValueKind != JsonValueKind.Object)
                return new RelayFailure(status, null, null, null, null, null);
            int? retry = e.TryGetProperty("retry_after", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt32(out int seconds) ? seconds : null;
            long? serverTime = e.TryGetProperty("server_time", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out long unix) ? unix : null;
            DateTimeOffset? resets = DateTimeOffset.TryParse(ChatStreamEvent.Str(e, "resets_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;
            return new RelayFailure(status, ChatStreamEvent.Str(e, "code"), ChatStreamEvent.Str(e, "message"), retry, resets, serverTime);
        }
        catch (JsonException)
        {
            return new RelayFailure(status, null, null, null, null, null);
        }
    }

    /// <summary>The install row is gone or was evicted: register again with the same key, then retry once.</summary>
    public bool NeedsRegistration => Code is "unknown_install" or "install_evicted";

    /// <summary>The server rejected the timestamp; <see cref="ServerTime"/> allows one retry with an offset.</summary>
    public bool IsClockSkew => Code == "clock_skew";

    public string Message() => Code switch
    {
        "quota_rpm" or "quota_rpd" or "rate_limited" => RetryAfter is { } wait
            ? string.Format(CultureInfo.CurrentCulture, CoreText.T("RelayLimitWait", "Relay limit reached. Try again in {0} s."), wait)
            : CoreText.T("RelayLimit", "Relay limit reached. Try again shortly."),
        "quota_budget" => ResetsAt is { } at
            ? string.Format(CultureInfo.CurrentCulture, CoreText.T("RelayBudgetUntil", "Your monthly Premium AI budget is used up until {0}."), at.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
            : CoreText.T("RelayBudget", "Your monthly Premium AI budget is used up for this month."),
        "app_budget_exhausted" => CoreText.T("RelayAppBudget", "The Clipmark relay has reached its monthly budget. Use your own API key or try again next month."),
        "upstream_error" or "upstream_unavailable" => CoreText.T("ApiOverloaded", "Anthropic is overloaded or unavailable. Try again shortly."),
        "storage_unavailable" => CoreText.T("RelayUnavailable", "The Clipmark relay is temporarily unavailable. Try again shortly."),
        "subject_revoked" or "licence_invalid" => CoreText.T("RelayLicence", "This licence cannot use the relay. Check your licence in Settings."),
        "install_revoked" => CoreText.T("RelayInstallRevoked", "This PC was blocked from the relay. Use your own API key or contact support."),
        "unknown_install" or "install_evicted" => CoreText.T("RelayNotRegistered", "This PC could not be registered with the relay. Try again."),
        "clock_skew" => CoreText.T("RelayClock", "The clock of this PC is off. Check the Windows date and time, then try again."),
        "body_too_large" => CoreText.T("AiCaptureTooLarge", "The capture is too large to send."),
        "input_invalid" or "body_invalid" or "model_not_allowed" or "task_not_found" or "app_disabled" => string.IsNullOrWhiteSpace(Detail)
            ? CoreText.T("ApiRejected", "Request rejected.")
            : string.Format(CultureInfo.CurrentCulture, CoreText.T("ApiRejectedWith", "Request rejected: {0}"), Detail),
        "replay" or "signature_invalid" or "envelope_invalid" => string.Format(CultureInfo.CurrentCulture, CoreText.T("RelayRejected", "The relay rejected the request ({0}). Try again."), Code),
        _ => string.Format(CultureInfo.CurrentCulture, CoreText.T("ApiRequestFailed", "Request failed (HTTP {0})."), Status),
    };
}
