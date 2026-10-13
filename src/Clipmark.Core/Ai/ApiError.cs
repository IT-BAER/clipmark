using System.Text.Json;

namespace Clipmark.Core.Ai;

/// <summary>Short English messages for failed Messages API calls; never throws on a malformed body.</summary>
public static class ApiError
{
    private const string Overloaded = "Anthropic is overloaded or unavailable. Try again shortly.";
    private const string RateLimited = "Rate limit reached. Try again shortly.";

    public static string Message(int status, string? body, TimeSpan? retryAfter) => status switch
    {
        400 => Rejected(BodyMessage(body)),
        401 => "The API key is not valid. Check it in Settings > AI.",
        403 => "This API key cannot use this model or feature.",
        404 => "This model is not available. Choose another model in Settings > AI.",
        413 => "The capture is too large to send.",
        429 => retryAfter is { } wait ? $"Rate limit reached. Try again in {(int)Math.Ceiling(wait.TotalSeconds)} s." : RateLimited,
        500 or 502 or 503 or 529 => Overloaded,
        _ => $"Request failed (HTTP {status}).",
    };

    public static string FromStreamError(string? type, string? message) => type switch
    {
        "overloaded_error" or "api_error" => Overloaded,
        "rate_limit_error" => RateLimited,
        "invalid_request_error" => Rejected(message),
        _ => $"The answer stopped: {message ?? type}",
    };

    public static string? StopMessage(string? stopReason) => stopReason switch
    {
        "refusal" => "Claude declined to answer this request.",
        "max_tokens" => "The answer was cut off at the length limit.",
        _ => null,
    };

    private static string Rejected(string? message) => string.IsNullOrWhiteSpace(message) ? "Request rejected." : $"Request rejected: {message}";

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
