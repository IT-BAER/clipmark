using Clipmark.App.Interop;
using Microsoft.UI.Xaml;

namespace Clipmark.App;

/// <summary>Launcher and result window. Closing hides it; the app keeps running in the tray.</summary>
public sealed partial class MainWindow : Window
{
    public nint Handle { get; }

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            AppWindow.Hide();
        };
        RootFrame.Navigate(typeof(MainPage));
    }

    public bool IsShown => AppWindow.IsVisible && !_cloaked;

    private bool _cloaked;

    /// <summary>Cloaking hides the window at once, without the minimize animation that would end up in the capture.</summary>
    public unsafe void SetCloaked(bool cloaked)
    {
        int value = cloaked ? 1 : 0;
        Native.DwmSetWindowAttribute(Handle, 13 /* DWMWA_CLOAK */, &value, sizeof(int));
        _cloaked = cloaked;
    }

    public void ShowAndActivate()
    {
        if (_cloaked)
            SetCloaked(false);
        AppWindow.Show();
        Activate();
    }

    private MainPage? Page()
    {
        if (RootFrame.Content is not MainPage)
        {
            while (RootFrame.CanGoBack)
                RootFrame.GoBack();
        }
        return RootFrame.Content as MainPage;
    }

    public void ShowImage(string path) => Page()?.ShowImage(path);

    public void ShowError(string text) => Page()?.ShowError(text);

    public void ShowSettings()
    {
        if (RootFrame.Content is not SettingsPage)
            RootFrame.Navigate(typeof(SettingsPage));
    }
}
