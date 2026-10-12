using System.Text;
using Clipmark.App.Interop;

namespace Clipmark.App.Ai;

/// <summary>
/// The user's Anthropic API key: Credential Manager generic credential "Clipmark/AnthropicKey", local machine (does not roam).
/// Never written to settings.json or logs. Debug runs with CLIPMARK_TEST_REGROOT use their own target.
/// </summary>
internal static class AnthropicKeyStore
{
    private const string Target = "Clipmark/AnthropicKey";

    private static string TargetName()
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("CLIPMARK_TEST_REGROOT") is { Length: > 0 } root)
            return Target + "/" + root;
#endif
        return Target;
    }

    public static unsafe string? Read()
    {
        if (!Native.CredRead(TargetName(), Native.CRED_TYPE_GENERIC, 0, out var cred))
            return null;
        try
        {
            if (cred->CredentialBlob is null || cred->CredentialBlobSize == 0)
                return null;
            return Encoding.UTF8.GetString(cred->CredentialBlob, (int)cred->CredentialBlobSize);
        }
        finally
        {
            Native.CredFree(cred);
        }
    }

    public static bool HasKey => Read() is not null;

    /// <summary>"…abcd": enough to recognize the key, never the key itself; empty for a key of 8 characters or fewer; null without a key.</summary>
    public static string? Hint() => Read() is { } key ? (key.Length > 8 ? "…" + key[^4..] : "") : null;

    public static unsafe bool Save(string key)
    {
        byte[] blob = Encoding.UTF8.GetBytes(key);
        try
        {
            fixed (char* target = TargetName())
            fixed (char* user = "Clipmark")
            fixed (byte* data = blob)
            {
                var cred = new Native.CREDENTIALW
                {
                    Type = Native.CRED_TYPE_GENERIC,
                    TargetName = target,
                    UserName = user,
                    CredentialBlob = data,
                    CredentialBlobSize = (uint)blob.Length,
                    Persist = Native.CRED_PERSIST_LOCAL_MACHINE,
                };
                return Native.CredWrite(&cred, 0);
            }
        }
        finally
        {
            Array.Clear(blob);
        }
    }

    /// <summary>True when the credential is gone afterwards (also when there was none).</summary>
    public static bool Delete() =>
        Native.CredDelete(TargetName(), Native.CRED_TYPE_GENERIC, 0) || System.Runtime.InteropServices.Marshal.GetLastPInvokeError() == Native.ERROR_NOT_FOUND;
}
