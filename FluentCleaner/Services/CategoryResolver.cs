using FluentCleaner.Models;

namespace FluentCleaner.Services;

// Tiny map from Winapp2 LangSecRef codes to the category names we show in the UI.
// If Winapp2 gives us no known code, we fall back to Section, then finally "Other Applications".
public static class CategoryResolver
{
    private static readonly IReadOnlyDictionary<int, CategoryInfo> Categories =
        new Dictionary<int, CategoryInfo>
        {
            [3006] = new("Microsoft Edge", 5),
            [3021] = new("Applications", 10),
            [3022] = new("Internet", 20),
            [3023] = new("Multimedia", 30),
            [3024] = new("Utilities", 40),
            [3025] = new("Windows", 50),
            [3026] = new("Firefox", 60),
            [3027] = new("Opera", 70),
            [3028] = new("Safari", 80),
            [3029] = new("Google Chrome", 90),
            [3030] = new("Thunderbird", 100),
            [3031] = new("Microsoft Store", 110),
            [3033] = new("Vivaldi", 130),
            [3034] = new("Brave", 140),
            [3035] = new("Opera GX", 150),
            [3036] = new("Spotify", 160),
            [3037] = new("Avast Secure Browser", 170),
            [3038] = new("AVG Secure Browser", 180),
            [3039] = new("Arc Browser", 190),
            [3040] = new("iTunes", 200),
            [3042] = new("WhatsApp", 210),
            [3043] = new("Norton Private Browser", 220),
            [3044] = new("Avira Secure Browser", 230),
        }; // I removed a few rare codes (e.g. 3032, 3041), e.g. CCleaner Browser nobody needs that crap

    public static CategoryInfo TryMapLangSecRef(CleanerEntry entry)
    {
        if (entry.LangSecRef is int code && Categories.TryGetValue(code, out var category))
            return category;

        if (!string.IsNullOrWhiteSpace(entry.Section))
            return new CategoryInfo(entry.Section, 1000); // If we have a section, use that as the category, but put it after all the known LangSecRef categories.

        return new CategoryInfo("Other Applications", 2000);
    }

    public readonly record struct CategoryInfo(string Name, int Order);
}
