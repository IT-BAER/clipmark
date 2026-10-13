namespace Clipmark.Core;

/// <summary>User-visible Core text: the app plugs in its resource lookup; without one (tests) the English text is used.</summary>
public static class CoreText
{
    /// <summary>Returns the localized text for a resource key, or null when the key is missing.</summary>
    public static Func<string, string?>? Resolve { get; set; }

    internal static string T(string key, string english) => Resolve?.Invoke(key) ?? english;
}
