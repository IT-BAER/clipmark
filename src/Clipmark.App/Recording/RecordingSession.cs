using System.Diagnostics;
using Clipmark.App.Output;
using Clipmark.Core.Capture;
using Clipmark.Core.Recording;
using Microsoft.UI.Dispatching;

namespace Clipmark.App.Recording;

/// <summary>Outcome of a recording session: the saved MP4 (null when cancelled or failed), an error, audio warnings.</summary>
internal sealed record RecordingResult(string? Path, string? Error, IReadOnlyList<string> Warnings);

/// <summary>
/// One recording on the UI thread: bar and outline around the picked region, Record runs a 3 s countdown, then the
/// recorder writes to Videos\Screen Recordings until Stop (bar, recording hotkey or tray click) or Discard.
/// </summary>
internal sealed class RecordingSession
{
    private const int CountdownSeconds = 3;

    private readonly RecordingArea _area;
    private readonly RecordBar _bar;
    private readonly RegionOutline _outline;
    private readonly DispatcherQueueTimer _timer;
    private readonly TaskCompletionSource<RecordingResult> _done = new();
    private ScreenRecorder? _recorder;
    private long _countdownStart;

    /// <summary>The running session, if any (hotkey and tray route to it).</summary>
    public static RecordingSession? Current { get; private set; }

    private RecordingSession(RecordingArea area)
    {
        _area = area;
        var s = AppSettings.Current;
        _bar = new RecordBar(area, s.RecordMicrophone, s.RecordSystemAudio);
        _bar.Clicked += OnClicked;
        _outline = new RegionOutline(area.Region);
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(200);
        _timer.Tick += (_, _) => OnTick();
    }

    public static async Task<RecordingResult> RunAsync(RecordingArea area)
    {
        var session = new RecordingSession(area);
        Current = session;
        try
        {
            session._bar.Show();
            return await session._done.Task;
        }
        finally
        {
            Current = null;
            session._timer.Stop();
            session._outline.Dispose();
            session._bar.Dispose();
        }
    }

    /// <summary>Record / Stop: starts the countdown, cancels it, or stops a running recording.</summary>
    public void Toggle() => OnClicked(RecordBarButton.Record);

    private void OnClicked(RecordBarButton button)
    {
        switch (button, _bar.State)
        {
            case (RecordBarButton.Record, RecordBarState.Idle):
                _countdownStart = Stopwatch.GetTimestamp();
                _bar.State = RecordBarState.Countdown;
                OnTick();
                _timer.Start();
                break;
            case (RecordBarButton.Record, RecordBarState.Countdown):
                _timer.Stop();
                _bar.Timer = "00:00";
                _bar.State = RecordBarState.Idle;
                break;
            case (RecordBarButton.Record, RecordBarState.Recording):
                _ = StopAsync(keep: true);
                break;
            case (RecordBarButton.Pause, RecordBarState.Recording):
                if (_recorder!.IsPaused)
                    _recorder.Resume();
                else
                    _recorder.Pause();
                _bar.Paused = _recorder.IsPaused;
                break;
            case (RecordBarButton.Microphone, RecordBarState.Recording):
                // Mutes the source in this recording only; the saved setting stays.
                bool muteMic = _bar.Microphone;
                _bar.MicrophoneLive = _recorder!.SetMuted(loopback: false, muteMic);
                _bar.Microphone = _bar.MicrophoneLive && !muteMic;
                break;
            case (RecordBarButton.SystemAudio, RecordBarState.Recording):
                bool muteSystem = _bar.SystemAudio;
                _bar.SystemAudioLive = _recorder!.SetMuted(loopback: true, muteSystem);
                _bar.SystemAudio = _bar.SystemAudioLive && !muteSystem;
                break;
            case (RecordBarButton.Discard, RecordBarState.Recording):
                _ = StopAsync(keep: false);
                break;
            case (RecordBarButton.Discard, RecordBarState.Idle or RecordBarState.Countdown):
                _done.TrySetResult(new RecordingResult(null, null, []));
                break;
            case (RecordBarButton.Microphone, RecordBarState.Idle or RecordBarState.Countdown):
                AppSettings.Update(s => s with { RecordMicrophone = !s.RecordMicrophone });
                _bar.Microphone = AppSettings.Current.RecordMicrophone;
                break;
            case (RecordBarButton.SystemAudio, RecordBarState.Idle or RecordBarState.Countdown):
                AppSettings.Update(s => s with { RecordSystemAudio = !s.RecordSystemAudio });
                _bar.SystemAudio = AppSettings.Current.RecordSystemAudio;
                break;
        }
    }

    private void OnTick()
    {
        if (_bar.State == RecordBarState.Countdown)
        {
            int left = CountdownSeconds - (int)Stopwatch.GetElapsedTime(_countdownStart).TotalSeconds;
            if (left > 0)
                _bar.Timer = left.ToString(System.Globalization.CultureInfo.CurrentCulture);
            else
                _ = StartAsync();
        }
        else if (_bar.State == RecordBarState.Recording && _recorder is not null)
        {
            _bar.Timer = RecordingTime.Format(_recorder.Elapsed);
            // A device lost mid-recording: its button shows off and cannot be unmuted.
            if (_bar.MicrophoneLive && !_recorder.IsLive(loopback: false))
                _bar.Microphone = _bar.MicrophoneLive = false;
            if (_bar.SystemAudioLive && !_recorder.IsLive(loopback: true))
                _bar.SystemAudio = _bar.SystemAudioLive = false;
            if (_recorder.Faulted)
                _ = StopAsync(keep: true); // StopAsync reports the failure
        }
    }

    private async Task StartAsync()
    {
        _bar.State = RecordBarState.Starting;
        _bar.Timer = "00:00";
        string folder = CaptureOutput.RecordingsFolder;
        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, CaptureFileName.Next(DateTime.Now, name => File.Exists(Path.Combine(folder, name)), ".mp4"));
            var s = AppSettings.Current;
            _recorder = await ScreenRecorder.StartAsync(_area, path, s.RecordSystemAudio, s.RecordMicrophone);
            // A source that is off or could not be opened shows off and cannot be unmuted.
            _bar.Microphone = _bar.MicrophoneLive = _recorder.SetMuted(loopback: false, false);
            _bar.SystemAudio = _bar.SystemAudioLive = _recorder.SetMuted(loopback: true, false);
            _bar.State = RecordBarState.Recording;
        }
        catch (Exception e)
        {
            _done.TrySetResult(new RecordingResult(null, Loc.Format("RecordingNotStarted", e.Message), []));
        }
    }

    private async Task StopAsync(bool keep)
    {
        var recorder = _recorder!;
        _timer.Stop();
        _bar.State = RecordBarState.Saving;
        if (!keep)
        {
            await recorder.DiscardAsync();
            _done.TrySetResult(new RecordingResult(null, null, recorder.Warnings));
            return;
        }
        try
        {
            await recorder.StopAsync();
            _done.TrySetResult(new RecordingResult(recorder.Path, null, recorder.Warnings));
        }
        catch (Exception e)
        {
            _done.TrySetResult(new RecordingResult(File.Exists(recorder.Path) ? recorder.Path : null, e.Message, recorder.Warnings));
        }
    }
}
