namespace FluentCleaner.Plugins.Apps;

//personal, safe to remove, or needed by windows
internal enum AppSafety { Personal, Candidate, Protected }

//one installed app (store or classic program)
internal sealed class InstalledApp
{
    public string Name { get; set; } = "";           // display name
    public string Family { get; set; } = "";         // package family name = id
    public string Publisher { get; set; } = "";      // signer
    public string Uninstall { get; set; } = "";      // uninstall string, empty for store apps
    public string QuietUninstall { get; set; } = ""; // silent version, if there is one
    public bool IsFramework { get; set; }            // other apps need it
    public bool IsResourcePackage { get; set; }      // language/asset part of another app
    public bool NonRemovable { get; set; }           // windows won't uninstall it
    public AppSafety Safety;                         // from AppRules.ini
    public bool IsProgram => Uninstall.Length > 0;   // classic program, not store
    public bool Selected;                            // ticked
    public bool Removed;                             // already removed this session
    public bool CanRemove => !Removed && Safety != AppSafety.Protected
        && !IsFramework && !IsResourcePackage && !NonRemovable;
}

//json source gen for the inventory, AOT has no reflection
[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<InstalledApp>))]
internal sealed partial class InventoryJson : System.Text.Json.Serialization.JsonSerializerContext;

//scan result: apps and/or an error
internal sealed class AppScan
{
    public List<InstalledApp> Apps = new();   // found apps
    public string Error = "";                 // shown above the list if a source failed
}
