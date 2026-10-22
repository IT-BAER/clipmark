using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Shell;

/// <summary>
/// The tray icon's context menu as a WinUI MenuFlyout, styled like the Windows 11 shell menus. It opens from a 1 px host
/// window at the click point that is fully transparent; in test mode the host is cloaked, which hides the flyout too.
/// </summary>
internal sealed class TrayMenu
{
    private readonly Window _host;
    private readonly Grid _root = new();
    private readonly nint _hwnd;
    private MenuFlyout? _flyout;
    private Action? _picked;

    public TrayMenu()
    {
        _host = new Window { Content = _root };
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_host);
        // AOT: AppWindow.Presenter is the base type, so the window gets its own OverlappedPresenter.
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        _host.AppWindow.SetPresenter(presenter);
        _host.AppWindow.IsShownInSwitchers = false;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, GetWindowLongPtr(_hwnd, GWL_EXSTYLE) | (nint)(WS_EX_LAYERED | WS_EX_TOOLWINDOW));
        SetLayeredWindowAttributes(_hwnd, 0, 0, LWA_ALPHA);
        if (App.TestDir is not null)
        {
            int cloak = 1;
            unsafe { DwmSetWindowAttribute(_hwnd, 13 /* DWMWA_CLOAK */, &cloak, sizeof(int)); }
        }
    }

    /// <summary>The host's XamlRoot: the open flyout is among its popups.</summary>
    internal XamlRoot? Root => _root.XamlRoot;

    /// <summary>Screen position (physical px) of the host window, which is the menu's anchor.</summary>
    internal PointInt32 HostPosition => _host.AppWindow.Position;

    public MenuFlyoutItem Item(string text, string glyph, Action? pick, string? shortcut = null)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        if (shortcut is not null)
            item.KeyboardAcceleratorTextOverride = shortcut;
        item.Click += (_, _) => _picked = pick;
        return item;
    }

    public MenuFlyoutSubItem SubMenu(string text, string glyph, IEnumerable<(string Text, string? Glyph, bool Checked, Action Pick)> choices)
    {
        var sub = new MenuFlyoutSubItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        foreach (var (choice, choiceGlyph, isChecked, pick) in choices)
        {
            var item = new RadioMenuFlyoutItem { Text = choice, GroupName = text, IsChecked = isChecked };
            if (choiceGlyph is not null)
                item.Icon = new FontIcon { Glyph = choiceGlyph };
            item.Click += (_, _) => _picked = pick;
            sub.Items.Add(item);
        }
        return sub;
    }

    /// <summary>
    /// Opens the menu with a corner at the screen point (physical px), in the Theme setting; the picked action runs once
    /// the menu is gone.
    /// </summary>
    public void Show(int x, int y, string theme, IEnumerable<MenuFlyoutItemBase> items)
    {
        _flyout?.Hide();
        var flyout = new MenuFlyout { ShouldConstrainToRootBounds = false };
        foreach (var item in items)
            flyout.Items.Add(item);
        flyout.Closed += async (_, _) =>
        {
            if (_flyout != flyout)
                return;
            _flyout = null;
            _host.AppWindow.Hide();
            if (_picked is not { } pick)
                return;
            _picked = null;
            await Task.Delay(60); // let DWM compose a frame without the menu before a capture freezes the screen
            pick();
        };
        _flyout = flyout;
        _picked = null;
        _root.RequestedTheme = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        _host.AppWindow.MoveAndResize(new RectInt32(x, y, 1, 1));
        if (App.TestDir is null)
        {
            _host.Activate();
            // Without foreground the menu does not close on an outside click; the tray callback lets this process take it.
            SetForegroundWindow(_hwnd);
        }
        else
            _host.AppWindow.Show(false); // test mode: no activation, nothing on the desktop

        void Open() => flyout.ShowAt(_root, new FlyoutShowOptions { Position = new Windows.Foundation.Point(0, 0) });
        if (_root.IsLoaded)
            Open();
        else
        {
            void OnLoaded(object sender, RoutedEventArgs e)
            {
                _root.Loaded -= OnLoaded;
                if (_flyout == flyout)
                    Open();
            }
            _root.Loaded += OnLoaded;
        }
    }

    public void Close() => _host.Close();
}
