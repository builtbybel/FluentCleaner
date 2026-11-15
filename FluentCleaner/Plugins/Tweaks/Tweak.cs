using FluentCleaner.Services;   // Loc

namespace FluentCleaner.Plugins.Tweaks;

//one registry value a tweak writes. a tweak that changes three values carries three of these,
//which is what path2, valuename2 and the rest of the numbered keys in the ini mean.
internal sealed class Entry
{
    public string Path = "";            // hive and key in one line, long or short spelling
    public string ValueName = "";       // (Default) means the key's unnamed value
    public string ValueType = "DWORD";  // DWORD, QWORD, String, ExpandString or Binary
    public string Recommended = "";     // what applying writes, and what the check compares
    public string Default = "";         // what resetting writes, empty means it cannot go back
}

//one section of the ini, read once at start and then only asked
internal sealed class Tweak
{
    // --- what the ini says ---------------------------------------------------

    public string Name = "";            // the section heading, unique, and the id everywhere else
    public string Category = "";        // the group it is listed under
    public string Description = "";     // the plain words the detail card shows
    public string Restart = "";         // explorer or system, empty when it takes effect at once
    public string OsVersion = "";       // 10 or 11, empty means both
    public string Requires = "";        // a key that has to be there, else this pc is not meant
    public string Group = "";           // variants of one setting share it, and only one may win
    public bool Recommended;            // false leaves the row unticked after a check
    public List<Entry> Entries = new(); // written in order; failures are not rolled back

    // --- what the app makes of it --------------------------------------------

    //the machine hive and anything under policies decide for every account on the pc,
    //so windows refuses them to a normal user and the app has to say so before it tries
    public bool NeedsAdmin => _admin ?? (_admin = Entries.Any(entry =>
        Machine.Any(hive => entry.Path.StartsWith(hive + @"\", StringComparison.OrdinalIgnoreCase))
        || entry.Path.IndexOf(@"\Policies\", StringComparison.OrdinalIgnoreCase) >= 0)).Value;

    //both spellings of the two hives the ini may use for them
    private static readonly string[] Machine =
        { "HKLM", "HKEY_LOCAL_MACHINE", "HKCR", "HKEY_CLASSES_ROOT" };

    private bool? _admin;               // worked out once, the paths never change afterwards

    //the ini holds the english wording, a language file beside it may replace it
    public string Title => RuleText.Get("Name", Name, Name);
    public string Text => RuleText.Get("Detail", Name, Description);
    //a category out of a plugin's ini has no Where_ key in any locale file, so the raw name the
    //ini wrote is the stand-in: "Browser" reads like a category, "Where_Browser" does not
    public string Where => Loc.GetOr("Where_" + Category, Category);
}
