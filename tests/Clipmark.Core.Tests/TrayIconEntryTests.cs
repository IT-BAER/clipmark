using Clipmark.Core.Shell;

namespace Clipmark.Core.Tests;

public class TrayIconEntryTests
{
    private const string ProgramFiles = @"C:\Program Files";

    [Theory]
    [InlineData(@"D:\Tools\Clipmark\Clipmark.App.exe", @"D:\Tools\Clipmark\Clipmark.App.exe")]
    [InlineData(@"d:\tools\clipmark\CLIPMARK.APP.EXE", @"D:\Tools\Clipmark\Clipmark.App.exe")]
    // Packaged apps: Windows stores Program Files as its known-folder GUID (measured for ChatGPT, Claude, Teams).
    [InlineData(@"{6D809377-6AF0-444B-8957-A3773F02200E}\WindowsApps\ITBAER.Clipmark_1.0.0.0_x64__abc\Clipmark.App.exe",
        @"C:\Program Files\WindowsApps\ITBAER.Clipmark_1.0.0.0_x64__abc\Clipmark.App.exe")]
    public void Matches_the_own_exe(string stored, string exe) =>
        Assert.True(TrayIconEntry.IsFor(stored, exe, ProgramFiles));

    [Theory]
    [InlineData(@"D:\Old\Clipmark.App.exe")]
    [InlineData(@"D:\Tools\Clipmark\Clipmark.App.exe.bak")]
    [InlineData(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Tools\Clipmark\Clipmark.App.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_other_paths(string? stored) =>
        Assert.False(TrayIconEntry.IsFor(stored, @"D:\Tools\Clipmark\Clipmark.App.exe", ProgramFiles));

    [Fact]
    public void Promotes_only_when_the_user_never_chose()
    {
        Assert.True(TrayIconEntry.ShouldPromote(null));
        Assert.False(TrayIconEntry.ShouldPromote(0)); // hidden by the user
        Assert.False(TrayIconEntry.ShouldPromote(1));
    }
}
