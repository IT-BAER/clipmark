using Clipmark.Core.Capture;
using Clipmark.Core.Editor;
using Clipmark.Core.TextActions;

namespace Clipmark.Core.Tests;

public class RedactionTests
{
    [Fact]
    public void Redact_adds_boxes_clipped_to_the_image_as_one_undo_step()
    {
        var doc = new EditorDocument(100, 50);
        doc.Redact([new PixelRect(10, 10, 20, 5), new PixelRect(90, 40, 30, 30)]);
        Assert.Equal([new PixelRect(10, 10, 20, 5), new PixelRect(90, 40, 10, 10)], doc.Redactions);
        Assert.True(doc.IsDirty);
        doc.Undo();
        Assert.Empty(doc.Redactions);
    }

    [Fact]
    public void Redact_outside_the_image_or_with_nothing_adds_no_step()
    {
        var doc = new EditorDocument(100, 50);
        doc.Redact([]);
        doc.Redact([new PixelRect(200, 200, 5, 5)]);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void Fill_paints_only_the_clipped_rect()
    {
        // 4 x 3 image, all pixels (1, 2, 3, 4).
        var bgra = Enumerable.Repeat(new byte[] { 1, 2, 3, 4 }, 12).SelectMany(p => p).ToArray();
        Pixels.Fill(bgra, 4, new PixelRect(2, 1, 5, 1), new Rgba(10, 20, 30, 255));
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++)
            {
                int i = (y * 4 + x) * 4;
                byte[] expected = y == 1 && x >= 2 ? [30, 20, 10, 255] : [1, 2, 3, 4];
                Assert.Equal(expected, bgra[i..(i + 4)]);
            }
    }

    [Fact]
    public void Shrink_averages_each_block_and_keeps_partial_blocks_at_the_edge()
    {
        // 3 x 2 image, factor 2: blocks (0..1, 0..1) and the partial column 2.
        byte[] bgra =
        [
            0, 0, 0, 255, 100, 100, 100, 255, 7, 8, 9, 255,
            200, 200, 200, 255, 100, 100, 100, 255, 9, 10, 11, 255,
        ];
        var (small, w, h) = Pixels.Shrink(bgra, 3, 2, 2);
        Assert.Equal((2, 1), (w, h));
        Assert.Equal([100, 100, 100, 255, 8, 9, 10, 255], small);
    }

    private static IReadOnlyList<PixelRect> Boxes(IEnumerable<OcrLine> lines, int pad) =>
        [.. OcrText.Sensitive(lines, pad).Select(m => m.Box)];

    private static OcrLine Line(params string[] words) =>
        new([.. words.Select((w, i) => new OcrWord(w, new(new(i * 100, 0), new(i * 100 + 80, 20))))]);

    private static PixelRect Words(int first, int last) =>
        PixelRect.FromCorners(first * 100 - 2, -2, last * 100 + 80 + 2, 22);

    [Fact]
    public void An_email_covers_its_word()
    {
        Assert.Equal([Words(1, 1)], Boxes([Line("Mail:", "test.user+tag@example.com,", "danke")], 2));
    }

    [Theory]
    [InlineData("+43", "660", "1234567")]
    [InlineData("(0732)", "12", "34", "56")]
    [InlineData("555-123-4567")]
    [InlineData("+49/89/1234-567")]
    public void A_phone_number_covers_all_its_words_but_not_the_label(params string[] number)
    {
        Assert.Equal([Words(1, number.Length)], Boxes([Line(["Tel.", .. number, "abends"])], 2));
    }

    [Theory]
    [InlineData("11.10.2026")]
    [InlineData("300.1.1.1")]
    [InlineData("1234")]
    [InlineData("1.234,56")]
    [InlineData("v2.3.12")]
    [InlineData("12:30")]
    public void Dates_addresses_and_short_numbers_are_not_phone_numbers(string word)
    {
        Assert.Empty(Boxes([Line("am", word, "um")], 2));
    }

    // Measured: Windows OCR returned "Tel." and "1234567" but no words for "+43 660" between them.
    private static OcrLine Gapped() => new([
        new OcrWord("Tel.", new(new(20, 172), new(67, 196))),
        new OcrWord("1234567", new(new(206, 173), new(321, 196))),
        new OcrWord("abends", new(new(560, 172), new(650, 196))),
    ]);

    [Fact]
    public void A_phone_number_extends_over_unread_gaps_to_its_neighbors()
    {
        Assert.Equal([PixelRect.FromCorners(69, 171, 558, 198)], Boxes([Gapped()], 2));
    }

    [Fact]
    public void A_phone_number_does_not_extend_over_normal_word_spacing()
    {
        var line = new OcrLine([
            new OcrWord("Tel.", new(new(20, 172), new(67, 196))),
            new OcrWord("1234567", new(new(76, 173), new(191, 196))),
        ]);
        Assert.Equal([PixelRect.FromCorners(74, 171, 193, 198)], Boxes([line], 2));
    }

    [Fact]
    public void Several_matches_in_several_lines_are_found_separately()
    {
        var found = Boxes([Line("a@b.at", "x", "c@d.com"), Line("x", "0660", "1234567")], 2);
        Assert.Equal(3, found.Count);
    }

    [Fact]
    public void Join_puts_words_on_lines()
    {
        Assert.Equal($"Hallo Welt{Environment.NewLine}zwei", OcrText.Join([Line("Hallo", "Welt"), Line("zwei")]));
    }

    [Fact]
    public void Join_can_remove_line_breaks()
    {
        Assert.Equal("Hallo Welt zwei", OcrText.Join([Line("Hallo", "Welt"), Line("zwei")], removeLineBreaks: true));
    }

    [Fact]
    public void Sensitive_matches_carry_their_kind()
    {
        var found = OcrText.Sensitive([Line("a@b.at", "x", "0660", "1234567")], 2);
        Assert.Equal([SensitiveKind.Email, SensitiveKind.Phone], found.Select(m => m.Kind));
    }

    // The kind and the words (joined by spaces) each match covers; words sit 100 px apart, boxes padded by 2.
    private static IReadOnlyList<(SensitiveKind Kind, string Covered)> Hits(string text)
    {
        var words = text.Split(' ');
        return [.. OcrText.Sensitive([Line(words)], 2).Select(m =>
            (m.Kind, string.Join(' ', words[((m.Box.X + 2) / 100)..((m.Box.X + m.Box.Width - 82) / 100 + 1)])))];
    }

    [Theory]
    [InlineData("10.10.20.35")]
    [InlineData("192.168.1.0/24")]
    [InlineData("192.168.0.10:8080")]
    [InlineData("255.255.255.255")]
    [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334")]
    [InlineData("2001:db8:85a3::8a2e:370:7334")]
    [InlineData("fe80::1")]
    [InlineData("::1")]
    [InlineData("2001:db8::/32")]
    [InlineData("00:1A:2B:3C:4D:5E")]
    [InlineData("00-1a-2b-3c-4d-5e")]
    [InlineData("001a.2b3c.4d5e")]
    public void IP_and_MAC_addresses_are_found(string address)
    {
        Assert.Equal([(SensitiveKind.IpAddress, address)], Hits($"Host: {address} ok"));
    }

    [Theory]
    [InlineData("v2.3.12")]
    [InlineData("1.2.3.4.5")]
    [InlineData("12:30")]
    [InlineData("12:30:45")]
    [InlineData("00:1a-2b:3c-4d:5e")]
    [InlineData("std::string")]
    public void Versions_times_and_mixed_separators_are_not_addresses(string word)
    {
        Assert.DoesNotContain(Hits($"am {word} um"), h => h.Kind == SensitiveKind.IpAddress);
    }

    [Theory]
    [InlineData("DE89370400440532013000")]
    [InlineData("DE89 3704 0044 0532 0130 00")]
    [InlineData("AT61 1904 3002 3457 3201")]
    [InlineData("AT611904300234573201")]
    [InlineData("CH93 0076 2011 6238 5295 7")]
    [InlineData("GB82 WEST 1234 5698 7654 32")]
    public void An_IBAN_is_found_with_or_without_spaces_and_not_the_label(string iban)
    {
        Assert.Equal([(SensitiveKind.Bank, iban)], Hits($"IBAN: {iban} danke"));
    }

    [Theory]
    [InlineData("DE89 3704 0044")]
    [InlineData("DE8937040044053201300012345")]
    [InlineData("XX89370400440532013000")]
    public void A_too_short_or_unknown_IBAN_is_not_matched(string text)
    {
        Assert.DoesNotContain(Hits($"IBAN: {text}"), h => h.Kind == SensitiveKind.Bank);
    }

    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("4012888888881881")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("3782 822463 10005")]
    [InlineData("1234 5678 9012 3456")]
    public void A_card_number_with_a_valid_checksum_or_4_4_4_4_grouping_is_found(string card)
    {
        Assert.Equal([(SensitiveKind.Bank, card)], Hits($"Karte {card} gueltig"));
    }

    [Theory]
    [InlineData("1234567890123456")]
    [InlineData("1234 5678 9012 345")]
    [InlineData("4111")]
    public void A_non_checksum_number_without_grouping_is_not_a_card(string number)
    {
        Assert.DoesNotContain(Hits($"Nr {number} ok"), h => h.Kind == SensitiveKind.Bank);
    }

    [Theory]
    [InlineData("BIC:", "COBADEFFXXX")]
    [InlineData("SWIFT", "DEUTDEFF")]
    [InlineData("BIC/SWIFT:", "BKAUATWW")]
    public void A_BIC_is_found_only_after_its_label(string label, string bic)
    {
        Assert.Equal([(SensitiveKind.Bank, bic)], Hits($"{label} {bic} Bank"));
        Assert.Empty(Hits($"Code {bic} Bank"));
    }

    [Fact]
    public void A_lowercase_word_after_the_BIC_label_is_not_a_BIC()
    {
        Assert.Empty(Hits("BIC: danke"));
    }

    [Theory]
    [InlineData("sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("sk-proj1234567890abcdefghijklmnop")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("github_pat_11ABCDEFG0abcdefghijkl_mnop")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("xoxb-1234567890-abcdefghij")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U")]
    public void Known_token_shapes_are_found_without_a_label(string token)
    {
        Assert.Equal([(SensitiveKind.Secret, token)], Hits($"dann {token} nie"));
    }

    [Fact]
    public void A_private_key_header_is_found()
    {
        Assert.Equal([(SensitiveKind.Secret, "-----BEGIN RSA PRIVATE KEY-----")], Hits("x -----BEGIN RSA PRIVATE KEY----- y"));
    }

    [Theory]
    [InlineData("Password: hunter2", "hunter2")]
    [InlineData("Passwort: Geheim!1", "Geheim!1")]
    [InlineData("Kennwort=abc123", "Kennwort=abc123")]
    [InlineData("pwd = x9", "x9")]
    [InlineData("api key: 0123456789abcdef0123456789abcdef", "0123456789abcdef0123456789abcdef")]
    [InlineData("API_KEY=Zk3dQ9", "API_KEY=Zk3dQ9")]
    [InlineData("client secret: s3cr3t", "s3cr3t")]
    [InlineData("\"password\": \"hunter2\",", "\"hunter2\",")]
    [InlineData("Password: 123456789", "123456789")]
    public void A_value_after_a_secret_label_is_found_and_never_also_a_phone(string text, string covered)
    {
        Assert.Equal([(SensitiveKind.Secret, covered)], Hits(text));
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("Password:")]
    [InlineData("Passport: AB123456")]
    [InlineData("keyboard: de")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("da39a3ee5e6b4b0d3255bfef95601890afd80709")]
    public void A_label_without_value_and_unlabeled_GUIDs_or_hashes_are_not_secrets(string text)
    {
        Assert.Empty(Hits(text));
    }

    [Theory]
    [InlineData("USt-IdNr. DE123456789 IBAN DE89 3704 0044 0532 0130 00", "DE89 3704 0044 0532 0130 00")]
    [InlineData("DE123456789 DE89370400440532013000", "DE89370400440532013000")]
    [InlineData("IBAN: DE89 3704 0044 0532 0130 0", "DE89 3704 0044 0532 0130 0")]
    [InlineData("IBAN: DE89 3704 0044 0532 0130 001", "DE89 3704 0044 0532 0130 001")]
    [InlineData("IBAN: de89 3704 0044 0532 0130 00", "de89 3704 0044 0532 0130 00")]
    public void An_IBAN_after_a_VAT_ID_misread_by_one_digit_or_lowercase_is_found(string text, string iban)
    {
        Assert.Contains((SensitiveKind.Bank, iban), Hits(text));
    }

    [Theory]
    [InlineData("Karte 4111 1111 1111 1111 12/27")]
    [InlineData("Karte 4111 1111 1111 1111 123")]
    [InlineData("Karte 4111111111111111 123")]
    [InlineData("Nr 12 4111 1111 1111 1111")]
    public void A_card_number_next_to_other_digits_is_found(string text)
    {
        string card = text.Contains("4111111111111111") ? "4111111111111111" : "4111 1111 1111 1111";
        Assert.Contains(Hits(text), h => h.Kind == SensitiveKind.Bank && h.Covered.Contains(card));
    }

    [Fact]
    public void A_secret_value_with_spaces_is_covered_to_the_end_of_the_line()
    {
        Assert.Equal([(SensitiveKind.Secret, "correct horse battery")], Hits("Password : correct horse battery"));
    }

    [Fact]
    public void ClearRedactions_is_one_undo_step_and_undo_restores_them()
    {
        var doc = new EditorDocument(100, 50);
        doc.Redact([new PixelRect(10, 10, 20, 5), new PixelRect(30, 30, 5, 5)]);
        doc.ClearRedactions();
        Assert.Empty(doc.Redactions);
        doc.Undo();
        Assert.Equal(2, doc.Redactions.Count);
    }

    [Fact]
    public void ClearRedactions_without_redactions_adds_no_step()
    {
        var doc = new EditorDocument(100, 50);
        doc.ClearRedactions();
        Assert.False(doc.CanUndo);
    }
}
