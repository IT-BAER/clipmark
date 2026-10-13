using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Clipmark.App;

/// <summary>UI text from Strings\&lt;language&gt;\Resources.resw (MRT Core). Keys have no dots: a dot means Uid.Property.</summary>
/// <remarks>First use must come after App sets the language override, so the loader resolves the chosen language.</remarks>
internal static class Loc
{
    private static readonly Lazy<ResourceLoader> Loader = new(() => new ResourceLoader());

    /// <summary>The text for the key; a missing key returns the key itself, so gaps show in the UI.</summary>
    public static string Get(string key) => TryGet(key) ?? key;

    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>The text for the key, or null when it is missing.</summary>
    public static string? TryGet(string key)
    {
        try
        {
            return Loader.Value.GetString(key) is { Length: > 0 } text ? text : null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null; // MRT Core throws for a missing resource
        }
    }
}
