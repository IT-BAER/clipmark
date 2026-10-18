namespace Clipmark.Core.Ai;

/// <summary>
/// Report of an Ask AI answer: a mailto: link with the question and answer, opened in the user's mail
/// app, so the user sees and sends the mail; the app sends nothing.
/// </summary>
public static class AiReport
{
    public const string Address = "admin@it-baer.net";
    public const string CutMarker = "[…]";

    /// <summary>Outlook classic and some shell paths drop longer mailto: links.</summary>
    public const int MaxUriLength = 2000;

    /// <summary>The text is cut from the end until the escaped link fits, so the question and the start of the answer stay.</summary>
    public static Uri MailUri(string version, string model, string question, string answer)
    {
        string text = $"Question:\r\n{question}\r\n\r\nAnswer:\r\n{answer}".ReplaceLineEndings("\r\n");
        for (int keep = text.Length; ; keep = keep * 3 / 4)
        {
            string body = $"\r\n\r\n---\r\nClipmark {version}, model {model}\r\n\r\n{Cut(text, keep)}";
            var uri = new Uri($"mailto:{Address}?subject={Uri.EscapeDataString("Clipmark: AI answer report")}&body={Uri.EscapeDataString(body)}");
            if (uri.AbsoluteUri.Length <= MaxUriLength || keep == 0)
                return uri;
        }
    }

    private static string Cut(string text, int keep)
    {
        if (keep >= text.Length)
            return text;
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
            keep--; // never split a surrogate pair
        return text[..keep] + CutMarker;
    }
}
