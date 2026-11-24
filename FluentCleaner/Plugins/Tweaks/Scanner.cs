using FluentCleaner.Services;   // Loc and WorkProgress are shared with the app uninstaller

namespace FluentCleaner.Plugins.Tweaks;

//read only;writing is done by the Applier
internal static class Scanner
{
    //checks every tweak that fits this windows, one finding per tweak
    public static List<Finding> Scan(IProgress<WorkProgress>? progress = null)
    {
        var tweaks = TweakDatabase.All.Where(Fits).ToList();
        var findings = new List<Finding>(tweaks.Count);
        for (int index = 0; index < tweaks.Count; index++)
        {
            var tweak = tweaks[index];
            progress?.Report(new WorkProgress(index, tweaks.Count,
                Loc.Format("Progress_Checking", tweak.Title, index + 1, tweaks.Count)));
            //only recommended tweaks get pre-ticked
            var done = tweak.Entries.All(Holds);
            findings.Add(new Finding { Tweak = tweak, Done = done, Wanted = !done && tweak.Recommended });
            if (index == tweaks.Count - 1)
                progress?.Report(new WorkProgress(tweaks.Count, tweaks.Count, Loc.Format("Progress_Checked", tweaks.Count)));
        }
        return findings;
    }

    // --- one value -----------------------------------------------------------

    //is the value already set as recommended? missing value = not set
    internal static bool Holds(Entry entry) =>
        RegistryService.Matches(entry, entry.Recommended);

    // --- this windows --------------------------------------------------------

    //os version has to match and the required key has to exist.
    //public, the start page uses it too so the counts match
    public static bool Fits(Tweak tweak) =>
        (tweak.OsVersion.Length == 0 || (tweak.OsVersion == "11") == AppInfo.IsEleven)
        && (tweak.Requires.Length == 0 || tweak.Requires.Split('|').Any(RegistryService.Exists));
}
