using FluentCleaner.Services;   // ResourceService

namespace FluentCleaner.Plugins;

/// <summary>
/// Seam between the ported engines and FluentCleaner's own localisation. Bloatynosy calls
/// <c>Loc</c>; this forwards to <see cref="ResourceService"/> so the ported files could move
/// across unchanged. Shared by the tweak catalogue and the app uninstaller.
/// <para>
/// Lives in Plugins\Shared, not in Services: the core app never calls it. Everything in this
/// folder goes when both plugin folders go.
/// </para>
/// </summary>
internal static class Loc
{
    /// <summary>Localised string, or the key itself when it is missing.</summary>
    public static string Get(string key) => ResourceService.Get(key);

    /// <summary>Localised string, or <paramref name="fallback"/> when the key is missing.</summary>
    public static string GetOr(string key, string fallback)
    {
        var text = ResourceService.Get(key);
        return text == key ? fallback : text;
    }

    /// <summary>Format-string lookup.</summary>
    public static string Format(string key, params object[] args) => ResourceService.Fmt(key, args);
}
