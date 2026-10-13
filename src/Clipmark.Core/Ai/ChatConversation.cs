namespace Clipmark.Core.Ai;

public enum ChatRole { User, Assistant }

public sealed record ChatTurn(ChatRole Role, string Text, string Thinking = "");

/// <summary>One screenshot and its question/answer turns. When not busy the turns strictly alternate, starting with the user.</summary>
public sealed class ChatConversation
{
    private readonly List<ChatTurn> _turns = [];

    public string? ImageBase64 { get; private set; }
    public string? MediaType { get; private set; }
    public bool IsBusy { get; private set; }

    /// <summary>While busy, the last turn is the growing answer once the first delta has arrived.</summary>
    public IReadOnlyList<ChatTurn> Turns => _turns;

    public void Start(string imageBase64, string mediaType)
    {
        ImageBase64 = imageBase64;
        MediaType = mediaType;
        _turns.Clear();
        IsBusy = false;
    }

    public void Reset()
    {
        ImageBase64 = null;
        MediaType = null;
        _turns.Clear();
        IsBusy = false;
    }

    public void Ask(string question)
    {
        if (IsBusy || ImageBase64 is null || string.IsNullOrWhiteSpace(question))
            throw new InvalidOperationException("Cannot ask now.");
        _turns.Add(new ChatTurn(ChatRole.User, question));
        IsBusy = true;
    }

    public void AppendDelta(string text)
    {
        if (!IsBusy)
            return;
        if (_turns[^1].Role == ChatRole.Assistant)
            _turns[^1] = _turns[^1] with { Text = _turns[^1].Text + text };
        else
            _turns.Add(new ChatTurn(ChatRole.Assistant, text));
    }

    public void AppendThinking(string text)
    {
        if (!IsBusy)
            return;
        if (_turns[^1].Role == ChatRole.Assistant)
            _turns[^1] = _turns[^1] with { Thinking = _turns[^1].Thinking + text };
        else
            _turns.Add(new ChatTurn(ChatRole.Assistant, "", text));
    }

    /// <summary>An answer without any text is dropped together with its question so the turns keep alternating.</summary>
    public void Complete(string? stopReason) => Fail();

    /// <summary>Keeps a partial answer; otherwise removes the pending question and returns its text for the input box.</summary>
    public string? Fail()
    {
        if (!IsBusy)
            return null;
        IsBusy = false;
        if (_turns[^1].Role == ChatRole.Assistant)
        {
            if (!string.IsNullOrWhiteSpace(_turns[^1].Text))
                return null;
            _turns.RemoveAt(_turns.Count - 1);
        }
        string question = _turns[^1].Text;
        _turns.RemoveAt(_turns.Count - 1);
        return question;
    }

    public string? Cancel() => Fail();
}
