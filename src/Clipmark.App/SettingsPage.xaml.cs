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
        // Code, not a style: setters cannot use x:Uid, and the WinUI default follows the Windows display language.
        foreach (var toggle in new[] { PrintScreenSwitch, StartupSwitch, ClipboardSwitch, ToastSwitch, AutoSaveSwitch, OcrLineBreaksSwitch, CopyEditsSwitch, BorderSwitch, HistorySwitch,
                     SystemAudioSwitch, MicrophoneSwitch })
            (toggle.OnContent, toggle.OffContent) = (Loc.Get("ToggleOn"), Loc.Get("ToggleOff"));
        foreach (int key in Keys)
        {
            KeyBox.Items.Add(Hotkey.KeyName(key));
            RecKeyBox.Items.Add(Hotkey.KeyName(key));
        }
        foreach (var m in MainPage.Modes)
            ModeBox.Items.Add(m.Text);
        foreach (int d in ClipmarkSettings.DelayChoices)
            DelayBox.Items.Add(MainPage.DelayText(d));
        foreach (var model in AiModels.All)
            AiModelBox.Items.Add(model.Name);
        foreach (string effort in AiEfforts.All)
            AiEffortBox.Items.Add(MainPage.EffortText(effort));
        foreach (string theme in new[] { "ThemeSystem", "ThemeLight", "ThemeDark" })
            ThemeBox.Items.Add(Loc.Get(theme));
        foreach (string format in new[] { "HEX", "RGB", "HSL" })
            ColorFormatBox.Items.Add(format);
        LanguageBox.Items.Add(Loc.Get("LanguageWindowsDefault"));
        foreach (var language in UiLanguages.All)
            LanguageBox.Items.Add(language.NativeName);

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
        OcrLineBreaksSwitch.IsOn = s.OcrRemoveLineBreaks;
        ColorFormatBox.SelectedIndex = (int)s.ColorFormat;
        CopyEditsSwitch.IsOn = s.CopyEdits;
        BorderSwitch.IsOn = s.Border;
        BorderOptions.Visibility = s.Border ? Visibility.Visible : Visibility.Collapsed;
        BorderPicker.Color = Windows.UI.Color.FromArgb(255, s.BorderColor.R, s.BorderColor.G, s.BorderColor.B);
        BorderSwatch.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(BorderPicker.Color);
        BorderWidthBox.Value = s.BorderWidth;
        ThemeBox.SelectedIndex = Math.Max(0, Array.IndexOf(ClipmarkSettings.Themes, s.Theme));
        ShowFolder(s);
        ShowHotkeyStatus();
        RecCtrlBox.IsChecked = s.RecordHotkey.Modifiers.HasFlag(HotkeyModifiers.Control);
        RecAltBox.IsChecked = s.RecordHotkey.Modifiers.HasFlag(HotkeyModifiers.Alt);
        RecShiftBox.IsChecked = s.RecordHotkey.Modifiers.HasFlag(HotkeyModifiers.Shift);
        RecWinBox.IsChecked = s.RecordHotkey.Modifiers.HasFlag(HotkeyModifiers.Win);
        RecKeyBox.SelectedIndex = Math.Max(0, Array.IndexOf(Keys, s.RecordHotkey.Key));
        SystemAudioSwitch.IsOn = s.RecordSystemAudio;
        MicrophoneSwitch.IsOn = s.RecordMicrophone;
        RecordingsFolderText.Text = Loc.Format("RecordingsFolder", CaptureOutput.RecordingsFolder);
        ShowRecordHotkeyStatus();
        PrintScreenSwitch.IsOn = s.PrintScreenRestore is not null;
        if (CaptureOutput.IsPackaged())
        {
            // Packaged: Windows keeps HKCU writes in the package's private hive, so the switch cannot reach the Windows option.
            PrintScreenSwitch.Visibility = Visibility.Collapsed;
            PrintScreenSettingsLink.Visibility = Visibility.Visible;
            PrintScreenHint.Text = Loc.Get("PrintScreenHintPackaged");
        }
        ShowStartup(SystemIntegration.StartupStateAsync);
        VersionText.Text = Loc.Format("VersionText", App.Version);
        AiModelBox.SelectedIndex = AiModels.All.ToList().IndexOf(AiModels.Find(s.AiModel));
        AiEffortBox.SelectedIndex = AiEfforts.All.ToList().IndexOf(s.AiEffort);
        LanguageBox.SelectedIndex = 1 + UiLanguages.All.ToList().FindIndex(l => l.Tag == s.Language); // -1 + 1 = Windows default
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
            LicenseStatus.Licensed => Loc.Get("LicenseActive"),
            LicenseStatus.Trial => license.TrialDaysLeft == 1 ? Loc.Get("LicenseTrialOneDay") : Loc.Format("LicenseTrialDays", license.TrialDaysLeft),
            _ => Loc.Get("LicenseTrialEnded"),
        };
        var unlicensed = license.Status == LicenseStatus.Licensed ? Visibility.Collapsed : Visibility.Visible;
        LicenseEntry.Visibility = BuyButton.Visibility = unlicensed;
        ShowHistory();
        AiHint.Text = Loc.Format(license.IsEnabled(PremiumFeature.AiChat) ? "AiHint" : "AiHintPremium", ClaudeChat.Host);
    }

    private void ShowAiKey()
    {
        string? hint = AnthropicKeyStore.Hint();
        AiKeyStatus.Text = hint is null ? Loc.Get("AiKeyNone") : hint.Length == 0 ? Loc.Get("AiKeySaved") : Loc.Format("AiKeySavedEnds", hint);
        AiTestButton.IsEnabled = AiRemoveButton.IsEnabled = hint is not null;
    }

    private void OnAiSave(object sender, RoutedEventArgs e)
    {
        string key = AiKeyBox.Password.Trim();
        if (key.Length == 0)
        {
            AiResultText.Text = Loc.Get("AiKeyPasteFirst");
            return;
        }
        AiResultText.Text = Loc.Get(AnthropicKeyStore.Save(key) ? "AiKeySaved" : "AiKeySaveFailed");
        AiKeyBox.Password = "";
        ShowAiKey();
    }

    private async void OnAiTest(object sender, RoutedEventArgs e)
    {
        if (AnthropicKeyStore.Read() is not { } key)
            return;
        AiTestButton.IsEnabled = false;
        AiResultText.Text = Loc.Get("AiKeyTesting");
        string? problem = await ClaudeChat.TestKeyAsync(key, CancellationToken.None);
        AiResultText.Text = problem ?? Loc.Get("AiKeyWorks");
        ShowAiKey();
    }

    private void OnAiRemove(object sender, RoutedEventArgs e)
    {
        AiResultText.Text = Loc.Get(AnthropicKeyStore.Delete() ? "AiKeyRemoved" : "AiKeyRemoveFailed");
        ShowAiKey();
    }

    private void OnAiModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && AiModelBox.SelectedIndex >= 0)
            AppSettings.Update(s => s with { AiModel = AiModels.All[AiModelBox.SelectedIndex].Id });
    }

    private void OnAiEffortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && AiEffortBox.SelectedIndex >= 0)
            AppSettings.Update(s => s with { AiEffort = AiEfforts.All[AiEffortBox.SelectedIndex] });
    }

    /// <summary>Takes effect at the next start: App applies the language override once, before the first window.</summary>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedIndex < 0)
            return;
        string? language = LanguageBox.SelectedIndex == 0 ? null : UiLanguages.All[LanguageBox.SelectedIndex - 1].Tag;
        AppSettings.Update(s => s with { Language = language });
        LanguageNote.Visibility = Visibility.Visible;
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
        HistoryHint.Text = Loc.Format(licensed ? "HistoryHint" : "HistoryHintPremium", HistoryPolicy.MaxItems, HistoryPolicy.MaxBytes / (1024 * 1024));
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
                RequestedTheme = ActualTheme,
                Title = count == 1 ? Loc.Get("HistoryOffTitleOne") : Loc.Format("HistoryOffTitle", count),
                PrimaryButtonText = Loc.Get("HistoryOffConfirm"),
                CloseButtonText = Loc.Get("Cancel"),
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
            LicenseResultText.Text = Loc.Get("LicenseActivated");
        }
        else
            LicenseResultText.Text = Loc.Get("LicenseInvalid");
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
            HotkeyStatus.Text = Loc.Format("PrintScreenChangeFailed", ex.Message);
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
            StartupTaskState.DisabledByUser => Loc.Get("StartupDisabledByUser"),
            StartupTaskState.DisabledByPolicy => Loc.Get("StartupDisabledByPolicy"),
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
            ? Loc.Format("RecordHotkeyOk", AppSettings.Current.RecordHotkey)
            : Loc.Format("HotkeyTaken", AppSettings.Current.RecordHotkey);
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
            ? Loc.Format("HotkeyOk", AppSettings.Current.Hotkey)
            : Loc.Format("HotkeyTaken", AppSettings.Current.Hotkey);
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
                OcrRemoveLineBreaks = OcrLineBreaksSwitch.IsOn, CopyEdits = CopyEditsSwitch.IsOn, Border = BorderSwitch.IsOn,
            });
        BorderOptions.Visibility = BorderSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBorderColorChanged(ColorPicker sender, ColorChangedEventArgs e)
    {
        BorderSwatch.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(e.NewColor);
        if (!_loading)
            AppSettings.Update(s => s with { BorderColor = new Clipmark.Core.Editor.Rgba(e.NewColor.R, e.NewColor.G, e.NewColor.B, 255) });
    }

    private void OnBorderWidthChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (double.IsNaN(sender.Value))
            sender.Value = AppSettings.Current.BorderWidth; // cleared box: back to the stored width
        else if (!_loading)
            AppSettings.Update(s => s with { BorderWidth = (int)sender.Value });
    }

    private void OnColorFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ColorFormatBox.SelectedIndex >= 0)
            AppSettings.Update(s => s with { ColorFormat = (ColorFormat)ColorFormatBox.SelectedIndex });
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeBox.SelectedIndex >= 0)
            AppSettings.Update(s => s with { Theme = ClipmarkSettings.Themes[ThemeBox.SelectedIndex] });
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

    private void OnOpenFolder(object sender, RoutedEventArgs e) => CaptureOutput.OpenFolder(FolderText.Text);

    private void OnOpenRecordingsFolder(object sender, RoutedEventArgs e) => CaptureOutput.OpenFolder(CaptureOutput.RecordingsFolder);

    private void OnResetFolder(object sender, RoutedEventArgs e)
    {
        AppSettings.Update(s => s with { SaveFolder = null });
        ShowFolder(AppSettings.Current);
    }

    private void OnBack(object sender, RoutedEventArgs e) => Frame.GoBack();
}
