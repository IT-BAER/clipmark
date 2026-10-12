using System.Text;

namespace Clipmark.Core.Ai;

public sealed record SseEvent(string? Event, string Data);

/// <summary>Incremental Server-Sent Events reader: feed arbitrary chunks, get the events completed so far.</summary>
public sealed class SseParser
{
    private readonly StringBuilder _pending = new();
    private readonly List<string> _data = [];
    private string? _event;

    public IEnumerable<SseEvent> Feed(string chunk)
    {
        _pending.Append(chunk);
        string text = _pending.ToString();
        var events = new List<SseEvent>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch != '\n' && ch != '\r')
                continue;
            if (ch == '\r' && i + 1 == text.Length)
                break; // the \n of a CRLF may arrive in the next chunk
            Line(text.AsSpan(start, i - start), events);
            if (ch == '\r' && text[i + 1] == '\n')
                i++;
            start = i + 1;
        }
        _pending.Clear().Append(text, start, text.Length - start);
        return events;
    }

    private void Line(ReadOnlySpan<char> line, List<SseEvent> events)
    {
        if (line.IsEmpty)
        {
            if (_data.Count > 0)
                events.Add(new SseEvent(_event, string.Join('\n', _data)));
            _data.Clear();
            _event = null;
            return;
        }
        if (line[0] == ':')
            return;
        int colon = line.IndexOf(':');
        var name = colon < 0 ? line : line[..colon];
        var value = colon < 0 ? default : line[(colon + 1)..];
        if (!value.IsEmpty && value[0] == ' ')
            value = value[1..];
        if (name.SequenceEqual("event"))
            _event = value.ToString();
        else if (name.SequenceEqual("data"))
            _data.Add(value.ToString());
    }
}
