using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Clipmark.Core.Recording;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Recording;

/// <summary>
/// Records one monitor region to MP4: Windows.Graphics.Capture frames are cropped with Win2D at a constant 30 fps
/// (the newest frame per tick, the last one again while the screen is static) and fed with the mixed WASAPI audio
/// through a MediaStreamSource into MediaTranscoder (H.264 + AAC).
/// </summary>
internal sealed class ScreenRecorder
{
    public const int Fps = 30;
    private static readonly long TickQpc = Stopwatch.Frequency / Fps;
    private static readonly TimeSpan FrameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / Fps);
    // The mixer stays this far behind the wall clock so late audio packets still land at their own position.
    private static readonly long AudioLagQpc = Stopwatch.Frequency / 10;

    private readonly RecordingArea _area;
    private readonly int _width, _height;
    private readonly CanvasDevice _device = new();
    private readonly BlockingCollection<MediaStreamSample> _video = new(boundedCapacity: 8);
    private readonly BlockingCollection<MediaStreamSample>? _audio;
    private readonly List<string> _warnings = [];
    private readonly bool _systemAudio, _microphone;

    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private IRandomAccessStream? _stream;
    private Task? _transcode;
    private Thread? _videoThread, _audioThread;
    private CanvasRenderTarget? _latest;
    private long _start, _stopAt = long.MaxValue;
    private volatile bool _stopping;
    private Exception? _failure;

    public string Path { get; }

    /// <summary>Audio sources that could not be opened, as user-facing sentences.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Capture or audio failed; the session stops and reports it.</summary>
    public bool Faulted => _failure is not null || _transcode?.IsFaulted == true;

    public TimeSpan Elapsed => _start == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_start);

    private ScreenRecorder(RecordingArea area, string path, bool systemAudio, bool microphone)
    {
        _area = area;
        _width = area.Region.Width;
        _height = area.Region.Height;
        Path = path;
        _systemAudio = systemAudio;
        _microphone = microphone;
        if (systemAudio || microphone)
            _audio = new BlockingCollection<MediaStreamSample>();
    }

    /// <summary>Opens the file and the encoder and starts capturing; throws with a readable message when that fails.</summary>
    public static async Task<ScreenRecorder> StartAsync(RecordingArea area, string path, bool systemAudio, bool microphone)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new InvalidOperationException(Loc.Get("RecordingUnsupported"));
        var recorder = new ScreenRecorder(area, path, systemAudio, microphone);
        try
        {
            await recorder.StartCoreAsync();
            return recorder;
        }
        catch
        {
            recorder.Release();
            throw;
        }
    }

    private async Task StartCoreAsync()
    {
        try
        {
            // Windows 11: without this the capture draws a yellow frame around the monitor.
            await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
        }
        catch (Exception) { }

        _item = CaptureItems.ForMonitor(_area.Monitor);
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _item.Size);
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = true;
        try { _session.IsBorderRequired = false; } catch (Exception) { }

        var videoProps = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_width, (uint)_height);
        var source = new MediaStreamSource(new VideoStreamDescriptor(videoProps)) { BufferTime = TimeSpan.Zero, CanSeek = false };
        if (_audio is not null)
            source.AddStreamDescriptor(new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(AudioMixer.SampleRate, AudioMixer.Channels, 16)));
        source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += OnSampleRequested;

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = (uint)_width;
        profile.Video.Height = (uint)_height;
        profile.Video.Bitrate = VideoBitrate.For(_width, _height, Fps);
        profile.Video.FrameRate.Numerator = Fps;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;
        profile.Audio = _audio is null ? null : AudioEncodingProperties.CreateAac(AudioMixer.SampleRate, AudioMixer.Channels, 192000);

        _stream = await FileRandomAccessStream.OpenAsync(Path, FileAccessMode.ReadWrite, StorageOpenOptions.None, FileOpenDisposition.CreateAlways);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, _stream, profile);
        if (!prepared.CanTranscode)
            throw new InvalidOperationException(Loc.Format("RecordingNoEncoder", prepared.FailureReason));

        _session.StartCapture();
        // The clock starts with the first captured frame; otherwise frame 0 is the black clear (black preview and thumbnail).
        for (long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2; _latest is null && Stopwatch.GetTimestamp() < deadline;)
        {
            TakeNewestFrame();
            if (_latest is null)
                await Task.Delay(5);
        }
        _start = Stopwatch.GetTimestamp();
        _videoThread = new Thread(VideoLoop) { IsBackground = true, Name = "Clipmark video", Priority = ThreadPriority.AboveNormal };
        _videoThread.Start();
        if (_audio is not null)
        {
            var opened = new TaskCompletionSource();
            _audioThread = new Thread(() => AudioLoop(opened)) { IsBackground = true, Name = "Clipmark audio", Priority = ThreadPriority.AboveNormal };
            _audioThread.Start();
            await opened.Task;
        }
        _transcode = prepared.TranscodeAsync().AsTask();
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs e)
    {
        // Runs on a transcoder thread; blocking here until the next sample is how a live source paces the encoder.
        var queue = e.Request.StreamDescriptor is VideoStreamDescriptor ? _video : _audio!;
        e.Request.Sample = queue.TryTake(out var sample, Timeout.Infinite) ? sample : null;
    }

    private void VideoLoop()
    {
        try
        {
            long next = _start;
            while (!_stopping && next < _stopAt)
            {
                long wait = next - Stopwatch.GetTimestamp();
                if (wait > 0)
                    Thread.Sleep(TimeSpan.FromTicks(wait * TimeSpan.TicksPerSecond / Stopwatch.Frequency));
                if (_stopping)
                    break;
                TakeNewestFrame();
                var target = new CanvasRenderTarget(_device, _width, _height, 96);
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(Microsoft.UI.Colors.Black);
                    if (_latest is not null)
                        ds.DrawImage(_latest);
                }
                var sample = MediaStreamSample.CreateFromDirect3D11Surface(target, Timestamp(next));
                sample.Duration = FrameDuration;
                sample.Processed += (_, _) => target.Dispose();
                if (!_video.TryAdd(sample))
                    target.Dispose(); // encoder behind: drop this frame
                next += TickQpc;
                if (Stopwatch.GetTimestamp() - next > TickQpc * 4)
                    next = Stopwatch.GetTimestamp(); // fell far behind (sleep, debugger): skip instead of bursting
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }
        finally
        {
            _video.CompleteAdding();
        }
    }

    /// <summary>Copies the newest captured frame (if any arrived since the last tick) cropped into <see cref="_latest"/>.</summary>
    private void TakeNewestFrame()
    {
        Direct3D11CaptureFrame? newest = null;
        while (_pool!.TryGetNextFrame() is { } frame)
        {
            newest?.Dispose();
            newest = frame;
        }
        if (newest is null)
            return;
        using (newest)
        using (var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(_device, newest.Surface))
        {
            _latest ??= new CanvasRenderTarget(_device, _width, _height, 96);
            using var ds = _latest.CreateDrawingSession();
            ds.Clear(Microsoft.UI.Colors.Black);
            var r = _area.Region;
            ds.DrawImage(bitmap, new Rect(0, 0, _width, _height),
                new Rect(r.X - _area.Monitor.X, r.Y - _area.Monitor.Y, _width, _height), 1, CanvasImageInterpolation.NearestNeighbor);
        }
    }

    private void AudioLoop(TaskCompletionSource opened)
    {
        CoInitializeEx(0, COINIT_MULTITHREADED);
        var sources = new List<WasapiCapture>();
        try
        {
            foreach (bool loopback in new[] { true, false })
            {
                if (!(loopback ? _systemAudio : _microphone))
                    continue;
                if (WasapiCapture.TryOpen(loopback, out var error) is { } capture)
                    sources.Add(capture);
                else
                    lock (_warnings) _warnings.Add(error!);
            }
            foreach (var s in sources)
                s.Start();
        }
        catch (Exception e)
        {
            lock (_warnings) _warnings.Add(Loc.Format("AudioFailed", e.Message));
        }
        opened.TrySetResult();

        var mixer = new AudioMixer(Math.Max(1, sources.Count));
        // Int128: QPC ticks x 10^7 overflow a long after about a day of uptime.
        long start100ns = (long)((Int128)_start * 10_000_000 / Stopwatch.Frequency);
        var lost = new bool[sources.Count];
        try
        {
            while (true)
            {
                bool last = _stopping;
                for (int i = 0; i < sources.Count; i++)
                {
                    if (lost[i])
                        continue;
                    var s = sources[i];
                    int index = i;
                    try
                    {
                        s.Drain((samples, qpc100ns) =>
                            mixer.Push(index, samples, s.Channels, s.SampleRate, (qpc100ns - start100ns) * AudioMixer.SampleRate / 10_000_000));
                    }
                    catch (Exception e) when (e is COMException or InvalidOperationException)
                    {
                        // Device unplugged or switched (AUDCLNT_E_DEVICE_INVALIDATED): keep recording without this source.
                        lost[i] = true;
                        s.Dispose();
                        lock (_warnings) _warnings.Add(Loc.Get("AudioDeviceLost"));
                    }
                }
                long until = last ? Math.Min(_stopAt, Stopwatch.GetTimestamp()) : Stopwatch.GetTimestamp() - AudioLagQpc;
                long target = (until - _start) * AudioMixer.SampleRate / Stopwatch.Frequency;
                int frames = (int)Math.Min(target - mixer.FramesProduced, AudioMixer.SampleRate);
                if (frames > 0)
                {
                    long at = mixer.FramesProduced;
                    var pcm = mixer.Pull(frames);
                    var bytes = new byte[pcm.Length * 2];
                    System.Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                    var sample = MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(),
                        TimeSpan.FromTicks(at * TimeSpan.TicksPerSecond / AudioMixer.SampleRate));
                    sample.Duration = TimeSpan.FromTicks((long)frames * TimeSpan.TicksPerSecond / AudioMixer.SampleRate);
                    _audio!.Add(sample);
                }
                if (last && frames <= 0)
                    break;
                if (!last)
                    Thread.Sleep(10);
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }
        finally
        {
            foreach (var s in sources)
            {
                s.Stop();
                s.Dispose();
            }
            _audio!.CompleteAdding();
            CoUninitialize();
        }
    }

    private TimeSpan Timestamp(long qpc) => TimeSpan.FromTicks((qpc - _start) * TimeSpan.TicksPerSecond / Stopwatch.Frequency);

    private void Fail(Exception e)
    {
        _failure ??= e;
        _stopping = true;
    }

    /// <summary>Ends the recording and finishes the MP4; throws when capture or encoding failed.</summary>
    public async Task StopAsync()
    {
        _stopAt = Stopwatch.GetTimestamp();
        _stopping = true;
        await Task.Run(() =>
        {
            _videoThread?.Join();
            _audioThread?.Join();
        });
        try
        {
            if (_transcode is not null)
                await _transcode;
        }
        finally
        {
            Release();
        }
        if (_failure is not null)
            throw new InvalidOperationException(Loc.Format("RecordingFailed", _failure.Message), _failure);
    }

    /// <summary>Stops without keeping the file.</summary>
    public async Task DiscardAsync()
    {
        try
        {
            await StopAsync();
        }
        catch (Exception) { }
        try
        {
            File.Delete(Path);
        }
        catch (IOException) { }
    }

    private void Release()
    {
        _session?.Dispose();
        _pool?.Dispose();
        _stream?.Dispose();
        _latest?.Dispose();
        _session = null;
        _item = null;
        _pool = null;
        _stream = null;
        _latest = null;
        while (_video.TryTake(out _)) { }
        _device.Dispose();
    }
}
