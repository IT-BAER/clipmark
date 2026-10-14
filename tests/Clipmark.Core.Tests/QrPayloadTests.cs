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

    [Theory]
    [InlineData(QrKind.Phone, "+43 660 123", "tel:+43 660 123")]
    [InlineData(QrKind.Phone, "+43 (660) 12-3.4/5", "tel:+43 (660) 12-3.4/5")]
    [InlineData(QrKind.Email, "a.b@c.at", "mailto:a.b@c.at")]
    [InlineData(QrKind.Phone, "123?x=1", null)]
    [InlineData(QrKind.Phone, "123;ext=4", null)]
    [InlineData(QrKind.Email, @"a?attach=C:\x@b.at", null)]
    [InlineData(QrKind.Email, "a@b.at&attach=x", null)]
    [InlineData(QrKind.Email, "a@b.at#x", null)]
    [InlineData(QrKind.Email, "a b@c.at", null)]
    [InlineData(QrKind.Link, "https://x.at", null)]
    [InlineData(QrKind.Text, "tel:1", null)]
    public void Target_is_rebuilt_from_the_value_alone(QrKind kind, string value, string? expected)
    {
        var target = QrPayload.Target(kind, value);
        Assert.Equal(expected, target is null ? null : Uri.UnescapeDataString(target.AbsoluteUri));
        Assert.True(target is null || (target.Query.Length == 0 && target.Fragment.Length == 0));
    }
}
