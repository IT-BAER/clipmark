using Clipmark.App.Output;
using Clipmark.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

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
        _loading = false;
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
