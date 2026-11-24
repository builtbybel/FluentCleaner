using Microsoft.Windows.ApplicationModel.Resources;

namespace FluentCleaner.Services;

//Localized string lookup for code-side strings (status messages, dialogs).
//XAML uses x:Uid directly
//New language: drop a Strings\{lang}\Resources.resw folder and rebuild
public static class ResourceService
{
    private static ResourceLoader? _loader;

    private static ResourceLoader Loader => _loader ??= new ResourceLoader();

    // Keys that came back missing once. GetString throws on a miss rather than returning "",
    // and a deliberate fallback lookup — Loc.GetOr, say — would otherwise throw on every call.
    // Concurrent, because the tweak scanner looks strings up from a background thread.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Missing = new();

    //Scans Strings\{lang}\ folders next to the exe for installed languages
    public static IReadOnlyList<string> GetAvailableLanguages()
    {
        var result = new List<string>();
        var dir = Path.Combine(AppContext.BaseDirectory, "Strings");
        if (!Directory.Exists(dir)) return result;

        foreach (var sub in Directory.GetDirectories(dir))
            if (File.Exists(Path.Combine(sub, "Resources.resw")))
                result.Add(Path.GetFileName(sub));

        return result;
    }

    //null / "" = follow Windows display language
    public static void SetLanguage(string? lang)
    {
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang ?? "";
        }
        catch { /* ignore;app keeps running in the current language */ }

        _loader = null;  //force a fresh loader so lookups pick up the new language
        Missing.Clear(); //a key absent in one language may well be present in the next
    }

    // Returns the key itself as fallback when a string is missing
    public static string Get(string key)
    {
        if (Missing.ContainsKey(key)) return key;

        try
        {
            var result = Loader.GetString(key);
            //resw stores \n literally will turn it into a real newline
            if (string.IsNullOrEmpty(result)) { Missing[key] = 0; return key; }
            return result.Replace("\\n", "\n");
        }
        catch { Missing[key] = 0; return key; }
    }

    //Format-string lookup; used for interpolated status messages.
    // e.g. ResourceService.Fmt("St_ScanComplete", files, reg, size)
    public static string Fmt(string key, params object[] args)
    {
        var template = Get(key);
        //If Get fell back to the key itself (no braces>> not a format string), skip formatting
        return template == key && !template.Contains('{') ? key : string.Format(template, args);
    }
}