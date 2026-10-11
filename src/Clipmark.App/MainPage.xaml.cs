using Clipmark.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace Clipmark.App;

/// <summary>Launcher toolbar (New, mode, delay) and the last capture. M2 replaces the image view with the editor.</summary>
public sealed partial class MainPage : Page
{
    internal static readonly (CaptureMode Mode, string Text, string Glyph)[] Modes =
    [
        (CaptureMode.Rectangle, "Rectangle", "\uF407"), (CaptureMode.Window, "Window", "\uE737"),
        (CaptureMode.FullScreen, "Full screen", "\uE7F4"), (CaptureMode.AllMonitors, "All monitors", "\uEBC6"),
        (CaptureMode.Freeform, "Freeform", "\uF408"),
    ];

    public MainPage()
    {
        InitializeComponent();
        foreach (var (mode, text, glyph) in Modes)
        {
            var item = new RadioMenuFlyoutItem { Text = text, GroupName = "mode", Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => AppSettings.Update(s => s with { Mode = mode });
            ModeMenu.Items.Add(item);
        }
        foreach (int d in ClipmarkSettings.DelayChoices)
        {
            var item = new RadioMenuFlyoutItem { Text = DelayText(d), GroupName = "delay" };
            item.Click += (_, _) => AppSettings.Update(s => s with { DelaySeconds = d });
            DelayMenu.Items.Add(item);
        }
        Loaded += (_, _) => AppSettings.Changed += OnSettingsChanged;
        Unloaded += (_, _) => AppSettings.Changed -= OnSettingsChanged;
        OnSettingsChanged(AppSettings.Current);
    }

    private static string DelayText(int seconds) => seconds == 0 ? "No delay" : $"{seconds} seconds";

    private void OnSettingsChanged(ClipmarkSettings s)
    {
        int modeIndex = Array.FindIndex(Modes, m => m.Mode == s.Mode);
        ModeButton.Content = new FontIcon { Glyph = Modes[modeIndex].Glyph, FontSize = 16 };
        AutomationProperties.SetName(ModeButton, "Capture mode: " + Modes[modeIndex].Text);
        ((RadioMenuFlyoutItem)ModeMenu.Items[modeIndex]).IsChecked = true;

        int delayIndex = Array.IndexOf(ClipmarkSettings.DelayChoices, s.DelaySeconds);
        DelayButton.Content = s.DelaySeconds == 0 ? new FontIcon { Glyph = "\uE916", FontSize = 16 } : $"{s.DelaySeconds} s";
        AutomationProperties.SetName(DelayButton, "Delay: " + DelayText(s.DelaySeconds));
        ((RadioMenuFlyoutItem)DelayMenu.Items[delayIndex]).IsChecked = true;

        Hint.Text = $"Select New or press {s.Hotkey} to capture.";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is string path)
            ShowImage(path);
    }

    internal void ShowImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.ImageOpened += (_, _) =>
        {
            // Show at most 1:1 physical pixels.
            double scale = XamlRoot?.RasterizationScale ?? 1;
            CaptureImage.MaxWidth = bitmap.PixelWidth / scale;
            CaptureImage.MaxHeight = bitmap.PixelHeight / scale;
        };
        bitmap.UriSource = new Uri(path);
        CaptureImage.Source = bitmap;
        Hint.Visibility = Visibility.Collapsed;
        ImageScroller.Visibility = Visibility.Visible;
    }

    internal void ShowError(string text)
    {
        Message.Message = text;
        Message.IsOpen = true;
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        Message.IsOpen = false;
        _ = ((App)Application.Current).CaptureAsync();
    }

    private void OnSettings(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));
}
