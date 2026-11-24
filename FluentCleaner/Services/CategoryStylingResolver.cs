namespace FluentCleaner.Services;

public readonly record struct CategoryStyle(
    string? AppIconSource,
    bool HasAppIcon,
    string Glyph,
    string AccentHex,
    string BackgroundHex
);

/// <summary>
/// Resolves authentic application brand assets, high-contrast Fluent iconography,
/// brand accent hues, and 28x28 badge container backgrounds for standard categories
/// as well as dynamic Winapp2 and Winapp3 (Dangerous) entry sets.
/// </summary>
public static class CategoryStylingResolver
{
    // High-visibility Danger warning palette for Winapp3 aggressive rules
    private const string DangerAccentHex = "#F87171"; // Coral Red
    private const string DangerBgHex     = "#22F87171"; // 13.5% translucent Coral

    public static CategoryStyle Resolve(string categoryName, string macroGroup)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return ResolveFallback(macroGroup);

        var trimmed = categoryName.Trim();

        // 1. Winapp3 "Dangerous ..." categories: inherit full resolved icon/glyph (including browser patterns) with danger palette
        if (trimmed.StartsWith("Dangerous ", StringComparison.OrdinalIgnoreCase))
        {
            var subCategory = trimmed["Dangerous ".Length..].Trim();
            var subStyle = Resolve(subCategory, macroGroup);
            return new CategoryStyle(
                subStyle.AppIconSource,
                subStyle.HasAppIcon,
                subStyle.Glyph,
                DangerAccentHex,
                DangerBgHex
            );
        }

        // 2. Exact application & category matches
        var style = ResolveAppStyle(trimmed, macroGroup);
        if (style is not null)
            return style.Value;

        // 3. Pattern matches (e.g. niche browsers "* Web Browser", "* Browser")
        if (trimmed.EndsWith("Web Browser", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("Browser", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("Internet Suite", StringComparison.OrdinalIgnoreCase))
        {
            return new CategoryStyle(null, false, "\uE774", "#38BDF8", "#2238BDF8");
        }

        // 4. Macro-group fallback with distinct vibrant colors
        return ResolveFallback(macroGroup);
    }

    private static CategoryStyle? ResolveAppStyle(string name, string macroGroup) => name switch
    {
        // --- Authentic Application Assets (HasAppIcon = true + non-empty Fluent fallback glyph) ---
        "Google Chrome" or "Chrome" =>
            new CategoryStyle("ms-appx:///Assets/Icons/chrome.png", true, "\uE774", "#FBBF24", "#22FBBF24"),

        "Microsoft Edge" or "Edge" or "Edge Chromium" =>
            new CategoryStyle("ms-appx:///Assets/Icons/edge.png", true, "\uE774", "#38BDF8", "#2238BDF8"),

        "Firefox" or "Mozilla Firefox" =>
            new CategoryStyle("ms-appx:///Assets/Icons/firefox.png", true, "\uE774", "#FB923C", "#22FB923C"),

        "Brave" =>
            new CategoryStyle("ms-appx:///Assets/Icons/brave.png", true, "\uE774", "#FB542B", "#22FB542B"),

        "Opera" or "Opera GX" =>
            new CategoryStyle("ms-appx:///Assets/Icons/opera.png", true, "\uE774", "#FF1B2D", "#22FF1B2D"),

        "Vivaldi" =>
            new CategoryStyle("ms-appx:///Assets/Icons/vivaldi.png", true, "\uE774", "#EF3939", "#22EF3939"),

        "Adobe" or "Adobe Acrobat" or "Adobe Reader" =>
            new CategoryStyle("ms-appx:///Assets/Icons/adobe.png", true, "\uE71D", "#ED2224", "#22ED2224"),

        "Microsoft PowerToys" or "PowerToys" =>
            new CategoryStyle("ms-appx:///Assets/Icons/powertoys.png", true, "\uE74C", "#0078D7", "#220078D7"),

        "Windows" =>
            new CategoryStyle("ms-appx:///Assets/Icons/windows.png", true, "\uE770", "#0078D4", "#220078D4"),

        "Microsoft Store" or "Windows Store" or "Store" =>
            new CategoryStyle("ms-appx:///Assets/Icons/store.png", true, "\uE71D", "#0078D4", "#220078D4"),

        // --- Guaranteed-Safe Universal Fluent Glyphs (HasAppIcon = false, zero missing '▯' boxes) ---
        "Applications" =>
            new CategoryStyle(null, false, "\uE71D", "#C084FC", "#22C084FC"),

        "Developer Tools" or "Developer & AI" =>
            new CategoryStyle(null, false, "\uE943", "#34D399", "#2234D399"),

        "AI" =>
            new CategoryStyle(null, false, "\uE945", "#10B981", "#2210B981"),

        "Command Palette" =>
            new CategoryStyle(null, false, "\uE756", "#38BDF8", "#2238BDF8"),

        "Utilities" =>
            new CategoryStyle(null, false, "\uE74C", "#4ADE80", "#224ADE80"),

        "Games" =>
            new CategoryStyle(null, false, "\uE7FC", "#A78BFA", "#22A78BFA"),

        "Multimedia" or "iTunes" or "Spotify" =>
            new CategoryStyle(null, false, "\uE8B9", "#F472B6", "#22F472B6"),

        "Language Files" =>
            new CategoryStyle(null, false, "\uE12B", "#818CF8", "#22818CF8"),

        "Internet" or "Internet Explorer" =>
            new CategoryStyle(null, false, "\uE774", "#22D3EE", "#2222D3EE"),

        "Long" =>
            new CategoryStyle(null, false, "\uE823", "#F59E0B", "#22F59E0B"),

        _ => null
    };

    private static CategoryStyle ResolveFallback(string macroGroup) => macroGroup switch
    {
        "Windows"  => new CategoryStyle("ms-appx:///Assets/Icons/windows.png", true, "\uE770", "#60A5FA", "#2260A5FA"),
        "Browsers" => new CategoryStyle(null, false, "\uE774", "#FBBF24", "#22FBBF24"),
        "DevAi"    => new CategoryStyle(null, false, "\uE943", "#34D399", "#2234D399"),
        _          => new CategoryStyle(null, false, "\uE71D", "#C084FC", "#22C084FC")
    };
}
