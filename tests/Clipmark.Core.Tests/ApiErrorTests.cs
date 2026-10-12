using Clipmark.Core.Ai;

namespace Clipmark.Core.Tests;

public sealed class ApiErrorTests
{
    private const string Body = """{"type":"error","error":{"type":"invalid_request_error","message":"bad image"}}""";

    [Theory]
    [InlineData(401, "The API key is not valid. Check it in Settings > AI.")]
    [InlineData(403, "This API key cannot use this model or feature.")]
    [InlineData(404, "This model is not available. Choose another model in Settings > AI.")]
    [InlineData(413, "The capture is too large to send.")]
    [InlineData(500, "Anthropic is overloaded or unavailable. Try again shortly.")]
    [InlineData(502, "Anthropic is overloaded or unavailable. Try again shortly.")]
    [InlineData(503, "Anthropic is overloaded or unavailable. Try again shortly.")]
    [InlineData(529, "Anthropic is overloaded or unavailable. Try again shortly.")]
    [InlineData(418, "Request failed (HTTP 418).")]
    public void Status_maps_to_a_short_message(int status, string expected)
    {
        Assert.Equal(expected, ApiError.Message(status, Body, null));
    }

    [Fact]
    public void Bad_request_includes_the_server_message()
    {
        Assert.Equal("Request rejected: bad image", ApiError.Message(400, Body, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>")]
    [InlineData("""{"type":"error"}""")]
    [InlineData("""[1]""")]
    public void Unparseable_body_never_throws(string? body)
    {
        Assert.StartsWith("Request rejected", ApiError.Message(400, body, null));
    }

    [Fact]
    public void Rate_limit_rounds_the_retry_delay_up()
    {
        Assert.Equal("Rate limit reached. Try again in 8 s.", ApiError.Message(429, null, TimeSpan.FromSeconds(7.2)));
        Assert.Equal("Rate limit reached. Try again shortly.", ApiError.Message(429, null, null));
    }

    [Theory]
    [InlineData("overloaded_error", null, "Anthropic is overloaded or unavailable. Try again shortly.")]
    [InlineData("rate_limit_error", null, "Rate limit reached. Try again shortly.")]
    [InlineData("api_error", null, "Anthropic is overloaded or unavailable. Try again shortly.")]
    [InlineData("invalid_request_error", "bad", "Request rejected: bad")]
    [InlineData("weird_error", "boom", "The answer stopped: boom")]
    [InlineData("weird_error", null, "The answer stopped: weird_error")]
    public void Stream_errors_map_by_type(string type, string? message, string expected)
    {
        Assert.Equal(expected, ApiError.FromStreamError(type, message));
    }

    [Theory]
    [InlineData("refusal", "Claude declined to answer this request.")]
    [InlineData("max_tokens", "The answer was cut off at the length limit.")]
    [InlineData("end_turn", null)]
    [InlineData(null, null)]
    public void Stop_reasons_that_need_a_notice(string? reason, string? expected)
    {
        Assert.Equal(expected, ApiError.StopMessage(reason));
    }
}
