using FluentCleaner.Services;   // Loc and WorkProgress are shared with the app uninstaller

namespace FluentCleaner.Plugins.Tweaks;

//reads the pc and never writes to it. writing is the applier's job alone.
internal static class Scanner
{
    //reads every tweak that fits this windows and reports one finding per tweak, with its progress
    public static List<Finding> Scan(IProgress<WorkProgress>? progress = null)
    {
        var tweaks = TweakDatabase.All.Where(Fits).ToList();
        var findings = new List<Finding>(tweaks.Count);
        for (int index = 0; index < tweaks.Count; index++)
        {
            var tweak = tweaks[index];
            progress?.Report(new WorkProgress(index, tweaks.Count,
                Loc.Format("Progress_Checking", tweak.Title, index + 1, tweaks.Count)));
            //Only catalogue recommendations are preselected; recipes are explicit UI actions.
            var done = tweak.Entries.All(Holds);
            findings.Add(new Finding { Tweak = tweak, Done = done, Wanted = !done && tweak.Recommended });
            if (index == tweaks.Count - 1)
                progress?.Report(new WorkProgress(tweaks.Count, tweaks.Count, Loc.Format("Progress_Checked", tweaks.Count)));
        }
        return findings;
    }

    // --- one value -----------------------------------------------------------

    //whether one value already stands as the catalogue wants it; a missing value counts as not set
    internal static bool Holds(Entry entry) =>
        RegistryService.Matches(entry, entry.Recommended);

    // --- this windows --------------------------------------------------------

    //whether a tweak belongs on this pc: its os version has to match and its required key exist.
    //public, because the number the start page promises has to be the number the scan then reports
    public static bool Fits(Tweak tweak) =>
        (tweak.OsVersion.Length == 0 || (tweak.OsVersion == "11") == (Environment.OSVersion.Version.Build >= 22000))
        && (tweak.Requires.Length == 0 || tweak.Requires.Split('|').Any(RegistryService.Exists));
}
