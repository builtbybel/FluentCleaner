using FluentCleaner.Services;   // ResourceService

namespace FluentCleaner.Plugins;

/// <summary>
/// The ported Bloatynosy engines call Loc, this just forwards to <see cref="ResourceService"/>.
/// Used by Tweaks and Apps only, the core app never calls it.
/// </summary>
internal static class Loc
{
    /// <summary>Localised string, or the key if it's missing.</summary>
    public static string Get(string key) => ResourceService.Get(key);

    /// <summary>Localised string, or <paramref name="fallback"/> if the key is missing.</summary>
    public static string GetOr(string key, string fallback)
    {
        var text = ResourceService.Get(key);
        return text == key ? fallback : text;
    }

    /// <summary>Format-string lookup.</summary>
    public static string Format(string key, params object[] args) => ResourceService.Fmt(key, args);
}
