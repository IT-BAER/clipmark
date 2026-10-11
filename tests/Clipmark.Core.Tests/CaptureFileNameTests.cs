using Clipmark.Core.Capture;

namespace Clipmark.Core.Tests;

public class CaptureFileNameTests
{
    private static readonly DateTime Time = new(2026, 10, 11, 16, 5, 9);

    [Fact]
    public void Name_uses_date_and_time()
    {
        Assert.Equal("Clipmark 2026-10-11 160509.png", CaptureFileName.Next(Time, _ => false));
    }

    [Fact]
    public void Taken_names_get_a_counter()
    {
        var taken = new HashSet<string> { "Clipmark 2026-10-11 160509.png", "Clipmark 2026-10-11 160509 (2).png" };
        Assert.Equal("Clipmark 2026-10-11 160509 (3).png", CaptureFileName.Next(Time, taken.Contains));
    }
}
