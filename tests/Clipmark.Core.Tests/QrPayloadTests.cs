using Clipmark.Core.TextActions;

namespace Clipmark.Core.Tests;

public class QrPayloadTests
{
    [Theory]
    [InlineData("TEL:+43 660 123", QrKind.Phone, "+43 660 123")]
    [InlineData("tel:+43%20660%20123", QrKind.Phone, "+43 660 123")]
    [InlineData("mailto:a@b.at?subject=x", QrKind.Email, "a@b.at")]
    [InlineData("MAILTO:a%40b.at", QrKind.Email, "a@b.at")]
    [InlineData("mailto:", QrKind.Text, "mailto:")]
    [InlineData("mailto:?subject=x", QrKind.Text, "mailto:?subject=x")]
    [InlineData("tel:", QrKind.Text, "tel:")]
    [InlineData("https://x.at", QrKind.Link, "https://x.at")]
    [InlineData("http://x.at/a?b=1", QrKind.Link, "http://x.at/a?b=1")]
    [InlineData("hello world", QrKind.Text, "hello world")]
    [InlineData("ftp://x.at", QrKind.Text, "ftp://x.at")]
    public void Parse_classifies_the_payload(string payload, QrKind kind, string value)
    {
        Assert.Equal((kind, value), QrPayload.Parse(payload));
    }
}
