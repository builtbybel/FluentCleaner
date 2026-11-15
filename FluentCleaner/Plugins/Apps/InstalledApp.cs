namespace FluentCleaner.Plugins.Apps;

//how freely an app may be removed: a personal one, a known candidate, or one windows needs
internal enum AppSafety { Personal, Candidate, Protected }

//one app windows reported, with everything the list and the uninstaller need to know about it
internal sealed class InstalledApp
{
    public string Name { get; set; } = "";           // what the start menu calls it
    public string Family { get; set; } = "";         // the package family name, which identifies it
    public string Publisher { get; set; } = "";      // who signed it
    public string Uninstall { get; set; } = "";      // the uninstaller of a classic program, empty for a store app
    public string QuietUninstall { get; set; } = ""; // the same without a window, where the maker supplied one
    public bool IsFramework { get; set; }            // other apps need it to run
    public bool IsResourcePackage { get; set; }      // language or asset part of another app
    public bool NonRemovable { get; set; }           // windows refuses to uninstall it
    public AppSafety Safety;                         // the level AppRules.ini puts it in
    public bool IsProgram => Uninstall.Length > 0;   // installed the classic way, not out of the store
    public bool Selected;                            // ticked for removal
    public bool Removed;                             // already gone in this session
    public bool CanRemove => !Removed && Safety != AppSafety.Protected
        && !IsFramework && !IsResourcePackage && !NonRemovable;
}

//the result of one look at the installed apps: the list, the reason a source stayed silent, or both
internal sealed class AppScan
{
    public List<InstalledApp> Apps = new();   // the apps windows reported
    public string Error = "";                 // why a source reported none, shown above the list
}
