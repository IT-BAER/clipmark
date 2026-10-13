using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using Clipmark.Core.Ai;

namespace Clipmark.App.Ai;

/// <summary>Raw Messages API calls (the Anthropic SDK is not Native AOT clean). System proxy; no retries.</summary>
internal static class ClaudeChat
{
    private static readonly Uri BaseUri = new(
#if DEBUG
        Environment.GetEnvironmentVariable("CLIPMARK_TEST_AI_BASE") is { Length: > 0 } testBase ? testBase :
#endif
        "https://api.anthropic.com/");

    /// <summary>Time to the response headers; a large image upload over a slow line needs most of it.</summary>
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = TimeSpan.FromSeconds(120) };

    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(60);

    public static string Host => BaseUri.Host;

    private static HttpRequestMessage Request(HttpMethod method, string path, string key)
    {
        var request = new HttpRequestMessage(method, new Uri(BaseUri, path));
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", "2023-06-01");
        return request;
    }

    /// <summary>GET /v1/models (no token cost): null when the key works, else the message to show.</summary>
    public static async Task<string?> TestKeyAsync(string key, CancellationToken ct)
    {
        // The client timeout ends at the headers; a stalled error body would keep "Testing…" up forever.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var request = Request(HttpMethod.Get, "v1/models", key);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            return response.IsSuccessStatusCode ? null : await FailureAsync(response, limit.Token);
        }
        catch (Exception e) when (Network(e, ct) is { } message)
        {
            return message;
        }
    }

    /// <summary>Streams one answer: text deltas go to <paramref name="onText"/> and thinking deltas to <paramref name="onThinking"/> on a pool thread. Returns the stop reason.</summary>
    /// <exception cref="AiException">The message to show.</exception>
    public static async Task<string> SendAsync(string key, byte[] body, Action<string> onText, CancellationToken ct, Action<string>? onThinking = null)
    {
        try
        {
            using var request = Request(HttpMethod.Post, "v1/messages", key);
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new AiException(await FailureAsync(response, ct).ConfigureAwait(false));
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await ChatStream.ReadAsync(stream, onText, Idle, ct, onThinking).ConfigureAwait(false);
        }
        catch (Exception e) when (Network(e, ct) is { } message)
        {
            throw new AiException(message);
        }
    }

    private static async Task<string> FailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException) { } // the status alone still explains it
        var retry = response.Headers.RetryAfter;
        TimeSpan? wait = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return ApiError.Message((int)response.StatusCode, body, wait);
    }

    /// <summary>Message for a connection, proxy, TLS or timeout failure; null for a cancel by the caller or anything else.</summary>
    private static string? Network(Exception e, CancellationToken ct) => e switch
    {
        OperationCanceledException when ct.IsCancellationRequested => null,
        OperationCanceledException => Loc.Format("AiTimeout", BaseUri.Host),
        HttpRequestException { InnerException: AuthenticationException } => Loc.Format("AiTlsFailed", BaseUri.Host),
        HttpRequestException { StatusCode: HttpStatusCode.ProxyAuthenticationRequired } => Loc.Get("AiProxyAuth"),
        HttpRequestException h => Loc.Format("AiUnreachable", BaseUri.Host, h.Message),
        IOException io => Loc.Format("AiConnectionBroke", BaseUri.Host, io.Message),
        _ => null,
    };
}
