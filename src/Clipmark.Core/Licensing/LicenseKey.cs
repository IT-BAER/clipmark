using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Clipmark.Core.Licensing;

/// <summary>What a sold license says. <paramref name="LicenseId"/> maps back to the sale at the payment provider.</summary>
public sealed record LicensePayload(ulong LicenseId, DateOnly Issued, byte Product = LicenseKey.ClipmarkProduct);

/// <summary>
/// Offline license key: 12-byte payload + ECDSA P-256 / SHA-256 signature (IEEE P1363), Base32 in dash-separated groups.
/// Layout: version (1), product (1), license id (8, big-endian), issued as days since 2026-01-01 (2, big-endian).
/// </summary>
public static class LicenseKey
{
    public const byte ClipmarkProduct = 1;
    private const byte Version = 1;
    private const int PayloadLength = 12;
    private const int SignatureLength = 64;
    private const int GroupLength = 5;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static readonly DateOnly Epoch = new(2026, 1, 1);

    /// <summary>Decoded size of a v1 key: payload plus signature.</summary>
    public const int KeyLength = PayloadLength + SignatureLength;

    /// <summary>Bytes the text decodes to with the key alphabet (dashes, spaces and case ignored); null when it is not canonical Base32.</summary>
    public static int? DecodedLength(string? text) => FromBase32(text)?.Length;

    public static string Sign(LicensePayload payload, ECDsa privateKey)
    {
        int days = payload.Issued.DayNumber - Epoch.DayNumber;
        if (days is < 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(payload), payload.Issued, "Issue date must be between 2026-01-01 and 2205.");

        var data = new byte[PayloadLength];
        data[0] = Version;
        data[1] = payload.Product;
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(2), payload.LicenseId);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(10), (ushort)days);
        byte[] signature = privateKey.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Group(ToBase32([.. data, .. signature]));
    }

    /// <summary>Accepts any case, spaces and dashes. False for bad text, bad signature, unknown version or another product.</summary>
    public static bool TryVerify(string? text, ECDsa publicKey, [NotNullWhen(true)] out LicensePayload? payload)
    {
        payload = null;
        if (FromBase32(text) is not { Length: KeyLength } bytes)
            return false;
        var data = bytes.AsSpan(0, PayloadLength);
        if (data[0] != Version || data[1] != ClipmarkProduct)
            return false;
        if (!publicKey.VerifyData(data, bytes.AsSpan(PayloadLength), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            return false;
        payload = new LicensePayload(
            BinaryPrimitives.ReadUInt64BigEndian(data[2..]),
            Epoch.AddDays(BinaryPrimitives.ReadUInt16BigEndian(data[10..])),
            data[1]);
        return true;
    }

    private static string ToBase32(byte[] bytes)
    {
        var sb = new StringBuilder((bytes.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (byte b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }
        if (bits > 0)
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    /// <summary>Null when the text holds a character outside the alphabet.</summary>
    private static byte[]? FromBase32(string? text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (char c in text ?? "")
        {
            if (c is '-' || char.IsWhiteSpace(c))
                continue;
            int value = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (value < 0)
                return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
            }
        }
        // Canonical only: fewer than 5 leftover bits, all zero. Otherwise "key + A" would still verify.
        return bits < 5 && (buffer & ((1 << bits) - 1)) == 0 ? [.. bytes] : null;
    }

    private static string Group(string text) =>
        string.Join('-', text.Chunk(GroupLength).Select(chunk => new string(chunk)));
}
