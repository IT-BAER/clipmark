using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Clipmark.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        if (Spike.Arg("--spike-capture") is { } folder)
        {
            _ = RunThenExitAsync(() => Spike.CaptureToFolderAsync(folder));
            return;
        }
        _window = new MainWindow();
        if (Spike.Arg("--spike-startup") is { } marker)
        {
            void OnFirstFrame(object? sender, object e)
            {
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFirstFrame;
                File.WriteAllText(marker, DateTime.UtcNow.Ticks.ToString());
                Exit();
            }
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFirstFrame;
        }
        _window.Activate();
    }

    private async Task RunThenExitAsync(Func<Task<string>> work)
    {
        try { await work(); }
        finally { Exit(); }
    }
}
