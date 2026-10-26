using System.Security.Cryptography;
using System.Text.Json;
using Clipmark.Core.Ai;
using Clipmark.Core.Licensing;

namespace Clipmark.Core.Tests;

/// <summary>The releash licence vector (testdata/licence/v1.json without the test issuer's private key) and the Anthropic golden body (testdata/anthropic, copied byte-equal); test data only.</summary>
public sealed class RelayVectorTests
{
    private static JsonElement Load(params string[] path) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "testdata", .. path]))).RootElement;

    [Fact]
    public void Licence_vector_keys_verify_against_the_test_issuer_with_their_id_and_date()
    {
        var v = Load("licence", "v1.json");
        using var issuer = ECDsa.Create();
        issuer.ImportSubjectPublicKeyInfo(Convert.FromBase64String(v.GetProperty("issuer_spki_base64").GetString()!), out _);

        foreach (var c in v.GetProperty("vectors").EnumerateArray())
        {
            Assert.True(LicenseKey.TryVerify(c.GetProperty("key").GetString(), issuer, out var payload));
            Assert.Equal(ulong.Parse(c.GetProperty("licence_id").GetString()!), payload.LicenseId);
            Assert.Equal(DateOnly.Parse(c.GetProperty("issued").GetString()!), payload.Issued);
        }
    }

    [Fact]
    public void Ask_body_is_byte_equal_to_the_relay_golden()
    {
        var input = Load("anthropic", "clipmark-ask-input.json");
        Assert.Equal(ChatRequest.SystemPrompt, input.GetProperty("system").GetString());
        var image = input.GetProperty("image");
        var c = new ChatConversation();
        c.Start(image.GetProperty("base64").GetString()!, image.GetProperty("media_type").GetString()!);
        foreach (var turn in input.GetProperty("turns").EnumerateArray())
        {
            string text = turn.GetProperty("text").GetString()!;
            if (turn.GetProperty("role").GetString() == "user")
                c.Ask(text);
            else
            {
                c.AppendDelta(text);
                c.Complete("end_turn");
            }
        }

        byte[] body = ChatRequest.Build(c, input.GetProperty("model").GetString()!, input.GetProperty("effort").GetString()!,
            input.GetProperty("max_tokens").GetInt32());

        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "anthropic", "clipmark-ask.json")),
            System.Text.Encoding.UTF8.GetString(body));
    }
}
