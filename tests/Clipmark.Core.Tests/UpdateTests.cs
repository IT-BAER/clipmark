using Clipmark.Core.Settings;
using Clipmark.Core.Update;

namespace Clipmark.Core.Tests;

public sealed class UpdateReleaseTests
{
    [Fact]
    public void The_page_link_is_built_from_the_tag_and_never_taken_from_the_response()
    {
        var release = UpdateRelease.Parse("""{"tag_name":"v1.2.0","html_url":"https://evil.example/x","draft":false,"prerelease":false}""");

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal("https://github.com/IT-BAER/clipmark/releases/tag/v1.2.0", release.PageUrl);
    }

    [Theory]
    [InlineData("""{"tag_name":"v1.2.0","draft":true,"prerelease":false}""")]
    [InlineData("""{"tag_name":"v1.2.0","draft":false,"prerelease":true}""")]
    [InlineData("""{"tag_name":"v1.2","draft":false,"prerelease":false}""")]
    [InlineData("""{"tag_name":"1.2.3-beta","draft":false,"prerelease":false}""")]
    [InlineData("""{"tag_name":"v1.2.3/../x","draft":false,"prerelease":false}""")]
    [InlineData("""{"tag_name":"","draft":false,"prerelease":false}""")]
    [InlineData("""{"draft":false}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void A_draft_a_prerelease_or_an_odd_tag_is_no_release(string json)
    {
        Assert.Null(UpdateRelease.Parse(json));
    }

    [Theory]
    [InlineData("1.1.0", "v1.2.0", true)]
    [InlineData("1.1.0", "v1.1.0", false)]
    [InlineData("1.1.0", "v1.0.0", false)]
    [InlineData("1.9.0", "v1.10.0", true)]
    [InlineData("1.1.0", "1.1.1", true)]
    public void A_release_is_newer_only_by_version(string current, string tag, bool expected)
    {
        var release = UpdateRelease.Parse($$"""{"tag_name":"{{tag}}","draft":false,"prerelease":false}""")!;

        Assert.Equal(expected, release.IsNewerThan(current));
    }

    [Fact]
    public void An_unreadable_own_version_is_never_older()
    {
        var release = UpdateRelease.Parse("""{"tag_name":"v9.0.0"}""")!;

        Assert.False(release.IsNewerThan("dev"));
    }
}

public sealed class UpdatePolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly UpdateRelease V12 = UpdateRelease.Parse("""{"tag_name":"v1.2.0"}""")!;
    private static readonly UpdateRelease V13 = UpdateRelease.Parse("""{"tag_name":"v1.3.0"}""")!;

    [Fact]
    public void A_check_is_due_when_never_made_or_a_day_old()
    {
        Assert.True(UpdatePolicy.ShouldCheck(new ClipmarkSettings(), Now));
        Assert.False(UpdatePolicy.ShouldCheck(new ClipmarkSettings { UpdateLastCheckUtc = Now.AddHours(-23) }, Now));
        Assert.True(UpdatePolicy.ShouldCheck(new ClipmarkSettings { UpdateLastCheckUtc = Now.AddHours(-24) }, Now));
    }

    [Fact]
    public void A_last_check_in_the_future_is_a_clock_set_back_and_does_not_block()
    {
        Assert.True(UpdatePolicy.ShouldCheck(new ClipmarkSettings { UpdateLastCheckUtc = Now.AddDays(5) }, Now));
    }

    [Fact]
    public void Never_turns_the_check_and_the_prompt_off()
    {
        var off = new ClipmarkSettings { UpdateCheck = false };

        Assert.False(UpdatePolicy.ShouldCheck(off, Now));
        Assert.False(UpdatePolicy.ShouldPrompt(off, V12, "1.1.0", Now));
    }

    [Fact]
    public void A_prompt_needs_a_newer_release()
    {
        Assert.True(UpdatePolicy.ShouldPrompt(new ClipmarkSettings(), V12, "1.1.0", Now));
        Assert.False(UpdatePolicy.ShouldPrompt(new ClipmarkSettings(), V12, "1.2.0", Now));
    }

    [Fact]
    public void Later_is_quiet_for_three_days_for_that_version_only()
    {
        var later = new ClipmarkSettings { UpdateLaterVersion = "1.2.0", UpdateRemindAfterUtc = Now.AddDays(3) };

        Assert.False(UpdatePolicy.ShouldPrompt(later, V12, "1.1.0", Now.AddDays(2)));
        Assert.True(UpdatePolicy.ShouldPrompt(later, V12, "1.1.0", Now.AddDays(3)));
        Assert.True(UpdatePolicy.ShouldPrompt(later, V13, "1.1.0", Now.AddDays(1)));
    }

    [Fact]
    public void A_later_far_in_the_future_is_a_clock_set_back_and_does_not_silence_the_prompt()
    {
        var later = new ClipmarkSettings { UpdateLaterVersion = "1.2.0", UpdateRemindAfterUtc = Now.AddDays(40) };

        Assert.True(UpdatePolicy.ShouldPrompt(later, V12, "1.1.0", Now));
    }

    [Fact]
    public void Later_sets_the_version_and_the_time()
    {
        var s = UpdatePolicy.Postpone(new ClipmarkSettings(), V12, Now);

        Assert.Equal("1.2.0", s.UpdateLaterVersion);
        Assert.Equal(Now.AddDays(3), s.UpdateRemindAfterUtc);
        Assert.True(s.UpdateCheck);
    }

    [Fact]
    public void Update_state_survives_a_settings_round_trip()
    {
        var s = new ClipmarkSettings
        {
            UpdateCheck = false, UpdateLastCheckUtc = Now, UpdateLaterVersion = "1.2.0", UpdateRemindAfterUtc = Now.AddDays(3),
        };

        var back = SettingsStore.Parse(SettingsStore.Serialize(s));

        Assert.False(back.UpdateCheck);
        Assert.Equal(Now, back.UpdateLastCheckUtc);
        Assert.Equal("1.2.0", back.UpdateLaterVersion);
        Assert.Equal(Now.AddDays(3), back.UpdateRemindAfterUtc);
    }

    [Fact]
    public void Update_state_defaults_to_checking_with_nothing_remembered()
    {
        var s = SettingsStore.Parse("{}");

        Assert.True(s.UpdateCheck);
        Assert.Null(s.UpdateLastCheckUtc);
        Assert.Null(s.UpdateLaterVersion);
        Assert.Null(s.UpdateRemindAfterUtc);
    }

    [Fact]
    public void An_unreadable_update_time_falls_back_to_none()
    {
        var s = SettingsStore.Parse("""{"updateLastCheckUtc":"yesterday","updateRemindAfterUtc":5,"updateLaterVersion":"../x"}""");

        Assert.Null(s.UpdateLastCheckUtc);
        Assert.Null(s.UpdateRemindAfterUtc);
        Assert.Null(s.UpdateLaterVersion);
    }
}
