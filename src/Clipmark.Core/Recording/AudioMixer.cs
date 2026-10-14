using System.Buffers.Binary;

namespace Clipmark.Core.Recording;

public static class Pcm
{
    /// <summary>Interleaved little-endian samples (16 / 24 / 32-bit integer or 32-bit float) as floats in [-1, 1].</summary>
    public static float[] ToFloat(ReadOnlySpan<byte> data, int bitsPerSample, bool isFloat)
    {
        int size = bitsPerSample / 8;
        var result = new float[data.Length / size];
        for (int i = 0; i < result.Length; i++)
        {
            var s = data.Slice(i * size, size);
            result[i] = (bitsPerSample, isFloat) switch
            {
                (32, true) => BinaryPrimitives.ReadSingleLittleEndian(s),
                (16, false) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                (24, false) => ((s[0] << 8 | s[1] << 16 | s[2] << 24) >> 8) / 8388608f,
                (32, false) => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
                _ => throw new NotSupportedException($"{bitsPerSample}-bit {(isFloat ? "float" : "integer")} audio is not supported."),
            };
        }
        return result;
    }
}

/// <summary>
/// Mixes audio sources into 48 kHz stereo 16-bit. Each source is converted to stereo and resampled on push; a pull takes
/// the same number of frames from every source and fills what a source has not delivered with silence. Thread-safe.
/// </summary>
public sealed class AudioMixer
{
    public const int SampleRate = 48000, Channels = 2;

    /// <summary>Timestamp differences up to 2 ms are jitter; the audio stays contiguous.</summary>
    private const int Tolerance = 96;

    private readonly Source[] _sources;
    private readonly int _maxBacklog;
    private readonly object _lock = new();

    public AudioMixer(int sourceCount, int maxBacklogMs = 300)
    {
        _sources = new Source[sourceCount];
        for (int i = 0; i < sourceCount; i++)
            _sources[i] = new Source();
        _maxBacklog = SampleRate * maxBacklogMs / 1000;
    }

    /// <summary>Output frames handed out by <see cref="Pull"/> so far.</summary>
    public long FramesProduced { get; private set; }

    /// <summary>Frames of source <paramref name="source"/> waiting for the next pull.</summary>
    public int Buffered(int source)
    {
        lock (_lock)
            return _sources[source].Count / Channels;
    }

    /// <summary>
    /// A muted source adds silence in place of the audio pushed while muted; audio already queued still plays (with the
    /// recorder's lag up to about 100 ms after the mute).
    /// </summary>
    public void SetMuted(int source, bool muted)
    {
        lock (_lock)
            _sources[source].Muted = muted;
    }

    /// <summary>
    /// Adds source audio. With <paramref name="atFrame"/> (output frame index of its first sample) a gap before it becomes
    /// silence and a part that overlaps audio already queued or mixed is dropped.
    /// </summary>
    public void Push(int source, ReadOnlySpan<float> interleaved, int channels, int sampleRate, long? atFrame = null)
    {
        lock (_lock)
        {
            var s = _sources[source];
            if (s.Muted)
                interleaved = new float[interleaved.Length];
            long gap = atFrame is { } at ? at - (FramesProduced + s.Count / Channels) : 0;
            if (gap > Tolerance)
                s.AddSilence((int)Math.Min(gap, _maxBacklog));
            int before = s.Count;
            s.Append(interleaved, channels, sampleRate);
            if (gap < -Tolerance)
                s.RemoveRange(before, (int)Math.Min(-gap * Channels, s.Count - before));
            int excess = s.Count / Channels - _maxBacklog;
            if (excess > 0)
                s.Drop(excess * Channels);
        }
    }

    /// <summary>Interleaved stereo 16-bit samples for <paramref name="frames"/> frames.</summary>
    public short[] Pull(int frames)
    {
        var mix = new float[frames * Channels];
        lock (_lock)
        {
            foreach (var s in _sources)
            {
                int n = Math.Min(mix.Length, s.Count);
                for (int i = 0; i < n; i++)
                    mix[i] += s.At(i);
                s.Drop(n);
            }
            FramesProduced += frames;
        }
        var result = new short[mix.Length];
        for (int i = 0; i < mix.Length; i++)
            result[i] = (short)Math.Clamp(MathF.Round(mix[i] * 32768f), short.MinValue, short.MaxValue);
        return result;
    }

    /// <summary>Resampled stereo output waiting to be mixed, plus the resampler's unconsumed input.</summary>
    private sealed class Source
    {
        private float[] _out = new float[4096];
        private int _start, _count;
        private readonly List<float> _input = []; // stereo frames at the source rate
        private double _phase; // position between _input frames

        public int Count => _count;

        public bool Muted;

        public float At(int i) => _out[_start + i];

        public void Drop(int n)
        {
            _start += n;
            _count -= n;
            if (_count == 0)
                _start = 0;
        }

        public void AddSilence(int frames)
        {
            for (int i = 0; i < frames; i++)
                Add(0, 0);
        }

        /// <summary>Removes <paramref name="n"/> samples starting <paramref name="offset"/> samples after the head.</summary>
        public void RemoveRange(int offset, int n)
        {
            Array.Copy(_out, _start + offset + n, _out, _start + offset, _count - offset - n);
            _count -= n;
        }

        private void Add(float left, float right)
        {
            if (_start + _count + 2 > _out.Length)
            {
                var next = _count + 2 > _out.Length / 2 ? new float[_out.Length * 2] : _out;
                Array.Copy(_out, _start, next, 0, _count);
                _out = next;
                _start = 0;
            }
            _out[_start + _count++] = left;
            _out[_start + _count++] = right;
        }

        public void Append(ReadOnlySpan<float> data, int channels, int rate)
        {
            for (int i = 0; i + channels <= data.Length; i += channels)
            {
                float l = data[i], r = channels > 1 ? data[i + 1] : l;
                if (channels >= 3)
                {
                    // Surround: fold the center into both sides.
                    l += 0.707f * data[i + 2];
                    r += 0.707f * data[i + 2];
                }
                if (rate == SampleRate)
                    Add(l, r);
                else
                {
                    _input.Add(l);
                    _input.Add(r);
                }
            }
            if (rate == SampleRate)
                return;
            double step = (double)rate / SampleRate;
            int frames = _input.Count / 2;
            while (_phase + 1 < frames)
            {
                int k = (int)_phase;
                float t = (float)(_phase - k);
                Add(_input[2 * k] + (_input[2 * k + 2] - _input[2 * k]) * t, _input[2 * k + 1] + (_input[2 * k + 3] - _input[2 * k + 1]) * t);
                _phase += step;
            }
            int used = Math.Min((int)_phase, frames - 1);
            _input.RemoveRange(0, used * 2);
            _phase -= used;
        }
    }
}
