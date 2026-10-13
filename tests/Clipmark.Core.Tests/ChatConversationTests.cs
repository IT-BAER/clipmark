using Clipmark.Core.Ai;

namespace Clipmark.Core.Tests;

public sealed class ChatConversationTests
{
    private static ChatConversation Started()
    {
        var c = new ChatConversation();
        c.Start("QUJD", "image/png");
        return c;
    }

    [Fact]
    public void Ask_adds_a_user_turn_and_marks_the_conversation_busy()
    {
        var c = Started();
        c.Ask("what is this?");

        Assert.True(c.IsBusy);
        Assert.Equal([(ChatRole.User, "what is this?")], c.Turns.Select(t => (t.Role, t.Text)));
    }

    [Fact]
    public void Ask_rejects_a_busy_conversation_a_missing_image_and_a_blank_question()
    {
        var c = Started();
        Assert.Throws<InvalidOperationException>(() => c.Ask("  "));
        c.Ask("one");
        Assert.Throws<InvalidOperationException>(() => c.Ask("two"));
        Assert.Throws<InvalidOperationException>(() => new ChatConversation().Ask("no image"));
    }

    [Fact]
    public void Deltas_grow_the_last_assistant_turn_and_Complete_finishes_it()
    {
        var c = Started();
        c.Ask("q");
        c.AppendDelta("Hel");
        c.AppendDelta("lo");

        Assert.True(c.IsBusy);
        Assert.Equal((ChatRole.Assistant, "Hello"), (c.Turns[^1].Role, c.Turns[^1].Text));

        c.Complete("end_turn");
        Assert.False(c.IsBusy);
        Assert.Equal(2, c.Turns.Count);
    }

    [Fact]
    public void Fail_without_text_removes_the_question_and_returns_it()
    {
        var c = Started();
        c.Ask("my question");

        Assert.Equal("my question", c.Fail());
        Assert.False(c.IsBusy);
        Assert.Empty(c.Turns);
    }

    [Fact]
    public void Fail_and_Cancel_with_partial_text_keep_the_answer()
    {
        foreach (var end in new Func<ChatConversation, string?>[] { c => c.Fail(), c => c.Cancel() })
        {
            var c = Started();
            c.Ask("q");
            c.AppendDelta("partial");

            Assert.Null(end(c));
            Assert.False(c.IsBusy);
            Assert.Equal([(ChatRole.User, "q"), (ChatRole.Assistant, "partial")], c.Turns.Select(t => (t.Role, t.Text)));
        }
    }

    [Fact]
    public void Cancel_without_text_returns_the_question()
    {
        var c = Started();
        c.Ask("q");
        Assert.Equal("q", c.Cancel());
        Assert.Empty(c.Turns);
    }

    [Fact]
    public void Fail_when_idle_returns_null()
    {
        Assert.Null(Started().Fail());
    }

    [Fact]
    public void Thinking_grows_the_assistant_turn_and_survives_text_deltas()
    {
        var c = Started();
        c.Ask("q");
        c.AppendThinking("a");
        c.AppendThinking("b");
        Assert.Equal((ChatRole.Assistant, "", "ab"), (c.Turns[^1].Role, c.Turns[^1].Text, c.Turns[^1].Thinking));

        c.AppendDelta("Hi");
        c.Complete("end_turn");
        Assert.Equal(("Hi", "ab"), (c.Turns[^1].Text, c.Turns[^1].Thinking));
    }

    [Fact]
    public void AppendThinking_is_ignored_when_not_busy()
    {
        var c = Started();
        c.AppendThinking("x");
        Assert.Empty(c.Turns);
    }

    [Fact]
    public void Thinking_only_counts_as_no_answer()
    {
        var c = Started();
        c.Ask("q");
        c.AppendThinking("hmm");
        c.AppendDelta("  ");

        Assert.Equal("q", c.Fail());
        Assert.Empty(c.Turns);
    }

    [Fact]
    public void Start_and_Reset_clear_the_turns_and_Reset_drops_the_image()
    {
        var c = Started();
        c.Ask("q");
        c.AppendDelta("a");
        c.Complete(null);

        c.Start("WFla", "image/jpeg");
        Assert.Empty(c.Turns);
        Assert.False(c.IsBusy);
        Assert.Equal(("WFla", "image/jpeg"), (c.ImageBase64, c.MediaType));

        c.Reset();
        Assert.Null(c.ImageBase64);
        Assert.Throws<InvalidOperationException>(() => c.Ask("q"));
    }

    [Fact]
    public void History_alternates_user_and_assistant_whenever_not_busy()
    {
        var c = Started();
        c.Ask("1"); c.AppendDelta("a"); c.Complete("end_turn");
        c.Ask("2"); c.Fail();
        c.Ask("3"); c.AppendDelta("b"); c.AppendDelta("c"); c.Fail();
        c.Ask("4"); c.AppendDelta("d"); c.Complete("max_tokens");

        Assert.False(c.IsBusy);
        for (int i = 0; i < c.Turns.Count; i++)
            Assert.Equal(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, c.Turns[i].Role);
        Assert.Equal(["1", "a", "3", "bc", "4", "d"], c.Turns.Select(t => t.Text));
    }
}
