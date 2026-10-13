using System.Globalization;
using System.Text.Json;

namespace Clipmark.Core.Ai;

/// <summary>Short messages (English or via <see cref="CoreText"/>) for failed Messages API calls; never throws on a malformed body.</summary>
public static class ApiError
{
    private static string Overloaded => CoreText.T("ApiOverloaded", "Anthropic is overloaded or unavailable. Try again shortly.");
    private static string RateLimited => CoreText.T("ApiRateLimited", "Rate limit reached. Try again shortly.");

    public static string Message(int status, string? body, TimeSpan? retryAfter) => status switch
    {
        400 => Rejected(BodyMessage(body)),
        401 => CoreText.T("ApiKeyInvalid", "The API key is not valid. Check it in Settings > AI."),
        403 => CoreText.T("ApiForbidden", "This API key cannot use this model or feature."),
        404 => CoreText.T("ApiModelUnavailable", "This model is not available. Choose another model in Settings > AI."),
        413 => CoreText.T("AiCaptureTooLarge", "The capture is too large to send."),
        429 => retryAfter is { } wait
            ? string.Format(CultureInfo.CurrentCulture, CoreText.T("ApiRateLimitedWait", "Rate limit reached. Try again in {0} s."), (int)Math.Ceiling(wait.TotalSeconds))
            : RateLimited,
        500 or 502 or 503 or 529 => Overloaded,
        _ => string.Format(CultureInfo.CurrentCulture, CoreText.T("ApiRequestFailed", "Request failed (HTTP {0})."), status),
    };

    public static string FromStreamError(string? type, string? message) => type switch
    {
        "overloaded_error" or "api_error" => Overloaded,
        "rate_limit_error" => RateLimited,
        "invalid_request_error" => Rejected(message),
        _ => string.Format(CultureInfo.CurrentCulture, CoreText.T("ApiAnswerStopped", "The answer stopped: {0}"), message ?? type),
    };

    public static string? StopMessage(string? stopReason) => stopReason switch
    {
        "refusal" => CoreText.T("ApiRefusal", "Claude declined to answer this request."),
        "max_tokens" => CoreText.T("ApiMaxTokens", "The answer was cut off at the length limit."),
        _ => null,
    };

    private static string Rejected(string? message) => string.IsNullOrWhiteSpace(message)
        ? CoreText.T("ApiRejected", "Request rejected.")
        : string.Format(CultureInfo.CurrentCulture, CoreText.T("ApiRejectedWith", "Request rejected: {0}"), message);

    private static string? BodyMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e)
                ? ChatStreamEvent.Str(e, "message") : null;
        }
        catch (JsonException) { return null; }
    }
}
