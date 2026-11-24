namespace FluentCleaner.Services;

public readonly record struct CategoryStyle(string Glyph, string AccentHex, string BackgroundHex);

/// <summary>
/// Resolves high-contrast Fluent iconography, brand accent hues, and 28x28 badge container backgrounds
/// for all standard categories as well as dynamic Winapp2 and Winapp3 (Dangerous) entry sets.
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

        // 1. Winapp3 "Dangerous ..." categories
        if (trimmed.StartsWith("Dangerous ", StringComparison.OrdinalIgnoreCase))
        {
            var subCategory = trimmed["Dangerous ".Length..].Trim();
            var (glyph, _) = ResolveAppGlyph(subCategory, macroGroup);
            return new CategoryStyle(glyph, DangerAccentHex, DangerBgHex);
        }

        // 2. Exact application & category matches
        var (appGlyph, appStyle) = ResolveAppGlyph(trimmed, macroGroup);
        if (appStyle is not null)
            return appStyle.Value;

        // 3. Pattern matches (e.g. niche browsers "* Web Browser", "* Browser")
        if (trimmed.EndsWith("Web Browser", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("Browser", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("Internet Suite", StringComparison.OrdinalIgnoreCase))
        {
            return new CategoryStyle("\uE774", "#38BDF8", "#2238BDF8"); // Globe
        }

        // 4. Macro-group fallback with distinct vibrant colors
        return ResolveFallback(macroGroup);
    }

    private static (string glyph, CategoryStyle? fullStyle) ResolveAppGlyph(string name, string macroGroup) => name switch
    {
        // Creative & Documents
        "Adobe" => ("\uE790", new CategoryStyle("\uE790", "#EF4444", "#22EF4444")), // Pen Tool / Creative Suite in Adobe Red

        // Web Browsers
        "Google Chrome"          => ("\uE774", new CategoryStyle("\uE774", "#FBBF24", "#22FBBF24")), // High-Voltage Chrome Gold
        "Microsoft Edge"         => ("\uE774", new CategoryStyle("\uE774", "#38BDF8", "#2238BDF8")), // Vibrant Sky Blue
        "Edge Chromium"          => ("\uE774", new CategoryStyle("\uE774", "#38BDF8", "#2238BDF8")),
        "Firefox"                => ("\uE774", new CategoryStyle("\uE774", "#FB923C", "#22FB923C")), // Radiant Firefox Orange
        "Brave"                  => ("\uEA18", new CategoryStyle("\uEA18", "#FB7185", "#22FB7185")), // Brave Lion Shield
        "Opera" or "Opera GX"    => ("\uE774", new CategoryStyle("\uE774", "#F87171", "#22F87171")), // Opera Red
        "Vivaldi" or "Arc Browser" => ("\uE774", new CategoryStyle("\uE774", "#22D3EE", "#2222D3EE")), // Cyan
        "Internet Explorer"      => ("\uE774", new CategoryStyle("\uE774", "#38BDF8", "#2238BDF8")), // IE Sky
        "Safari"                 => ("\uE774", new CategoryStyle("\uE774", "#38BDF8", "#2238BDF8")),

        // Core Windows & Platforms
        "Windows"                => ("\uE782", new CategoryStyle("\uE782", "#60A5FA", "#2260A5FA")), // Electric Azure
        "Microsoft Store" or "Windows Store" => ("\uE719", new CategoryStyle("\uE719", "#60A5FA", "#2260A5FA")), // Store Bag

        // Developer & AI
        "Developer Tools" or "Developer & AI" => ("\uE943", new CategoryStyle("\uE943", "#34D399", "#2234D399")), // Code brackets {}
        "AI"                     => ("\uE945", new CategoryStyle("\uE945", "#10B981", "#2210B981")), // Sparkles / AI Copilot
        "Command Palette"        => ("\uE756", new CategoryStyle("\uE756", "#38BDF8", "#2238BDF8")), // Terminal prompt

        // Hardware, Utilities, Tools
        "Microsoft PowerToys"    => ("\uEDA2", new CategoryStyle("\uEDA2", "#FCD34D", "#22FCD34D")), // PowerTools Amber
        "Utilities"              => ("\uE74C", new CategoryStyle("\uE74C", "#4ADE80", "#224ADE80")), // Lime Tools
        "Applications"           => ("\uECAE", new CategoryStyle("\uECAE", "#C084FC", "#22C084FC")), // App Grid Tiles
        "Games"                  => ("\uE7FC", new CategoryStyle("\uE7FC", "#A78BFA", "#22A78BFA")), // Gamepad Controller
        "Multimedia" or "iTunes" or "Spotify" => ("\uE8B9", new CategoryStyle("\uE8B9", "#F472B6", "#22F472B6")), // Hot Pink Media Film

        // International, Languages, Long
        "Language Files"         => ("\uF2B7", new CategoryStyle("\uF2B7", "#818CF8", "#22818CF8")), // Language Translation
        "Internet"               => ("\uE12B", new CategoryStyle("\uE12B", "#22D3EE", "#2222D3EE")), // Ocean Network Globe
        "Long"                   => ("\uE823", new CategoryStyle("\uE823", "#F59E0B", "#22F59E0B")), // Timer / Clock Warning

        // Default: return fallback glyph with null style
        _ => (ResolveFallbackGlyph(macroGroup), null)
    };

    private static string ResolveFallbackGlyph(string macroGroup) => macroGroup switch
    {
        "Windows"  => "\uE782",
        "Browsers" => "\uE774",
        "DevAi"    => "\uE943",
        _          => "\uECAE"
    };

    private static CategoryStyle ResolveFallback(string macroGroup) => macroGroup switch
    {
        "Windows"  => new CategoryStyle("\uE782", "#60A5FA", "#2260A5FA"),
        "Browsers" => new CategoryStyle("\uE774", "#FBBF24", "#22FBBF24"),
        "DevAi"    => new CategoryStyle("\uE943", "#34D399", "#2234D399"),
        _          => new CategoryStyle("\uECAE", "#C084FC", "#22C084FC")
    };
}
