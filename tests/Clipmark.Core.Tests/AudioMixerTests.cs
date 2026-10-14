using Clipmark.Core.Recording;

namespace Clipmark.Core.Tests;

public class AudioMixerTests
{
    [Fact]
    public void Pcm16_and_float32_convert_to_unit_floats()
    {
        byte[] pcm16 = [0x00, 0x40, 0x00, 0xC0]; // 16384, -16384
        Assert.Equal([0.5f, -0.5f], Pcm.ToFloat(pcm16, 16, isFloat: false));
        byte[] f32 = [.. BitConverter.GetBytes(0.25f), .. BitConverter.GetBytes(-1f)];
        Assert.Equal([0.25f, -1f], Pcm.ToFloat(f32, 32, isFloat: true));
    }

    [Fact]
    public void Pcm24_and_pcm32_convert_to_unit_floats()
    {
        byte[] pcm24 = [0x00, 0x00, 0x40, 0x00, 0x00, 0xC0];
        Assert.Equal([0.5f, -0.5f], Pcm.ToFloat(pcm24, 24, isFloat: false));
        byte[] pcm32 = [.. BitConverter.GetBytes(0x40000000), .. BitConverter.GetBytes(int.MinValue)];
        Assert.Equal([0.5f, -1f], Pcm.ToFloat(pcm32, 32, isFloat: false));
    }

    [Fact]
    public void Stereo_48k_passes_through_as_16_bit()
    {
        var mixer = new AudioMixer(1);
        mixer.Push(0, [0.5f, -0.5f, 0.25f, -0.25f], 2, 48000);
        Assert.Equal([16384, -16384, 8192, -8192], mixer.Pull(2).Select(s => (int)s));
    }

    [Fact]
    public void Missing_data_is_filled_with_silence()
    {
        var mixer = new AudioMixer(1);
        mixer.Push(0, [0.5f, 0.5f], 2, 48000);
        Assert.Equal([16384, 16384, 0, 0, 0, 0], mixer.Pull(3).Select(s => (int)s));
        Assert.Equal(3, mixer.FramesProduced);
    }

    [Fact]
    public void Two_sources_are_summed_and_clipped()
    {
        var mixer = new AudioMixer(2);
        mixer.Push(0, [0.5f, 0.75f], 2, 48000);
        mixer.Push(1, [0.25f, 0.75f], 2, 48000);
        Assert.Equal([24576, short.MaxValue], mixer.Pull(1).Select(s => (int)s));
    }

    [Fact]
    public void Mono_is_copied_to_both_channels()
    {
        var mixer = new AudioMixer(1);
        mixer.Push(0, [0.5f, -0.5f], 1, 48000);
        Assert.Equal([16384, 16384, -16384, -16384], mixer.Pull(2).Select(s => (int)s));
    }

    [Fact]
    public void Other_rates_are_resampled_to_48k()
    {
        var mixer = new AudioMixer(1);
        // 24 kHz ramp 0, 0.5 (mono): at 48 kHz every second output frame lies halfway.
        mixer.Push(0, [0f, 0.5f, 0.5f], 1, 24000);
        var left = mixer.Pull(4).Where((_, i) => i % 2 == 0).Select(s => (int)s).ToArray();
        Assert.Equal([0, 8192, 16384, 16384], left);
    }

    [Fact]
    public void One_second_at_44100_gives_one_second_at_48000()
    {
        var mixer = new AudioMixer(1, maxBacklogMs: 2000);
        var block = Enumerable.Repeat(0.5f, 441).ToArray();
        for (int i = 0; i < 100; i++)
            mixer.Push(0, block, 1, 44100);
        Assert.InRange(mixer.Buffered(0), 47_990, 48_000);
    }

    [Fact]
    public void Backlog_is_capped_by_dropping_the_oldest_audio()
    {
        var mixer = new AudioMixer(1, maxBacklogMs: 10); // 480 frames
        mixer.Push(0, Enumerable.Repeat(0.5f, 960).ToArray(), 2, 48000);
        mixer.Push(0, Enumerable.Repeat(-0.5f, 960).ToArray(), 2, 48000);
        Assert.Equal(480, mixer.Buffered(0));
        Assert.All(mixer.Pull(480), s => Assert.Equal(-16384, s));
    }

    [Fact]
    public void Positioned_push_after_a_gap_starts_at_its_position()
    {
        var mixer = new AudioMixer(1);
        mixer.Push(0, [0.5f, 0.5f], 2, 48000, atFrame: 200);
        var pulled = mixer.Pull(201);
        Assert.All(pulled[..400], s => Assert.Equal(0, s));
        Assert.Equal([16384, 16384], pulled[400..]);
    }

    [Fact]
    public void Positioned_push_that_arrives_late_drops_what_was_already_mixed()
    {
        var mixer = new AudioMixer(1);
        mixer.Pull(300);
        var block = Enumerable.Range(0, 400).SelectMany(i => new[] { i < 200 ? -0.5f : 0.5f, i < 200 ? -0.5f : 0.5f }).ToArray();
        mixer.Push(0, block, 2, 48000, atFrame: 100); // frames 100..299 are already out
        Assert.Equal(200, mixer.Buffered(0));
        Assert.All(mixer.Pull(200), s => Assert.Equal(16384, s));
    }

    [Fact]
    public void Small_timestamp_jitter_keeps_the_audio_contiguous()
    {
        var mixer = new AudioMixer(1);
        mixer.Push(0, Enumerable.Repeat(0.5f, 960).ToArray(), 2, 48000, atFrame: 0);
        mixer.Push(0, Enumerable.Repeat(0.5f, 960).ToArray(), 2, 48000, atFrame: 490); // 10 frames late: jitter
        Assert.Equal(960, mixer.Buffered(0));
    }

    [Fact]
    public void A_muted_source_is_silent_and_unmute_mixes_it_again()
    {
        var mixer = new AudioMixer(2);
        mixer.SetMuted(1, true);
        mixer.Push(0, [0.5f, 0.5f], 2, 48000);
        mixer.Push(1, [0.25f, 0.25f], 2, 48000);
        Assert.Equal([16384, 16384], mixer.Pull(1).Select(s => (int)s));
        Assert.Equal(0, mixer.Buffered(1)); // the muted audio kept its place in the queue

        mixer.SetMuted(0, true);
        mixer.Push(0, [0.5f, 0.5f], 2, 48000);
        mixer.Push(1, [0.25f, 0.25f], 2, 48000);
        Assert.Equal([0, 0], mixer.Pull(1).Select(s => (int)s));

        mixer.SetMuted(0, false);
        mixer.SetMuted(1, false);
        mixer.Push(0, [0.5f, 0.5f], 2, 48000);
        mixer.Push(1, [0.25f, 0.25f], 2, 48000);
        Assert.Equal([24576, 24576], mixer.Pull(1).Select(s => (int)s));
    }

    [Fact]
    public void Audio_pushed_before_mute_still_plays()
    {
        var mixer = new AudioMixer(1);
        mixer.Push(0, [0.5f, 0.5f], 2, 48000);
        mixer.SetMuted(0, true);
        mixer.Push(0, [0.5f, 0.5f], 2, 48000, atFrame: 1);
        Assert.Equal([16384, 16384, 0, 0], mixer.Pull(2).Select(s => (int)s));
    }
}
