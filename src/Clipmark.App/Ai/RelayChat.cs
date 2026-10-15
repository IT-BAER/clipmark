using System.Globalization;
using Clipmark.App.Relay;
using Clipmark.Core.Ai;
using Clipmark.Core.Relay;

namespace Clipmark.App.Ai;

/// <summary>
/// Ask AI over IT-BAER's relay (releash task clipmark/ask) for Premium without an own key: the signed envelope instead of
/// an API key, the provider's SSE events unchanged plus a trailing releash.usage event. Timeouts and cancel as <see cref="ClaudeChat"/>.
/// </summary>
internal static class RelayChat
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = TimeSpan.FromSeconds(120) };

    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(60);

    public static string Host => LicenceClient.Host;

    /// <summary>Monthly budget left after the last answer (usage event, else the response header); null until known.</summary>
    public static double? RemainingMonthlyUsd { get; private set; }

    /// <summary>Streams one answer like <see cref="ClaudeChat.SendAsync"/>. A lost licence or install also drops the entitlement.</summary>
    /// <exception cref="AiException">The message to show.</exception>
    public static async Task<string> SendAsync(byte[] body, Action<string> onText, CancellationToken ct, Action<string>? onThinking = null)
    {
        if (DeviceKey.TryOpen() is not { } key)
            throw new AiException(Loc.Format("LicenceKeyFailed", "-"));
        string path = RelayRequest.AskPath;
        long offset = 0;
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(LicenceClient.BaseUri, path));
                LicenceClient.Sign(request, path, body, key, offset);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                ReadRemainingHeader(response);
                if (!response.IsSuccessStatusCode)
                {
                    string? text = null;
                    try { text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
                    catch (HttpRequestException) { } // the status alone still explains it
                    var failure = RelayFailure.Parse((int)response.StatusCode, text);
                    if (attempt == 0 && LicenceClient.SkewOffset(failure) is { } skew)
                    {
                        offset = skew;
                        continue;
                    }
                    if (failure.EndsEntitlement)
                        AppLicense.DropEntitlement(failure);
                    throw new AiException(failure.Message());
                }
                using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                return await ChatStream.ReadAsync(stream, onText, Idle, ct, onThinking, OnRelayEvent).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (ClaudeChat.Network(e, ct, Host) is { } message)
        {
            throw new AiException(message);
        }
        catch (System.Security.Cryptography.CryptographicException e)
        {
            throw new AiException(Loc.Format("LicenceKeyFailed", e.Message));
        }
    }

    private static void OnRelayEvent(string data)
    {
        if (RelayUsage.Parse(data)?.RemainingMonthlyUsd is { } remaining)
            RemainingMonthlyUsd = remaining;
    }

    private static void ReadRemainingHeader(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-releash-Remaining-Monthly-USD", out var values)
            && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out double usd))
            RemainingMonthlyUsd = usd;
    }
}
