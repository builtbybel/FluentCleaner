using FluentCleaner.Services;   // Loc and WorkProgress are shared with the app uninstaller

namespace FluentCleaner.Plugins.Tweaks;

//writes the ticked tweaks;the only class that writes to the registry.
//app removal is in the Apps plugin, not here
internal static class Applier
{
    //applies the ticked findings, then tells the shell (it caches some values)
    public static Report Apply(List<Finding> findings, IProgress<WorkProgress>? progress = null, int total = 0)
    {
        var report = Apply(findings, Write, Scanner.Holds, progress, total);
        if (report.Fixed + report.Reset > 0) WindowsActions.NotifyShellSettingsChanged();
        return report;
    }

    //same with any writer, for tests.
    //validates everything first;a failed write stops the row, no rollback
    internal static Report Apply(List<Finding> findings, Func<Entry, bool, bool> write, Func<Entry, bool> holds,
        IProgress<WorkProgress>? progress = null, int total = 0)
    {
        var report = new Report();
        var selected = findings.Where(f => f.Wanted).ToList();
        var conflicts = selected.Where(f => f.Tweak.Group.Length > 0)
            .GroupBy(f => f.Tweak.Group).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (total == 0) total = selected.Count;
        var completed = 0;
        foreach (var finding in selected)
        {
            bool reset = finding.Done;
            progress?.Report(new WorkProgress(completed, total,
                Loc.Format(reset ? "Progress_Resetting" : "Progress_Applying", finding.Tweak.Title)));
            bool conflict = conflicts.Contains(finding.Tweak.Group);
            bool success = !conflict && finding.Tweak.Entries.Count > 0
                && finding.Tweak.Entries.All(e => Valid(e, reset));
            if (success)
                foreach (var entry in finding.Tweak.Entries)
                    if (!write(entry, reset)) { success = false; break; }
            if (success)
            {
                if (reset) report.Reset++; else report.Fixed++;
                finding.Done = finding.Tweak.Entries.All(holds);
                finding.Wanted = false;
                //restart only for writes that worked
                var restart = finding.Tweak.Restart;
                if (restart.Length > 0 && !report.Restart.Contains(restart)) report.Restart.Add(restart);
            }
            else
            {
                report.Failed.Add(finding.Tweak.Title + (conflict ? ": " + Loc.Get("Tweak_Conflict") : ""));
                report.NeedsAdmin |= !conflict && finding.Tweak.NeedsAdmin;
            }
            progress?.Report(new WorkProgress(++completed, total, Loc.Format("Progress_Completed", completed, total)));
        }
        return report;
    }

    // --- values --------------------------------------------------------------

    //reset needs a default, value has to fit the type (<delete> markers are ok)
    internal static bool Valid(Entry entry, bool reset)
    {
        var value = reset ? entry.Default : entry.Recommended;
        if (reset && value.Length == 0) return false;
        return RegistryService.Deletes(value) || RegistryService.Fits(value, entry.ValueType);
    }

    //writes or deletes, then reads back;windows sometimes accepts a write and ignores it
    private static bool Write(Entry entry, bool reset)
    {
        var value = reset ? entry.Default : entry.Recommended;
        var done = RegistryService.DeletesKey(value) ? RegistryService.DeleteKey(entry)
            : RegistryService.Deletes(value) ? RegistryService.DeleteValue(entry)
            : RegistryService.Write(entry, value);
        return done && RegistryService.Matches(entry, value);
    }
}
