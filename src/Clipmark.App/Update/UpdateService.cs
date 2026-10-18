using System.Net;
using System.Net.Http.Headers;
using Clipmark.App.Ai;
using Clipmark.Core.Update;
using Microsoft.UI.Dispatching;

namespace Clipmark.App.Update;

/// <summary>
/// Looks for a newer GitHub release: at start and daily per <see cref="UpdatePolicy"/>, or on request in Settings. Sends only the
/// request itself (a User-Agent with the version, no id and no licence data) and installs nothing: the user gets the release page.
/// Not for the Store build, which the Store updates.
/// </summary>
internal static class UpdateService
{
    private const string LatestUrl = "https://api.github.com/repos/IT-BAER/clipmark/releases/latest";

    private static readonly Uri LatestUri = new(
#if DEBUG
        Environment.GetEnvironmentVariable("CLIPMARK_TEST_UPDATE_URL") is { Length: > 0 } testUrl ? testUrl :
#endif
        LatestUrl);

    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = TimeSpan.FromSeconds(15),
        MaxResponseContentBufferSize = 1 << 20,
    };

    private static DispatcherQueue? _ui;
    private static int _checking;

    /// <summary>False for the Store build and, in a Debug test run, unless CLIPMARK_TEST_UPDATE_URL names a test server.</summary>
    public static bool Supported =>
#if DEBUG
        (App.TestDir is null || Environment.GetEnvironmentVariable("CLIPMARK_TEST_UPDATE_URL") is { Length: > 0 }) &&
#endif
        !Output.CaptureOutput.IsPackaged();

    /// <summary>A newer release than this one, as of the last check that reached GitHub.</summary>
    public static UpdateRelease? Available { get; private set; }

    /// <summary>Outcome of the last check in this session: null = none yet, true = reached GitHub.</summary>
    public static bool? LastReached { get; private set; }

    public static bool Checking => Volatile.Read(ref _checking) == 1;

    /// <summary>Raised on the UI thread when a check starts or ends.</summary>
    public static event Action? Changed;

    /// <summary>Called on the UI thread at startup and hourly: a check runs only when <see cref="UpdatePolicy"/> says it is due.</summary>
    public static void Refresh()
    {
        _ui ??= DispatcherQueue.GetForCurrentThread();
        if (Supported && UpdatePolicy.ShouldCheck(AppSettings.Current, DateTime.UtcNow))
            _ = CheckAsync();
    }

    /// <summary>One request; a failure leaves <see cref="Available"/> and the stored last-check time alone.</summary>
    public static async Task CheckAsync()
    {
        _ui ??= DispatcherQueue.GetForCurrentThread();
        if (Interlocked.Exchange(ref _checking, 1) == 1)
            return;
        Raise();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestUri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Clipmark", App.Version));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            // 404: the repository has no release yet; any other failure (rate limit, outage) is retried later.
            if (response.StatusCode != HttpStatusCode.NotFound)
                response.EnsureSuccessStatusCode();
            string body = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : "";
            var release = UpdateRelease.Parse(body);
            Available = release is not null && release.IsNewerThan(App.Version) ? release : null;
            LastReached = true;
            Run(() => AppSettings.Update(s => s with { UpdateLastCheckUtc = DateTime.UtcNow }));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            LastReached = false;
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
            Raise();
        }
    }

    /// <summary>The release to offer in the popup, or null when none is newer or the user said Later or Never.</summary>
    public static UpdateRelease? ForPrompt() =>
        Available is { } release && UpdatePolicy.ShouldPrompt(AppSettings.Current, release, App.Version, DateTime.UtcNow) ? release : null;

    private static void Raise() => Run(() => Changed?.Invoke());

    private static void Run(Action action)
    {
        if (_ui is null || _ui.HasThreadAccess)
            action();
        else
            _ui.TryEnqueue(() => action());
    }
}
