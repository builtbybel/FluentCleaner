using FluentCleaner.Services;   // Loc

namespace FluentCleaner.Plugins.Tweaks;

//one registry value of a tweak;path2, valuename2 etc. in the ini become more of these
internal sealed class Entry
{
    public string Path = "";            // hive + key, long or short hive name
    public string ValueName = "";       // (Default) = the unnamed value
    public string ValueType = "DWORD";  // DWORD, QWORD, String, ExpandString or Binary
    public string Recommended = "";     // written on apply, compared on check
    public string Default = "";         // written on reset;empty = no reset
}

//one ini section, read once at start
internal sealed class Tweak
{
    // --- from the ini --------------------------------------------------------

    public string Name = "";            // section name, also the id
    public string Category = "";        // group in the list
    public string Description = "";     // text for the detail card
    public string Restart = "";         // explorer or system, empty = no restart
    public string OsVersion = "";       // 10 or 11, empty = both
    public string Requires = "";        // key that has to exist, else hidden
    public string Group = "";           // variants of one setting, only one may be applied
    public bool Recommended;            // false = not pre-ticked
    public List<Entry> Entries = new(); // written in order;no rollback on failure

    // --- computed ------------------------------------------------------------

    //HKLM and policies need admin rights
    public bool NeedsAdmin => _admin ?? (_admin = Entries.Any(entry =>
        Machine.Any(hive => entry.Path.StartsWith(hive + @"\", StringComparison.OrdinalIgnoreCase))
        || entry.Path.IndexOf(@"\Policies\", StringComparison.OrdinalIgnoreCase) >= 0)).Value;

    //both spellings
    private static readonly string[] Machine =
        { "HKLM", "HKEY_LOCAL_MACHINE", "HKCR", "HKEY_CLASSES_ROOT" };

    private bool? _admin;               // cached

    //english from the ini, a Lang\ file can override it
    public string Title => RuleText.Get("Name", Name, Name);
    public string Text => RuleText.Get("Detail", Name, Description);
    //plugin categories have no Where_ key, so the raw name is shown
    public string Where => Loc.GetOr("Where_" + Category, Category);
}
