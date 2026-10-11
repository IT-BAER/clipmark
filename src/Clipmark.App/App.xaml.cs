using Clipmark.App.Capture;
using Clipmark.App.Output;
using Clipmark.App.Shell;
using Clipmark.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace Clipmark.App;

/// <summary>
/// Single-instance tray app. Startup flags: `--tray` starts hidden; `--measure-startup FILE` writes the
/// first-frame time (UTC ticks) and exits (used by scripts\measure-m0.ps1).
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;
    private TrayHost? _tray;
    private CaptureController? _controller;
    private DispatcherQueue? _dispatcher;

    internal bool HotkeyRegistered { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    private static string? Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string? marker = Arg("--measure-startup");
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (marker is null)
        {
            var main = AppInstance.FindOrRegisterForKey("Clipmark");
            if (!main.IsCurrent)
            {
                await main.RedirectActivationToAsync(activation);
                Exit();
                return;
            }
            main.Activated += (_, e) => _dispatcher?.TryEnqueue(() => OnRedirected(e));
        }
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        AppNotificationManager.Default.NotificationInvoked += (_, e) =>
            _dispatcher.TryEnqueue(() => OpenCapture(OpenArgument(e)));
        CaptureOutput.RegisterNotifications();
        CaptureOutput.CleanTempFolder();

        _window = new MainWindow();
        _tray = new TrayHost { Settings = () => AppSettings.Current };
        _tray.CaptureRequested += () => _ = CaptureAsync();
        _tray.OpenRequested += () => _window.ShowAndActivate();
        _tray.SettingsRequested += () => { _window.ShowSettings(); _window.ShowAndActivate(); };
        _tray.ExitRequested += ExitApp;
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
        else if (!Environment.GetCommandLineArgs().Contains("--tray"))
            _window.ShowAndActivate();
    }

    private Hotkey? _registeredHotkey;

    private void RegisterHotkey(ClipmarkSettings s)
    {
        if (_tray is null || s.Hotkey == _registeredHotkey)
            return;
        HotkeyRegistered = _tray.TryRegisterHotkey(s.Hotkey);
        _registeredHotkey = s.Hotkey;
        if (!HotkeyRegistered && _window is not null)
            _window.ShowError($"{s.Hotkey} is used by another app. Choose a different hotkey in Settings.");
    }

    /// <summary>Second launch (Start menu, toast click while running) lands here.</summary>
    private void OnRedirected(AppActivationArguments e)
    {
        if (e.Kind == ExtendedActivationKind.AppNotification)
            OpenCapture(OpenArgument((AppNotificationActivatedEventArgs)e.Data));
        else
            _window?.ShowAndActivate();
    }

    private static string? OpenArgument(AppNotificationActivatedEventArgs e) =>
        e.Arguments.TryGetValue("open", out var path) ? path : null;

    private void OpenCapture(string? path)
    {
        if (_window is null)
            return;
        if (path is not null && File.Exists(path))
            _window.ShowImage(path);
        _window.ShowAndActivate();
    }

    /// <summary>Starts a capture; the main window is cloaked while the frame is taken and shows the result if it was open.</summary>
    internal async Task CaptureAsync()
    {
        if (_controller is null || _window is null)
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
        void OnCaptured(string path) => captured = path;
        _controller.Captured += OnCaptured;
        try
        {
            await _controller.RunAsync();
        }
        finally
        {
            _controller.Captured -= OnCaptured;
            if (windowWasShown)
            {
                if (captured is not null)
                    _window.ShowImage(captured);
                _window.ShowAndActivate();
            }
        }
    }

    private void ExitApp()
    {
        _tray?.Dispose();
        _tray = null;
        Exit();
    }
}
