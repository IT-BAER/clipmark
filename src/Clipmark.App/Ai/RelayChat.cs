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

    /// <summary>Streams one answer like <see cref="ClaudeChat.SendAsync"/>. A lost licence or install also drops the entitlement.</summary>
    /// <exception cref="AiException">The message to show.</exception>
    public static async Task<string> SendAsync(byte[] body, Action<string> onText, CancellationToken ct, Action<string>? onThinking = null)
    {
        if (DeviceKey.TryOpen() is not { } key)
            throw new AiException(Loc.Format("LicenceKeyFailed", "-"));
        string path = RelayRequest.AskPath;
        var retries = new LicenceClient.Retries();
        try
        {
            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(LicenceClient.BaseUri, path));
                LicenceClient.Sign(request, path, body, key, retries.Offset);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string? text = null;
                    try { text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
                    catch (HttpRequestException) { } // the status alone still explains it
                    var failure = RelayFailure.Parse((int)response.StatusCode, text);
                    if (await LicenceClient.RetryAsync(failure, retries, ct).ConfigureAwait(false))
                        continue;
                    if (failure.EndsEntitlement)
                        AppLicense.DropEntitlement(failure);
                    else if (failure.NeedsActivation)
                        AppLicense.InstallMissing(failure);
                    else if (failure.Code == "task_not_found")
                        AppLicense.RelayTaskMissing();
                    throw new AiException(failure.Message());
                }
                using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                return await ChatStream.ReadAsync(stream, onText, Idle, ct, onThinking).ConfigureAwait(false);
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
}
