using Clipmark.App.Ai;
using Clipmark.App.Output;
using Clipmark.App.Shell;
using Clipmark.Core.Ai;
using Clipmark.Core.History;
using Clipmark.Core.Licensing;
using Clipmark.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel;

namespace Clipmark.App;

public sealed partial class SettingsPage : Page
{
    private static readonly int[] Keys =
    [
        0x2C, 0x13, 0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22,
        .. Enumerable.Range(0x70, 12), // F1-F12
        .. Enumerable.Range(0x41, 26), // A-Z
        .. Enumerable.Range(0x30, 10), // 0-9
    ];

    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        foreach (int key in Keys)
        {
            KeyBox.Items.Add(Hotkey.KeyName(key));
            RecKeyBox.Items.Add(Hotkey.KeyName(key));
        }
        foreach (var m in MainPage.Modes)
            ModeBox.Items.Add(m.Text);
        foreach (int d in ClipmarkSettings.DelayChoices)
            DelayBox.Items.Add(d == 0 ? "No delay" : $"{d} seconds");
        foreach (var model in AiModels.All)
            AiModelBox.Items.Add(model.Name);

        var s = AppSettings.Current;
        CtrlBox.IsChecked = s.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Control);
        AltBox.IsChecked = s.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt);
        ShiftBox.IsChecked = s.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift);
        WinBox.IsChecked = s.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Win);
        KeyBox.SelectedIndex = Math.Max(0, Array.IndexOf(Keys, s.Hotkey.Key));
        ModeBox.SelectedIndex = Array.FindIndex(MainPage.Modes, m => m.Mode == s.Mode);
        DelayBox.SelectedIndex = Array.IndexOf(ClipmarkSettings.DelayChoices, s.DelaySeconds);
        ClipboardSwitch.IsOn = s.CopyToClipboard;
        ToastSwitch.IsOn = s.ShowToast;
        AutoSaveSwitch.IsOn = s.AutoSave;
        ShowFolder(s);
        ShowHotkeyStatus();
        RecCtrlBox.IsChecked = s.RecordHotkey.Modifiers.HasFlag(HotkeyModifiers.Control);
        RecAltBox.IsChecked = s.RecordHotkey.Modifiers.HasFlag(HotkeyModifiers.Alt);
        RecShiftBox.IsChecked = s.RecordHotkey.Modifiers.HasFlag(HotkeyModifiers.Shift);
        RecWinBox.IsChecked = s.RecordHotkey.Modifiers.HasFlag(HotkeyModifiers.Win);
        RecKeyBox.SelectedIndex = Math.Max(0, Array.IndexOf(Keys, s.RecordHotkey.Key));
        SystemAudioSwitch.IsOn = s.RecordSystemAudio;
        MicrophoneSwitch.IsOn = s.RecordMicrophone;
        RecordingsFolderText.Text = "Recordings are saved to " + CaptureOutput.RecordingsFolder;
        ShowRecordHotkeyStatus();
        PrintScreenSwitch.IsOn = s.PrintScreenRestore is not null;
        if (CaptureOutput.IsPackaged())
        {
            // Packaged: Windows keeps HKCU writes in the package's private hive, so the switch cannot reach the Windows option.
            PrintScreenSwitch.Visibility = Visibility.Collapsed;
            PrintScreenSettingsLink.Visibility = Visibility.Visible;
            PrintScreenHint.Text = "To use Print Screen for Clipmark, turn off the Windows option that opens screen capture with "
                + "Print Screen (Bluetooth & devices > Keyboard; on older Windows 11: Accessibility > Keyboard), then choose "
                + "PrintScreen as the hotkey above.";
        }
        ShowStartup(SystemIntegration.StartupStateAsync);
        VersionText.Text = "Clipmark " + App.Version;
        AiModelBox.SelectedIndex = AiModels.All.ToList().IndexOf(AiModels.Find(s.AiModel));
        ShowAiKey();
        ShowLicense();
        _loading = false;
    }

    /// <summary>Parameter "ai" (from the Ask AI flyout) scrolls to the Ask AI section.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is "ai")
            AiSection.Loaded += (_, _) => AiSection.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
    }

    private void ShowLicense()
    {
        var license = AppLicense.Current;
        LicenseStatusText.Text = license.Status switch
        {
            LicenseStatus.Licensed => "Clipmark Premium is active.",
            LicenseStatus.Trial => $"Premium trial: {license.TrialDaysLeft} {(license.TrialDaysLeft == 1 ? "day" : "days")} left.",
            _ => "The Premium trial has ended. Premium tools are locked; everything else keeps working.",
        };
        var unlicensed = license.Status == LicenseStatus.Licensed ? Visibility.Collapsed : Visibility.Visible;
        LicenseEntry.Visibility = BuyButton.Visibility = unlicensed;
        ShowHistory();
        AiHint.Text = (license.IsEnabled(PremiumFeature.AiChat) ? "" : "Premium feature. ")
            + "Ask AI sends the capture as shown (annotations and redactions included) and your question to Anthropic "
            + $"({ClaudeChat.Host}) under your own API key, only when you send a question. Anthropic bills the key. "
            + "The key is kept in Windows Credential Manager on this PC; apps running under your Windows account can read it.";
    }

    private void ShowAiKey()
    {
        string? hint = AnthropicKeyStore.Hint();
        AiKeyStatus.Text = hint is null ? "No API key saved." : hint.Length == 0 ? "API key saved." : $"API key saved, ends in {hint}.";
        AiTestButton.IsEnabled = AiRemoveButton.IsEnabled = hint is not null;
    }

    private void OnAiSave(object sender, RoutedEventArgs e)
    {
        string key = AiKeyBox.Password.Trim();
        if (key.Length == 0)
        {
            AiResultText.Text = "Paste the key first.";
            return;
        }
        AiResultText.Text = AnthropicKeyStore.Save(key) ? "API key saved." : "The key could not be saved in Credential Manager.";
        AiKeyBox.Password = "";
        ShowAiKey();
    }

    private async void OnAiTest(object sender, RoutedEventArgs e)
    {
        if (AnthropicKeyStore.Read() is not { } key)
            return;
        AiTestButton.IsEnabled = false;
        AiResultText.Text = "Testing…";
        string? problem = await ClaudeChat.TestKeyAsync(key, CancellationToken.None);
        AiResultText.Text = problem ?? "Key works.";
        ShowAiKey();
    }

    private void OnAiRemove(object sender, RoutedEventArgs e)
    {
        AiResultText.Text = AnthropicKeyStore.Delete() ? "API key removed." : "The key could not be removed from Credential Manager.";
        ShowAiKey();
    }

    private void OnAiModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && AiModelBox.SelectedIndex >= 0)
            AppSettings.Update(s => s with { AiModel = AiModels.All[AiModelBox.SelectedIndex].Id });
    }

    /// <summary>Premium gates turning the history on; turning it off always works.</summary>
    private void ShowHistory()
    {
        bool licensed = AppLicense.Current.IsEnabled(PremiumFeature.History), on = AppSettings.Current.HistoryEnabled;
        bool wasLoading = _loading;
        _loading = true;
        HistorySwitch.IsOn = on;
        _loading = wasLoading;
        HistorySwitch.IsEnabled = licensed || on;
        HistoryHint.Text = (licensed ? "" : "Premium feature. ")
            + $"Keeps the last {HistoryPolicy.MaxItems} captures with their objects (up to {HistoryPolicy.MaxBytes / (1024 * 1024)} MB), "
            + "encrypted for your Windows account and stored only on this PC. Turning it off deletes the history.";
    }

    private async void OnHistoryToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        if (HistorySwitch.IsOn)
        {
            AppSettings.Update(s => s with { HistoryEnabled = true });
            return;
        }
        int count = (await CaptureHistory.ListAsync()).Count;
        if (count > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = count == 1 ? "Delete the capture in the history?" : $"Delete the {count} captures in the history?",
                PrimaryButtonText = "Turn off and delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                ShowHistory();
                return;
            }
        }
        AppSettings.Update(s => s with { HistoryEnabled = false });
        await CaptureHistory.ClearAsync();
        ShowHistory();
    }

    private void OnActivate(object sender, RoutedEventArgs e)
    {
        if (AppLicense.Activate(LicenseKeyBox.Text))
        {
            LicenseKeyBox.Text = "";
            LicenseResultText.Text = "License activated.";
        }
        else
            LicenseResultText.Text = "This license key is not valid. Check that you pasted all of it.";
        ShowLicense();
    }

    private async void OnBuy(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri(AppLicense.BuyUrl));

    private void ShowHotkey(Hotkey hotkey)
    {
        CtrlBox.IsChecked = hotkey.Modifiers.HasFlag(HotkeyModifiers.Control);
        AltBox.IsChecked = hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt);
        ShiftBox.IsChecked = hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift);
        WinBox.IsChecked = hotkey.Modifiers.HasFlag(HotkeyModifiers.Win);
        KeyBox.SelectedIndex = Math.Max(0, Array.IndexOf(Keys, hotkey.Key));
    }

    private void OnPrintScreenToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        try
        {
            if (PrintScreenSwitch.IsOn)
                SystemIntegration.TakeOverPrintScreen();
            else
                SystemIntegration.RestorePrintScreen();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            HotkeyStatus.Text = "The Windows setting could not be changed: " + ex.Message;
        }
        _loading = true;
        ShowHotkey(AppSettings.Current.Hotkey);
        PrintScreenSwitch.IsOn = AppSettings.Current.PrintScreenRestore is not null;
        _loading = false;
        ShowHotkeyStatus();
    }

    private async void OnOpenKeyboardSettings(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:devices-keyboard")); // has the option on build 26200

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        bool enabled = StartupSwitch.IsOn;
        ShowStartup(() => SystemIntegration.SetStartupAsync(enabled));
    }

    /// <summary>Runs the read or change and shows the resulting state; a failed change shows the state as it is.</summary>
    private async void ShowStartup(Func<Task<StartupTaskState>> run)
    {
        StartupTaskState state;
        try
        {
            state = await run();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException
                                       or System.Runtime.InteropServices.COMException)
        {
            try { state = await SystemIntegration.StartupStateAsync(); }
            catch (Exception) { state = StartupTaskState.Disabled; }
        }
        bool wasLoading = _loading; // the unpackaged read completes synchronously inside the page load
        _loading = true;
        StartupSwitch.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        _loading = wasLoading;
        StartupStatus.Text = state switch
        {
            StartupTaskState.DisabledByUser => "Clipmark is turned off in Task Manager > Startup apps. Turn it on there.",
            StartupTaskState.DisabledByPolicy => "Your organization does not allow Clipmark to start with Windows.",
            _ => "",
        };
        StartupStatus.Visibility = StartupStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static HotkeyModifiers ModifiersOf(CheckBox ctrl, CheckBox alt, CheckBox shift, CheckBox win)
    {
        var mods = HotkeyModifiers.None;
        if (ctrl.IsChecked == true) mods |= HotkeyModifiers.Control;
        if (alt.IsChecked == true) mods |= HotkeyModifiers.Alt;
        if (shift.IsChecked == true) mods |= HotkeyModifiers.Shift;
        if (win.IsChecked == true) mods |= HotkeyModifiers.Win;
        return mods;
    }

    private void ShowRecordHotkeyStatus()
    {
        var app = (App)Application.Current;
        RecordHotkeyStatus.Text = app.RecordHotkeyRegistered
            ? $"{AppSettings.Current.RecordHotkey} starts and stops a recording."
            : $"{AppSettings.Current.RecordHotkey} is used by another app. Choose a different combination.";
        RecordHotkeyStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            app.RecordHotkeyRegistered ? "TextFillColorSecondaryBrush" : "SystemFillColorCriticalBrush"];
    }

    private void OnRecordHotkeyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || RecKeyBox.SelectedIndex < 0)
            return;
        var hotkey = new Hotkey(ModifiersOf(RecCtrlBox, RecAltBox, RecShiftBox, RecWinBox), Keys[RecKeyBox.SelectedIndex]);
        AppSettings.Update(s => s with { RecordHotkey = hotkey });
        ShowRecordHotkeyStatus();
    }

    private void OnRecordTogglesChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            AppSettings.Update(s => s with { RecordSystemAudio = SystemAudioSwitch.IsOn, RecordMicrophone = MicrophoneSwitch.IsOn });
    }

    private void ShowFolder(ClipmarkSettings s)
    {
        FolderText.Text = s.SaveFolder ?? CaptureOutput.DefaultFolder;
        ResetFolderButton.IsEnabled = s.SaveFolder is not null;
    }

    private void ShowHotkeyStatus()
    {
        var app = (App)Application.Current;
        HotkeyStatus.Text = app.HotkeyRegistered
            ? $"{AppSettings.Current.Hotkey} starts a capture."
            : $"{AppSettings.Current.Hotkey} is used by another app. Choose a different combination.";
        HotkeyStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            app.HotkeyRegistered ? "TextFillColorSecondaryBrush" : "SystemFillColorCriticalBrush"];
    }

    private void OnHotkeyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || KeyBox.SelectedIndex < 0)
            return;
        var mods = ModifiersOf(CtrlBox, AltBox, ShiftBox, WinBox);
        AppSettings.Update(s => s with { Hotkey = new Hotkey(mods, Keys[KeyBox.SelectedIndex]) });
        ShowHotkeyStatus();
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ModeBox.SelectedIndex >= 0)
            AppSettings.Update(s => s with { Mode = MainPage.Modes[ModeBox.SelectedIndex].Mode });
    }

    private void OnDelayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && DelayBox.SelectedIndex >= 0)
            AppSettings.Update(s => s with { DelaySeconds = ClipmarkSettings.DelayChoices[DelayBox.SelectedIndex] });
    }

    private void OnTogglesChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            AppSettings.Update(s => s with
            {
                CopyToClipboard = ClipboardSwitch.IsOn, ShowToast = ToastSwitch.IsOn, AutoSave = AutoSaveSwitch.IsOn,
            });
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        var result = await picker.PickSingleFolderAsync();
        if (result is null)
            return;
        AppSettings.Update(s => s with { SaveFolder = result.Path });
        ShowFolder(AppSettings.Current);
    }

    private void OnResetFolder(object sender, RoutedEventArgs e)
    {
        AppSettings.Update(s => s with { SaveFolder = null });
        ShowFolder(AppSettings.Current);
    }

    private void OnBack(object sender, RoutedEventArgs e) => Frame.GoBack();
}
