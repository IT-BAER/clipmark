using System.Security.Cryptography;
using Clipmark.Core.Relay;

namespace Clipmark.App.Relay;

/// <summary>
/// This PC's persisted, non-exportable ES256 key (releash spec 4.1): Platform Crypto Provider (TPM) first, Software KSP
/// otherwise, user scope. Its RFC 7638 thumbprint is the install id that entitlements are bound to. Debug runs with
/// CLIPMARK_TEST_REGROOT use their own key name. Kept after deactivation; Remove steps in README.
/// </summary>
internal sealed class DeviceKey
{
    private const string Name = "Clipmark.releash.install";

    private static readonly object Gate = new();
    private static DeviceKey? _current;

    private readonly ECDsaCng _key;

    public string KeyStore { get; }
    public string X { get; }
    public string Y { get; }
    public string Thumbprint { get; }

    private DeviceKey(CngKey key)
    {
        _key = new ECDsaCng(key);
        KeyStore = key.Provider == CngProvider.MicrosoftPlatformCryptoProvider ? "tpm" : "software";
        (X, Y) = Jwk.Coordinates(_key);
        Thumbprint = Jwk.Thumbprint(X, Y);
    }

    public static string KeyName()
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("CLIPMARK_TEST_REGROOT") is { Length: > 0 } root)
            return Name + "/" + root;
#endif
        return Name;
    }

    /// <summary>The existing key, or null when none was created yet (or it cannot be opened). Never creates one.</summary>
    public static DeviceKey? TryOpen()
    {
        lock (Gate)
        {
            if (_current is null && Open(KeyName()) is { } key)
                _current = new DeviceKey(key);
            return _current;
        }
    }

    /// <summary>Opens or creates the key; first use is the first trial or activation call, never at startup.</summary>
    /// <exception cref="CryptographicException">Neither provider can hold the key.</exception>
    public static DeviceKey OpenOrCreate()
    {
        lock (Gate)
        {
            if (_current is not null)
                return _current;
            string name = KeyName();
            CngKey key = Open(name) ?? Create(name, CngProvider.MicrosoftPlatformCryptoProvider) ?? Create(name, CngProvider.MicrosoftSoftwareKeyStorageProvider)
                ?? throw new CryptographicException("Neither the Platform Crypto Provider nor the Software Key Storage Provider could create the key.");
            return _current = new DeviceKey(key);
        }
    }

    /// <summary>64-byte IEEE P1363 r||s over SHA-256 of <paramref name="data"/>.</summary>
    public byte[] Sign(byte[] data) => _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    private static CngKey? Open(string name)
    {
        foreach (var provider in new[] { CngProvider.MicrosoftPlatformCryptoProvider, CngProvider.MicrosoftSoftwareKeyStorageProvider })
        {
            try
            {
                if (CngKey.Exists(name, provider))
                    return CngKey.Open(name, provider);
            }
            catch (CryptographicException) { } // no TPM or provider unavailable: try the next one
        }
        return null;
    }

    private static CngKey? Create(string name, CngProvider provider)
    {
        try
        {
            return CngKey.Create(CngAlgorithm.ECDsaP256, name, new CngKeyCreationParameters
            {
                Provider = provider,
                ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.Signing,
                KeyCreationOptions = CngKeyCreationOptions.None,
            });
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
