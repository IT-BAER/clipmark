using System.Net.Http.Headers;
using System.Security.Cryptography;
using Clipmark.App.Ai;
using Clipmark.Core.Relay;

namespace Clipmark.App.Relay;

/// <summary>Outcome of one licence call: a value, a server rejection, or a network failure (message only). Never carries the code, key or token in text.</summary>
internal sealed record LicenceResult<T>(T? Value, RelayFailure? Failure = null, string? NetworkError = null) where T : class
{
    public string Message => NetworkError ?? Failure?.Message() ?? "";
}

/// <summary>
/// The releash licence endpoints (spec 18.6), signed with <see cref="DeviceKey"/>. One client, 15 s timeout, system proxy;
/// one retry with the server's clock offset after clock_skew, never more than 5 minutes and never stored; one retry
/// after 1 s on server_busy. Every attempt is signed again (new nonce).
/// </summary>
internal static class LicenceClient
{
    public const string RelayHost = "https://releash.it-baer.net";

    internal static readonly Uri BaseUri = new(
#if DEBUG
        Environment.GetEnvironmentVariable("CLIPMARK_TEST_RELAY_BASE") is { Length: > 0 } testBase ? testBase :
#endif
        RelayHost);

    private const long MaxSkewSeconds = 300;

    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public static string Host => BaseUri.Host;

    public static Task<LicenceResult<ActivateResult>> ActivateAsync(string codeOrKey, CancellationToken ct) =>
        CallAsync(LicenceRequest.ActivatePath, key => LicenceRequest.BuildActivate(codeOrKey, key.X, key.Y, key.KeyStore, App.Version), LicenceResponse.ParseActivate, create: true, ct);

    public static Task<LicenceResult<TrialResult>> TrialAsync(CancellationToken ct) =>
        CallAsync(LicenceRequest.TrialPath, key => LicenceRequest.BuildTrial(key.X, key.Y, key.KeyStore, App.Version), LicenceResponse.ParseTrial, create: true, ct);

    public static Task<LicenceResult<string>> EntitlementAsync(CancellationToken ct) =>
        CallAsync(LicenceRequest.EntitlementPath, _ => LicenceRequest.EmptyObject, LicenceResponse.ParseEntitlement, create: false, ct);

    /// <summary>Value "" on 204.</summary>
    public static Task<LicenceResult<string>> DeactivateAsync(CancellationToken ct) =>
        CallAsync(LicenceRequest.DeactivatePath, _ => LicenceRequest.EmptyObject, _ => "", create: false, ct);

    /// <summary>Adds the four envelope headers for <paramref name="body"/> sent to <paramref name="path"/>.</summary>
    internal static void Sign(HttpRequestMessage request, string path, byte[] body, DeviceKey key, long offsetSeconds)
    {
        var envelope = RelaySigning.Sign("POST", path, key.Thumbprint, body, key.Sign, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offsetSeconds);
        foreach (var (name, value) in envelope.Headers)
            request.Headers.TryAddWithoutValidation(name, value);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
    }

    /// <summary>
    /// Whether to send again: once after clock_skew (with <paramref name="offset"/> set) and once after server_busy (after 1 s).
    /// </summary>
    internal static async Task<bool> RetryAsync(RelayFailure failure, Retries retries, CancellationToken ct)
    {
        if (!retries.Skew && SkewOffset(failure) is { } skew)
        {
            retries.Skew = true;
            retries.Offset = skew;
            return true;
        }
        if (!retries.Busy && failure.IsServerBusy)
        {
            retries.Busy = true;
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            return true;
        }
        return false;
    }

    internal sealed class Retries
    {
        public bool Skew, Busy;
        public long Offset;
    }

    /// <summary>The offset for one retry after clock_skew, or null when the server time is missing or more than 5 minutes off.</summary>
    internal static long? SkewOffset(RelayFailure failure)
    {
        if (!failure.IsClockSkew || failure.ServerTime is not { } server)
            return null;
        long offset = server - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Math.Abs(offset) <= MaxSkewSeconds ? offset : null;
    }

    private static async Task<LicenceResult<T>> CallAsync<T>(string path, Func<DeviceKey, byte[]> build, Func<string, T?> parse, bool create, CancellationToken ct) where T : class
    {
        DeviceKey key;
        try
        {
            key = (create ? DeviceKey.OpenOrCreate() : DeviceKey.TryOpen()) ?? throw new CryptographicException("No device key.");
        }
        catch (CryptographicException e)
        {
            return new(null, NetworkError: Loc.Format("LicenceKeyFailed", e.Message));
        }
        byte[] body = build(key);
        var retries = new Retries();
        try
        {
            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, path));
                Sign(request, path, body, key, retries.Offset);
                using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return parse(text) is { } value ? new(value) : new(null, RelayFailure.Parse((int)response.StatusCode, null));
                var failure = RelayFailure.Parse((int)response.StatusCode, text);
                if (await RetryAsync(failure, retries, ct).ConfigureAwait(false))
                    continue;
                return new(null, failure);
            }
        }
        catch (Exception e) when (ClaudeChat.Network(e, ct, BaseUri.Host) is { } message)
        {
            return new(null, NetworkError: message);
        }
        catch (CryptographicException e)
        {
            return new(null, NetworkError: Loc.Format("LicenceKeyFailed", e.Message));
        }
    }
}
