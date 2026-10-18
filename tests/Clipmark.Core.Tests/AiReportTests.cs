using Clipmark.Core.Ai;

namespace Clipmark.Core.Tests;

public class AiReportTests
{
    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    [Fact]
    public void Mail_goes_to_the_report_address_with_a_fixed_subject()
    {
        var uri = AiReport.MailUri("1.1.0", "claude-x", "What is this?", "A cat.");
        Assert.Equal("mailto", uri.Scheme);
        Assert.Equal(AiReport.Address, $"{uri.UserInfo}@{uri.Host}");
        Assert.Equal("Clipmark: AI answer report", Query(uri)["subject"]);
    }

    [Fact]
    public void Body_carries_version_model_question_and_answer_without_adding_parameters()
    {
        const string question = "Was ist & das? #1 50% a=b+c\nzweite Zeile";
        const string answer = "日本語 🙂 &attach=C:\\x.txt&cc=x@y.at ?body=z";
        var uri = AiReport.MailUri("1.1.0+abc", "claude-x", question, answer);
        var query = Query(uri);
        Assert.Equal(["subject", "body"], query.Keys);
        Assert.Contains("1.1.0+abc", query["body"]);
        Assert.Contains("claude-x", query["body"]);
        Assert.Contains(question.Replace("\n", "\r\n"), query["body"]);
        Assert.Contains(answer, query["body"]);
    }

    [Theory]
    [InlineData("回答")]
    [InlineData("🙂")]
    [InlineData("a")]
    public void A_long_answer_is_cut_so_the_link_fits_and_keeps_the_question(string unit)
    {
        string answer = string.Concat(Enumerable.Repeat(unit, 20_000));
        var uri = AiReport.MailUri("1.1.0", "claude-x", "Why?", answer);
        string body = Query(uri)["body"];
        Assert.True(uri.AbsoluteUri.Length <= AiReport.MaxUriLength, $"{uri.AbsoluteUri.Length}");
        Assert.Contains("Why?", body);
        Assert.Contains(unit, body);
        Assert.EndsWith(AiReport.CutMarker, body);
        Assert.DoesNotContain('\uFFFD', body);
    }
}
