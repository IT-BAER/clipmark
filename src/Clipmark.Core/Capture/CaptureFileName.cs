namespace Clipmark.Core.Capture;

public static class CaptureFileName
{
    /// <summary>"Clipmark yyyy-MM-dd HHmmss.png", with " (n)" when <paramref name="taken"/> reports the name as used.</summary>
    public static string Next(DateTime time, Func<string, bool> taken)
    {
        string stem = "Clipmark " + time.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string name = stem + ".png";
        for (int n = 2; taken(name); n++)
            name = $"{stem} ({n}).png";
        return name;
    }
}
