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

    private static readonly Dictionary<string, CategoryInfo> CategoriesByName =
        Categories.Values
            .Concat(new[] { new CategoryInfo("Other Applications", 2000) })
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

    public static CategoryInfo TryMapLangSecRef(CleanerEntry entry)
    {
        if (entry.LangSecRef is int code && Categories.TryGetValue(code, out var category))
            return category;

        if (!string.IsNullOrWhiteSpace(entry.Section))
        {
            var section = entry.Section.Trim();
            return CategoriesByName.TryGetValue(section, out var known)
                ? known
                : new CategoryInfo(section, 1000); // Put custom sections after all known LangSecRef categories.
        }

        return CategoriesByName["Other Applications"];
    }

    public readonly struct CategoryInfo : IEquatable<CategoryInfo>
    {
        public CategoryInfo(string name, int order) { Name = name; Order = order; }
        public string Name { get; }
        public int Order { get; }

        public bool Equals(CategoryInfo other) =>
            Order == other.Order && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) =>
            obj is CategoryInfo other && Equals(other);

        public override int GetHashCode() =>
            (Name != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(Name) : 0) ^ Order;

        public static bool operator ==(CategoryInfo left, CategoryInfo right) => left.Equals(right);
        public static bool operator !=(CategoryInfo left, CategoryInfo right) => !left.Equals(right);
    }
}
