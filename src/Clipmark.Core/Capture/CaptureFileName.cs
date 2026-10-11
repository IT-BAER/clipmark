namespace Clipmark.Core.Capture;

public static class CaptureFileName
{
    /// <summary>"Clipmark yyyy-MM-dd HHmmss.png" (or another extension), with " (n)" when <paramref name="taken"/> reports the name as used.</summary>
    public static string Next(DateTime time, Func<string, bool> taken, string extension = ".png")
    {
        string stem = "Clipmark " + time.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string name = stem + extension;
        for (int n = 2; taken(name); n++)
            name = $"{stem} ({n}){extension}";
        return name;
    }
}
