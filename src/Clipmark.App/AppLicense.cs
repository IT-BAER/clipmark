using System.Security.Cryptography;
using Clipmark.App.Relay;
using Clipmark.Core.Licensing;
using Clipmark.Core.Relay;
using Microsoft.UI.Dispatching;

namespace Clipmark.App;

/// <summary>
/// Premium state in %LOCALAPPDATA%\Clipmark\license.json: the releash entitlement (signed, bound to <see cref="DeviceKey"/>)
/// is checked offline and is the only source of Premium; the server is called per <see cref="RefreshPolicy"/> (trial start,
/// refresh, v1 key activation). Debug test mode keeps the file in CLIPMARK_TEST_DIR and the device key under CLIPMARK_TEST_REGROOT.
/// </summary>
internal static class AppLicense
{
    public const string BuyUrl = "https://it-baer.net/clipmark";

    /// <summary>Production entitlement keys, kid to base64 SPKI (`releash key list clipmark`). A kid missing here fails closed.</summary>
    private static readonly Dictionary<string, string> ReleaseKeys = new(StringComparer.Ordinal)
    {
        ["n4tc-QFOew4"] = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEYHmnFiDPZmdSaTtMC62/Z1Fr/+0kWe2CiDhyWYnhXUfdZDVNGKiEw4ROXz5FQGG+SrvHlmNgWfwjjY6rN6/PZg==",
    };

    private static readonly IReadOnlyDictionary<string, ECDsa> EntitlementKeys = EntitlementToken.LoadKeys(KeyTexts());

    private static readonly string FilePath = Path.Combine(App.DataDir, "license.json");

    private static DispatcherQueue? _ui;
    private static int _syncing;
    private static TokenVerdict _verdict = TokenVerdict.Malformed;
    private static EntitlementClaims? _claims;

    public static Entitlement Current { get; private set; } = new(LicenseStatus.Free);

    public static event Action<Entitlement>? Changed;

    /// <summary>license.json as last read.</summary>
    public static LicenseFile State { get; private set; } = new(null);

    /// <summary>The last rejection by the licence server in this session (Settings shows it); null after a success.</summary>
    public static string? LastServerMessage { get; private set; }

    /// <summary>Premium from a verified, non-trial entitlement (Remove from this PC applies).</summary>
    public static bool LicensedByToken => Current.Status == LicenseStatus.Licensed && _verdict == TokenVerdict.Valid && _claims is { Plan: not "trial" };

    /// <summary>Ask AI may use IT-BAER's relay: Premium by entitlement, an install on the server, and the task offered there.</summary>
    public static bool CanUseRelay => LicensedByToken && State.InstallId is not null && !_relayTaskMissing;

    /// <summary>Premium still verifies offline, but the server has no install for this PC (unknown_install): Settings asks for the code again.</summary>
    public static bool NeedsActivation => LicensedByToken && State.InstallId is null;

    /// <summary>task_not_found in this session: the host does not offer Ask AI, so the relay path is hidden until restart.</summary>
    private static bool _relayTaskMissing;

    /// <summary>No server contact yet: no install id and never activated.</summary>
    public static bool NeverContacted => State.InstallId is null && State.ActivatedUtc is null && State.Key is null;

    /// <summary>A licence was activated here and is no longer (removed, revoked, expired).</summary>
    public static bool WasActivated => State.ActivatedUtc is not null;

    private static Dictionary<string, string> KeyTexts()
    {
        var keys = new Dictionary<string, string>(ReleaseKeys, StringComparer.Ordinal);
#if DEBUG
        // The releash vector TEST key (testdata/entitlement/v1.json); never in a Release build.
        keys["dGVzdGtpZDE"] = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE2XHT79Y4QLGIA1goT5irW334QJ0vGCx8GiitkWCAmDPv9JOsVY+kFT1Tzdezd/Y8nSYSzwNna/uk7Bvr+CrlbA==";
#endif
        return keys;
    }

    /// <summary>Re-checks the stored state offline and, when due, calls the server in the background. Called at startup, hourly and when a capture opens.</summary>
    public static void Refresh()
    {
        _ui ??= DispatcherQueue.GetForCurrentThread();
        var loaded = TryLoad();
        Evaluate(loaded);
        if (loaded is not null && RefreshPolicy.ShouldCall(loaded, DateTime.UtcNow))
        {
            // Low priority: the call starts after the current UI work (at startup: after the window is created).
            if (_ui is null || !_ui.TryEnqueue(DispatcherQueuePriority.Low, () => _ = SyncAsync()))
                _ = SyncAsync();
        }
    }

    /// <summary>One server call if <see cref="RefreshPolicy"/> says so; a network failure only records the attempt.</summary>
    internal static async Task SyncAsync()
    {
        if (Interlocked.Exchange(ref _syncing, 1) == 1)
            return;
        try
        {
            var now = DateTime.UtcNow;
            if (TryLoad() is not { } file || !RefreshPolicy.ShouldCall(file, now))
                return;
            TrySave(file with { LastRefreshUtc = now });
            var call = RefreshPolicy.NextCall(file);
            if (call == LicenceCall.Refresh)
            {
                var r = await Task.Run(() => LicenceClient.EntitlementAsync(CancellationToken.None));
                if (r.Value is { } fresh)
                    Store(fresh, null, activated: false);
                // Only the codes that end the licence or install drop it; 403 app_disabled keeps it until exp + grace.
                else if (r.Failure is { EndsEntitlement: true })
                    DropEntitlement(r.Failure);
                else if (r.Failure is { NeedsActivation: true })
                    InstallMissing(r.Failure);
            }
            else if (call == LicenceCall.Activate)
            {
                var r = await Task.Run(() => LicenceClient.ActivateAsync(file.Key!, CancellationToken.None));
                if (r.Value is { } a)
                    Store(a.Entitlement, a.InstallId, activated: true);
                else if (r.Failure is { Status: 403, Code: "licence_invalid" or "subject_revoked" or "licence_expired" } f)
                    Update(s => s with { Key = null }, f.Message());
            }
            else
            {
                var r = await Task.Run(() => LicenceClient.TrialAsync(CancellationToken.None));
                if (r.Value is { } t)
                    Store(t.Entitlement, null, activated: false);
                else if (r.Failure is { EndsEntitlement: true })
                    DropEntitlement(r.Failure);
                else if (r.Failure is { Code: "trial_not_offered" } f)
                    LastServerMessage = f.Message();
            }
        }
        finally
        {
            Volatile.Write(ref _syncing, 0);
        }
    }

    /// <summary>Activates a code or v1 key on the server and stores the entitlement; the result is for the Settings text.</summary>
    public static async Task<LicenceResult<ActivateResult>> ActivateAsync(string text)
    {
        var r = await Task.Run(() => LicenceClient.ActivateAsync(text.Trim(), CancellationToken.None));
        if (r.Value is { } a)
            Store(a.Entitlement, a.InstallId, activated: true);
        return r;
    }

    /// <summary>Frees this PC's seat. True when the entitlement and install id were cleared: on 204, or when the server no longer holds the install; any other failure keeps them.</summary>
    public static async Task<(bool Removed, LicenceResult<string> Result)> DeactivateAsync()
    {
        var r = await Task.Run(() => LicenceClient.DeactivateAsync(CancellationToken.None));
        bool removed = r.Value is not null || r.Failure is { InstallGone: true };
        if (removed)
            Update(s => s with { Entitlement = null, InstallId = null }, null);
        return (removed, r);
    }

    /// <summary>The server ended the licence, trial or install: Premium locks at once. The install id stays for the status text.</summary>
    internal static void DropEntitlement(RelayFailure failure)
    {
        if (_ui is { HasThreadAccess: false } ui)
        {
            ui.TryEnqueue(() => DropEntitlement(failure));
            return;
        }
        Update(s => s with { Entitlement = null, InstallId = s.InstallId ?? DeviceKey.TryOpen()?.Thumbprint }, failure.Message());
    }

    /// <summary>
    /// unknown_install: the server has no row for this key (a database reset or delete). The signed entitlement stays for
    /// offline Premium until exp + grace; clearing the install id stops relay calls and shows the activation field.
    /// </summary>
    internal static void InstallMissing(RelayFailure failure)
    {
        if (_ui is { HasThreadAccess: false } ui)
        {
            ui.TryEnqueue(() => InstallMissing(failure));
            return;
        }
        Update(s => s with { InstallId = null }, failure.Message());
        Changed?.Invoke(Current); // Current is unchanged (still Licensed), but the Settings view and Ask AI change
    }

    internal static void RelayTaskMissing() => _relayTaskMissing = true;

    /// <summary>Stores a fresh entitlement; a v1 key is removed once the entitlement verifies here.</summary>
    private static void Store(string token, string? installId, bool activated)
    {
        var key = DeviceKey.TryOpen();
        string dev = key?.Thumbprint ?? "";
        bool verifies = EntitlementToken.Verify(token, EntitlementKeys, RelayRequest.App, dev, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out _) == TokenVerdict.Valid;
        Update(s => s with
        {
            Entitlement = token, InstallId = installId ?? dev, KeyStore = key?.KeyStore ?? s.KeyStore,
            ActivatedUtc = activated ? DateTime.UtcNow : s.ActivatedUtc, Key = verifies ? null : s.Key,
        }, null);
    }

    /// <summary>Read, change, write, re-evaluate. An unreadable file is left alone.</summary>
    private static void Update(Func<LicenseFile, LicenseFile> change, string? serverMessage)
    {
        LastServerMessage = serverMessage;
        if (TryLoad() is not { } file)
            return;
        var next = change(file);
        TrySave(next);
        Evaluate(next);
    }

    private static void Evaluate(LicenseFile? loaded)
    {
        var file = loaded ?? new LicenseFile(null);
        State = file;
        string dev = file.Entitlement is null ? "" : DeviceKey.TryOpen()?.Thumbprint ?? "";
        var next = Entitlement.Evaluate(file, EntitlementKeys, RelayRequest.App, dev, DateTime.UtcNow, out _verdict, out _claims);
        if (next == Current)
            return;
        Current = next;
        Changed?.Invoke(next);
    }

    /// <summary>Null when the file cannot be read; nothing is written then, so the stored state is not overwritten.</summary>
    private static LicenseFile? TryLoad()
    {
        try { return LicenseStore.Load(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A failed write loses licence bookkeeping only; the next server call restores it, so it must not stop the app.</summary>
    private static void TrySave(LicenseFile file)
    {
        try { LicenseStore.Save(FilePath, file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

#if DEBUG
    /// <summary>`--trial-status FILE` (Debug): entitlement and server state. Never prints the token.</summary>
    public static string Describe()
    {
        var file = TryLoad() ?? new LicenseFile(null);
        var claims = EntitlementToken.ReadClaims(file.Entitlement);
        string token = file.Entitlement is null ? "none"
            : $"{EntitlementToken.Name(_verdict)} plan={claims?.Plan} exp={(claims is null ? "-" : DateTimeOffset.FromUnixTimeSeconds(claims.Exp).ToString("O"))}";
        return $"""
            status={Current.Status} daysLeft={Current.TrialDaysLeft} packaged={Output.CaptureOutput.IsPackaged()}
            token={token}
            installId={file.InstallId ?? "-"} keyStore={file.KeyStore ?? "-"} v1key={(file.Key is null ? "no" : "yes")} activated={file.ActivatedUtc:O} lastRefresh={file.LastRefreshUtc:O}
            server={LastServerMessage ?? "-"}
            file={FilePath}

            """;
    }
#endif
}
