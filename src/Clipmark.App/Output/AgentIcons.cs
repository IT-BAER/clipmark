using System.Runtime.InteropServices.WindowsRuntime;
using Clipmark.Core.Sharing;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Management.Deployment;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Output;

/// <summary>Menu icons of AI agents, read at runtime from the agents' own executables; Clipmark ships no vendor logos.</summary>
internal static class AgentIcons
{
    private static readonly Dictionary<string, ImageSource?> Images = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Agent, string> InstalledApps = [];

    /// <summary>
    /// The icon of the target's exe, else of a running or installed desktop app of the same agent (codex.exe has none);
    /// null when none of them has one. <paramref name="size"/> is in physical pixels.
    /// </summary>
    public static ImageSource? For(AgentTarget target, IReadOnlyList<ProcessEntry> processes, int size)
    {
        foreach (var path in AgentTargets.IconSources(target, processes))
        {
            if (Load(path, size) is { } image)
                return image;
        }
        return InstalledApp(target.Agent) is { Length: > 0 } app ? Load(app, size) : null;
    }

    private static string InstalledApp(Agent agent)
    {
        if (InstalledApps.TryGetValue(agent, out var cached))
            return cached;
        var (family, exe) = AgentTargets.DesktopPackage(agent);
        string path = "";
        try
        {
            if (new PackageManager().FindPackagesForUser("", family).FirstOrDefault() is { } package)
                path = Path.Combine(package.InstalledPath, exe);
        }
        catch (Exception)
        {
            // The package query is optional: without it the menu shows the glyph.
        }
        return InstalledApps[agent] = path;
    }

    private static unsafe ImageSource? Load(string path, int size)
    {
        string key = $"{size}|{path}";
        if (Images.TryGetValue(key, out var cached))
            return cached;
        nint icon = 0;
        ImageSource? image = null;
        if (PrivateExtractIcons(path, 0, size, size, &icon, null, 1, 0) == 1 && icon != 0)
        {
            try
            {
                image = ToBitmap(icon, size);
            }
            finally
            {
                DestroyIcon(icon);
            }
        }
        return Images[key] = image;
    }

    /// <summary>The icon's color bitmap as premultiplied BGRA.</summary>
    /// <remarks>Shortcut: an icon without an alpha channel is shown opaque (its AND mask is ignored).</remarks>
    private static unsafe WriteableBitmap? ToBitmap(nint icon, int size)
    {
        ICONINFO info;
        if (!GetIconInfo(icon, &info))
            return null;
        try
        {
            if (info.hbmColor == 0)
                return null;
            var header = new BITMAPINFOHEADER { biSize = sizeof(BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
            var pixels = new byte[size * size * 4];
            nint dc = GetDC(0);
            int lines;
            fixed (byte* p = pixels)
                lines = GetDIBits(dc, info.hbmColor, 0, (uint)size, p, &header, 0);
            ReleaseDC(0, dc);
            if (lines != size)
                return null;
            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length && !hasAlpha; i += 4)
                hasAlpha = pixels[i] != 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int a = hasAlpha ? pixels[i + 3] : 255;
                pixels[i] = (byte)(pixels[i] * a / 255);
                pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
                pixels[i + 3] = (byte)a;
            }
            var bitmap = new WriteableBitmap(size, size);
            pixels.CopyTo(bitmap.PixelBuffer);
            bitmap.Invalidate();
            return bitmap;
        }
        finally
        {
            DeleteObject(info.hbmColor);
            DeleteObject(info.hbmMask);
        }
    }
}
