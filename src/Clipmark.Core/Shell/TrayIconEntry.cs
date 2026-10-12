namespace Clipmark.Core.Shell;

/// <summary>
/// Windows 11 keeps one HKCU\Control Panel\NotifyIconSettings entry per exe path; a new entry has no IsPromoted value,
/// so the icon starts in the overflow. Undocumented: if Windows changes it the icon only stays in the overflow.
/// </summary>
public static class TrayIconEntry
{
    /// <summary>FOLDERID_ProgramFiles: packaged apps store their path with this prefix.</summary>
    private const string ProgramFilesId = "{6D809377-6AF0-444B-8957-A3773F02200E}";

    public static bool IsFor(string? storedPath, string exePath, string programFiles)
    {
        if (string.IsNullOrEmpty(storedPath))
            return false;
        if (storedPath.StartsWith(ProgramFilesId + "\\", StringComparison.OrdinalIgnoreCase))
            storedPath = Path.TrimEndingDirectorySeparator(programFiles) + storedPath[ProgramFilesId.Length..];
        return string.Equals(storedPath, exePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Only a missing value means "never chosen"; 0 is the user hiding the icon and must stay.</summary>
    public static bool ShouldPromote(object? isPromoted) => isPromoted is null;
}
