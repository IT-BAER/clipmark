namespace Clipmark.Core.TextActions;

public enum QrKind { Text, Link, Phone, Email }

/// <summary>What a decoded QR code holds: a web link, a phone number (tel:), an email address (mailto:) or plain text.</summary>
public static class QrPayload
{
    public static (QrKind Kind, string Value) Parse(string payload)
    {
        if (Uri.TryCreate(payload, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return (QrKind.Link, payload);
        if (payload.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            return Or(QrKind.Phone, Uri.UnescapeDataString(payload[4..]).Trim(), payload);
        if (payload.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return Or(QrKind.Email, Uri.UnescapeDataString(payload[7..].Split('?')[0]).Trim(), payload);
        return (QrKind.Text, payload);
    }

    private static (QrKind, string) Or(QrKind kind, string value, string payload) =>
        value.Length > 0 ? (kind, value) : (QrKind.Text, payload);
}
