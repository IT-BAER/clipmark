using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Clipmark.App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        bool registered = Spike.RegisterHotkey(hwnd, () => _ = CaptureFromHotkeyAsync());
        AppTitleBar.Subtitle = registered ? "Ctrl+Shift+F8 captures all monitors" : "Hotkey Ctrl+Shift+F8 is taken";
    }

    private async Task CaptureFromHotkeyAsync()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Clipmark", "spike-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        try
        {
            AppTitleBar.Subtitle = await Spike.CaptureToFolderAsync(folder);
        }
        catch (Exception e)
        {
            AppTitleBar.Subtitle = "Capture failed: " + e.Message;
        }
    }
}
