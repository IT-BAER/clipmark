using Velopack;

namespace Clipmark.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Velopack install, update and uninstall hooks run here and exit; outside a Velopack install it returns at once.
        VelopackApp.Build().Run();
        XamlGeneratedProgram.XamlGeneratedMain();
    }
}
