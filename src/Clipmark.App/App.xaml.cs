using Clipmark.App.Capture;
using Clipmark.App.Output;
using Clipmark.App.Recording;
using Clipmark.App.Shell;
using Clipmark.Core.Recording;
using Clipmark.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace Clipmark.App;

/// <summary>
/// Single-instance tray app. Startup flags: `--tray` (or the packaged StartupTask) starts hidden; `--open FILE` shows an image or MP4;
/// `--measure-startup FILE` writes the first-frame time (UTC ticks) and exits (used by scripts\measure-m0.ps1);
/// `--record-test FILE` records a short test MP4 and exits; `--restore-system` undoes the Print Screen and autostart changes and deletes the Ask AI key;
/// `--trial-status FILE` (Debug) re-checks the licence, makes the due server call, writes the state and exits.
/// </summary>
public partial class App : Application
{
#if DEBUG
    /// <summary>Test mode (Debug only): the window stays cloaked and TestDriver takes commands from this folder.</summary>
    internal static readonly string? TestDir = Environment.GetEnvironmentVariable("CLIPMARK_TEST_DIR");
    // Debug builds run next to an installed release: own instance key and data folder, and they leave its Start menu shortcut alone.
    private const string InstanceKey = "Clipmark.Debug", DataName = "Clipmark.Debug";
    internal const bool OwnsShortcut = false;
#else
    internal static readonly string? TestDir = null;
    private const string InstanceKey = "Clipmark", DataName = "Clipmark";
    internal const bool OwnsShortcut = true;
#endif
    /// <summary>Settings, license and capture history folder.</summary>
    internal static readonly string DataDir =
        TestDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataName);

    private MainWindow? _window;
    private TrayHost? _tray;
    private CaptureController? _controller;
    private DispatcherQueue? _dispatcher;

    /// <summary>Counts the trial on while the app stays open, so an expiry locks Premium without a new capture.</summary>
    private DispatcherQueueTimer? _licenseTimer;

    internal bool HotkeyRegistered { get; private set; }

    /// <summary>Product version (Directory.Build.props), e.g. "1.0.0".</summary>
    internal static string Version { get; } = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public App()
    {
        InitializeComponent();
#if DEBUG
        // Screenshot checks in the other theme without changing the Windows setting.
        if (Enum.TryParse<ApplicationTheme>(Environment.GetEnvironmentVariable("CLIPMARK_TEST_THEME"), out var theme))
            RequestedTheme = theme;
#endif
    }

    private static string? Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Arg("--record-test") is { } recordTest)
        {
            await RecordTestAsync(recordTest);
            Exit();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--restore-system"))
        {
            SystemIntegration.RestoreAll();
            Exit();
            return;
        }
#if DEBUG
        if (Arg("--trial-status") is { } trialStatus)
        {
            AppLicense.Refresh();
            await AppLicense.SyncAsync();
            File.WriteAllText(Path.GetFullPath(trialStatus), AppLicense.Describe());
            Exit();
            return;
        }
#endif
        // Before any window or Loc use; test mode pins a language so TestDriver's English names work on any Windows.
        string? language = TestDir is null ? AppSettings.Current.Language
            : Environment.GetEnvironmentVariable("CLIPMARK_TEST_LANG") is { Length: > 0 } testLang ? testLang : "en-US";
        // MRT Core rejects "" (E_INVALIDARG); the override is per process, so no override = the Windows display language.
        if (language is not null)
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        Clipmark.Core.CoreText.Resolve = Loc.TryGet;
        string? marker = Arg("--measure-startup");
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        // Packaged: Register must run before GetActivatedEventArgs, or a toast click that starts the app is lost.
        AppNotificationManager.Default.NotificationInvoked += (_, e) =>
            _dispatcher.TryEnqueue(() => OpenCapture(OpenArgument(e)));
        CaptureOutput.ToastClicked += path => _dispatcher.TryEnqueue(() => OpenCapture(path));
        CaptureOutput.RegisterNotifications(writeShortcut: OwnsShortcut && marker is null);

        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (marker is null)
        {
            var main = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (!main.IsCurrent)
            {
                await main.RedirectActivationToAsync(activation);
                Exit();
                return;
            }
            main.Activated += (_, e) => _dispatcher?.TryEnqueue(() => OnRedirected(e));
        }
        CaptureOutput.CleanTempFolder();

        // Offline check now; a due server call is queued at low priority, so it starts after the window exists, off the UI thread.
        AppLicense.Refresh();
        _licenseTimer = _dispatcher.CreateTimer();
        _licenseTimer.Interval = TimeSpan.FromHours(1);
        _licenseTimer.Tick += (_, _) => AppLicense.Refresh();
        _licenseTimer.Start();
        _window = new MainWindow();
        _tray = new TrayHost { Settings = () => AppSettings.Current };
        if (TestDir is null && marker is null)
            TrayPromotion.Start();
        _tray.CaptureRequested += () => _ = CaptureAsync();
        _tray.RecordRequested += OnRecordRequested;
        _tray.IconClicked += () =>
        {
            if (RecordingSession.Current is { } recording)
                recording.Toggle();
            else
                _ = CaptureAsync();
        };
        _tray.OpenRequested += () => _window.ShowAndActivate();
        _tray.SettingsRequested += () => { _window.ShowSettings(); _window.ShowAndActivate(); };
        _tray.ExitRequested += () => _ = ExitAsync();
        _tray.ModeChosen += mode => AppSettings.Update(s => s with { Mode = mode });
        _tray.DelayChosen += delay => AppSettings.Update(s => s with { DelaySeconds = delay });

        _controller = new CaptureController(_tray.Handle);
        _controller.Failed += text => { _window.ShowError(text); _window.ShowAndActivate(); };

        RegisterHotkey(AppSettings.Current);
        AppSettings.Changed += RegisterHotkey;

        if (marker is not null)
        {
            void OnFirstFrame(object? sender, object e)
            {
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFirstFrame;
                File.WriteAllText(marker, DateTime.UtcNow.Ticks.ToString());
                ExitApp();
            }
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFirstFrame;
        }

        if (activation.Kind == ExtendedActivationKind.AppNotification)
            OpenCapture(OpenArgument((AppNotificationActivatedEventArgs)activation.Data));
        else if (Arg("--open") is { } file)
            OpenCapture(Path.GetFullPath(file));
        else if (!Environment.GetCommandLineArgs().Contains("--tray") && activation.Kind != ExtendedActivationKind.StartupTask)
            _window.ShowAndActivate();
    }

    /// <summary>
    /// Diagnostic for published builds (scripts check the recorder there): records 640x360 at the primary monitor's
    /// (100, 100) for 2 s with system audio to FILE and writes "ok" or the error to FILE.txt.
    /// </summary>
    private static async Task RecordTestAsync(string file)
    {
        string result = "ok";
        try
        {
            var area = RecordingRegion.Fit(new Clipmark.Core.Capture.PixelRect(100, 100, 640, 360), ScreenCapture.EnumerateMonitors())
                ?? throw new InvalidOperationException("no monitor at (100, 100)");
            var recorder = await ScreenRecorder.StartAsync(area, Path.GetFullPath(file), systemAudio: true, microphone: false);
            await Task.Delay(2000);
            await recorder.StopAsync();
            if (recorder.Warnings.Count > 0)
                result = "ok, warnings: " + string.Join(" ", recorder.Warnings);
        }
        catch (Exception e)
        {
            result = "error: " + e;
        }
        File.WriteAllText(Path.GetFullPath(file) + ".txt", result);
    }

    private Hotkey? _registeredHotkey, _registeredRecordHotkey;

    internal bool RecordHotkeyRegistered { get; private set; }

    private void RegisterHotkey(ClipmarkSettings s)
    {
        // Test mode takes no global hotkeys: an installed copy holds them, and the conflict bar would shift every test shot.
        if (_tray is null || TestDir is not null)
            return;
        if (s.Hotkey != _registeredHotkey)
        {
            HotkeyRegistered = _tray.TryRegisterHotkey(TrayHost.CaptureHotkeyId, s.Hotkey);
            _registeredHotkey = s.Hotkey;
            if (!HotkeyRegistered && _window is not null)
                _window.ShowError(Loc.Format("HotkeyTakenError", s.Hotkey));
        }
        if (s.RecordHotkey != _registeredRecordHotkey)
        {
            RecordHotkeyRegistered = _tray.TryRegisterHotkey(TrayHost.RecordHotkeyId, s.RecordHotkey);
            _registeredRecordHotkey = s.RecordHotkey;
            if (!RecordHotkeyRegistered && _window is not null)
                _window.ShowError(Loc.Format("RecordHotkeyTakenError", s.RecordHotkey));
        }
    }

    /// <summary>Recording hotkey or tray item: stops a running recording, else opens the overlay in Video mode.</summary>
    private void OnRecordRequested()
    {
        if (RecordingSession.Current is { } recording)
            recording.Toggle();
        else
            _ = CaptureAsync(video: true);
    }

    /// <summary>Second launch (Start menu, toast click while running) lands here.</summary>
    private void OnRedirected(AppActivationArguments e)
    {
        if (e.Kind == ExtendedActivationKind.AppNotification)
            OpenCapture(OpenArgument((AppNotificationActivatedEventArgs)e.Data));
        else
            _window?.ShowAndActivate();
    }

    internal void CloseWindow() => _window?.CloseToTray();

    private static string? OpenArgument(AppNotificationActivatedEventArgs e) =>
        e.Arguments.TryGetValue("open", out var path) ? path : null;

    private async void OpenCapture(string? path)
    {
        if (_window is null)
            return;
        if (path is not null && File.Exists(path) && await _window.ConfirmDiscardAsync())
        {
            if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                _window.ShowVideo(path);
            else
                _window.ShowImage(path);
        }
        _window.ShowAndActivate();
    }

    /// <summary>
    /// Starts a capture (video = overlay in Video mode); the main window is cloaked while the frame is taken and shows the
    /// result if it was open or no toast is shown. A video pick records first and then always shows the recording.
    /// </summary>
    internal async Task CaptureAsync(bool video = false)
    {
        if (RecordingSession.Current is not null || _controller is null || _window is null || !await _window.ConfirmDiscardAsync())
            return;
        bool windowWasShown = _window.IsShown;
        _controller.BeforeFreeze = async () =>
        {
            if (windowWasShown)
            {
                _window.SetCloaked(true);
                await Task.Delay(60); // let DWM compose a frame without the window
            }
        };
        string? captured = null;
        RecordingArea? record = null;
        bool failed = false;
        void OnCaptured(string path) => captured = path;
        void OnRecord(RecordingArea area) => record = area;
        void OnFailed(string _) => failed = true;
        _controller.Captured += OnCaptured;
        _controller.RecordRequested += OnRecord;
        _controller.Failed += OnFailed;
        try
        {
            await _controller.RunAsync(video: video);
            if (record is { } area)
            {
                await RecordAsync(area);
                return;
            }
        }
        finally
        {
            _controller.Captured -= OnCaptured;
            _controller.RecordRequested -= OnRecord;
            _controller.Failed -= OnFailed;
            // A failure (clipboard busy, save failed) keeps the window: it shows the error.
            bool closeAfterCopy = captured is not null && !failed && AppSettings.Current is { CopyToClipboard: true, CloseAfterCopy: true };
            // Without a toast the editor is the only way to reach the capture.
            bool showEditor = captured is not null && !closeAfterCopy && (windowWasShown || !AppSettings.Current.ShowToast);
            if (closeAfterCopy && windowWasShown)
                _window.CloseToTray();
            else
            {
                if (showEditor)
                    _window.ShowImage(captured!);
                if (showEditor || windowWasShown)
                    _window.ShowAndActivate();
            }
        }
    }

    /// <summary>Runs the record bar for the area; the window stays cloaked meanwhile and then shows the recording or the error.</summary>
    private async Task RecordAsync(RecordingArea area)
    {
        var result = await RecordingSession.RunAsync(area);
        if (_window is null)
            return;
        var problems = result.Warnings.ToList();
        if (result.Error is not null)
            problems.Insert(0, result.Error);
        if (result.Path is not null)
        {
            _window.ShowVideo(result.Path);
            if (AppSettings.Current.ShowToast)
                CaptureOutput.ShowRecordingToast(result.Path);
        }
        if (problems.Count > 0)
            _window.ShowError(string.Join(" ", problems));
        if (result.Path is not null || problems.Count > 0)
            _window.ShowAndActivate();
    }

    internal async Task ExitAsync()
    {
        if (RecordingSession.Current is not null)
        {
            _window?.ShowError(Loc.Get("StopRecordingBeforeExit"));
            _window?.ShowAndActivate();
            return;
        }
        if (_window is not null && !await _window.ConfirmDiscardAsync())
            return;
        try
        {
            if (_window is not null)
                await _window.FlushHistoryAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception) { } // a failed history write must not keep the app from exiting
        ExitApp();
    }

    private void ExitApp()
    {
        _tray?.Dispose();
        _tray = null;
        Exit();
    }
}
