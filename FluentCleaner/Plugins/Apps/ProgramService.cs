using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentCleaner.Services;   // Loc
using Microsoft.Win32;

namespace FluentCleaner.Plugins.Apps;

//the classic programs beside the store apps: the control panel reads them out of the uninstall
//keys and so does this. nothing here writes to the registry, it only starts what windows stored.
internal static class ProgramService
{
    //the three places windows lists an installed program in: both machine views, and this user
    private static readonly (RegistryHive Hive, RegistryView View)[] Places = {
        (RegistryHive.LocalMachine, RegistryView.Registry64),
        (RegistryHive.LocalMachine, RegistryView.Registry32),
        (RegistryHive.CurrentUser, RegistryView.Registry64)
    };
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Mark = "prog:";          // keeps a program identity apart from a package family

    //every program the control panel would also show, each with the level the rules put it in
    public static List<InstalledApp> Scan(AppRules rules)
    {
        var found = new List<InstalledApp>();
        foreach (var (hive, view) in Places)
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var list = root.OpenSubKey(Uninstall);
            if (list == null) continue;
            foreach (var id in list.GetSubKeyNames())
            {
                using var entry = list.OpenSubKey(id);
                if (Read(entry, id) is InstalledApp app) found.Add(app);
            }
        }
        //a 32 and a 64 bit build can share one key name, and one row per identity is what we need
        return found.GroupBy(app => app.Family, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var app = group.First();
            app.Safety = rules.Classify(app);
            app.Selected = false;
            return app;
        }).ToList();
    }

    //one uninstall entry, or null where windows hides it from the control panel as well
    private static InstalledApp? Read(RegistryKey? entry, string id)
    {
        if (entry == null) return null;
        var name = entry.GetValue("DisplayName") as string ?? "";
        var command = entry.GetValue("UninstallString") as string ?? "";
        //no name or no uninstaller means there is nothing to show and nothing to offer
        if (name.Length == 0 || command.Length == 0) return null;
        //runtimes, updates and parts of a bigger program are hidden from the list on purpose
        if ((entry.GetValue("SystemComponent") as int? ?? 0) != 0) return null;
        if (entry.GetValue("ParentKeyName") != null || entry.GetValue("ReleaseType") != null) return null;
        return new InstalledApp { Name = name, Family = Mark + id, Uninstall = command,
            QuietUninstall = entry.GetValue("QuietUninstallString") as string ?? "",
            Publisher = entry.GetValue("Publisher") as string ?? "" };
    }

    // --- removing ------------------------------------------------------------

    //tries every silent way first and verifies each against the registry; only if none of them
    //worked does the maker's own window open, which from then on is the user's to work through.
    //true means the program is really gone, false that its window was left standing
    public static async Task<bool> RemoveAsync(InstalledApp app)
    {
        if (!app.CanRemove) throw new InvalidOperationException(Loc.Get("Apps_Protected"));
        foreach (var (file, arguments) in Silent(app))
        {
            try { await Start(file, arguments, 300000); }
            catch (System.ComponentModel.Win32Exception) { continue; } //Try the next uninstaller.
            if (Gone(app)) return true;
        }
        //cmd resolves an unquoted path with spaces the way the shell would, so we need no parser
        await Start("cmd.exe", "/c \"" + app.Uninstall + "\"", 0);   // step four: the maker's window
        return Gone(app);
    }

    //the ways that need no window, in the order they are worth trying. only the steps this one
    //program can actually take are handed back, so the caller never tries an impossible one
    private static IEnumerable<(string File, string Arguments)> Silent(InstalledApp app)
    {
        if (app.QuietUninstall.Length > 0)          // step one: the maker's own silent switch
            yield return ("cmd.exe", "/c \"" + app.QuietUninstall + "\"");
        if (Product(app.Uninstall) is string code)  // step two: a real msi, not just a guid
            yield return ("msiexec.exe", "/x" + code + " /qn /norestart");
        //winget is asked by the name windows itself lists, since that is what its catalogue matches.
        //an ambiguous or unknown name makes it refuse, which is simply the next step in the chain
        if (Winget() is string winget)              // step three: the package manager, if present
            yield return (winget, "uninstall --name \"" + app.Name.Replace("\"", "") + "\" --exact"
                + " --silent --disable-interactivity --accept-source-agreements --nowarn");
    }

    //the product code of an msi uninstall command, which msiexec can then remove without a window.
    //a guid in the key name proves nothing: a burn bundle carries one too and is an exe
    private static string? Product(string command)
    {
        var match = Regex.Match(command, @"MsiExec(\.exe)?""?\s*/[IX]\s*(\{[0-9A-Fa-f-]{36}\})",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[2].Value : null;
    }

    //winget as a file, not as a name: its entry in WindowsApps is an execution alias that only
    //the shell resolves, and this app starts its processes without one
    private static string? Winget()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps\winget.exe");
        return File.Exists(path) ? path : null;
    }

    //runs one attempt and waits; a silent one gets a timeout because it must never ask anything,
    //the maker's window gets none because the user is working it
    private static async Task Start(string file, string arguments, int timeout)
    {
        var info = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(info) ?? throw new InvalidOperationException(Loc.Get("Apps_Failed"));
        if (timeout == 0) { await Task.Run(() => process.WaitForExit()); return; }
        if (!await Task.Run(() => process.WaitForExit(timeout))) { try { process.Kill(); } catch { } }
    }

    //whether windows has stopped listing this program. the exit code of an uninstaller says nothing,
    //so the same test that put the row on screen is the one that takes it off again
    private static bool Gone(InstalledApp app)
    {
        var id = app.Family.StartsWith(Mark, StringComparison.Ordinal)
            ? app.Family.Substring(Mark.Length) : app.Family;
        foreach (var (hive, view) in Places)
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(Uninstall + "\\" + id);
            if (key?.GetValue("DisplayName") is string name && name.Length > 0) return false;
        }
        return true;
    }
}
