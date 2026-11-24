using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentCleaner.Services;   // Loc
using Microsoft.Win32;

namespace FluentCleaner.Plugins.Apps;

//classic programs from the uninstall keys (same as control panel);no registry writes
internal static class ProgramService
{
    //HKLM 64 + 32 bit, HKCU
    private static readonly (RegistryHive Hive, RegistryView View)[] Places = {
        (RegistryHive.LocalMachine, RegistryView.Registry64),
        (RegistryHive.LocalMachine, RegistryView.Registry32),
        (RegistryHive.CurrentUser, RegistryView.Registry64)
    };
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Mark = "prog:";          // so a program id never looks like a package family

    //all programs control panel shows
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
        //32 and 64 bit can have the same key, one row is enough
        return found.GroupBy(app => app.Family, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var app = group.First();
            app.Safety = rules.Classify(app);
            app.Selected = false;
            return app;
        }).ToList();
    }

    //null if control panel hides it too
    private static InstalledApp? Read(RegistryKey? entry, string id)
    {
        if (entry == null) return null;
        var name = entry.GetValue("DisplayName") as string ?? "";
        var command = entry.GetValue("UninstallString") as string ?? "";
        //no name or no uninstaller = skip
        if (name.Length == 0 || command.Length == 0) return null;
        //skip runtimes, updates and sub-parts
        if ((entry.GetValue("SystemComponent") as int? ?? 0) != 0) return null;
        if (entry.GetValue("ParentKeyName") != null || entry.GetValue("ReleaseType") != null) return null;
        return new InstalledApp { Name = name, Family = Mark + id, Uninstall = command,
            QuietUninstall = entry.GetValue("QuietUninstallString") as string ?? "",
            Publisher = entry.GetValue("Publisher") as string ?? "" };
    }

    // --- removing ------------------------------------------------------------

    //silent ways first, checked against the registry each time;if none works, the normal uninstaller opens.
    //true = gone, false = still there
    public static async Task<bool> RemoveAsync(InstalledApp app)
    {
        if (!app.CanRemove) throw new InvalidOperationException(Loc.Get("Apps_Protected"));
        foreach (var (file, arguments) in Silent(app))
        {
            try { await Start(file, arguments, 300000); }
            catch (System.ComponentModel.Win32Exception) { continue; } //Try the next uninstaller.
            if (Gone(app)) return true;
        }
        //cmd handles unquoted paths with spaces, no own parser needed
        await Start("cmd.exe", "/c \"" + app.Uninstall + "\"", 0);   // 4. normal uninstaller with window
        return Gone(app);
    }

    //silent uninstall options, only the ones this program has
    private static IEnumerable<(string File, string Arguments)> Silent(InstalledApp app)
    {
        if (app.QuietUninstall.Length > 0)          // 1. QuietUninstallString
            yield return ("cmd.exe", "/c \"" + app.QuietUninstall + "\"");
        if (Product(app.Uninstall) is string code)  // 2. msi (real msiexec command, not just a guid)
            yield return ("msiexec.exe", "/x" + code + " /qn /norestart");
        //winget by display name;unknown or ambiguous name = it refuses, next step
        if (Winget() is string winget)              // 3. winget, if installed
            yield return (winget, "uninstall --name \"" + app.Name.Replace("\"", "") + "\" --exact"
                + " --silent --disable-interactivity --accept-source-agreements --nowarn");
    }

    //msi product code from the uninstall command.
    //guid in the key name isn't enough, burn bundles have one too and are exe
    private static string? Product(string command)
    {
        var match = Regex.Match(command, @"MsiExec(\.exe)?""?\s*/[IX]\s*(\{[0-9A-Fa-f-]{36}\})",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[2].Value : null;
    }

    //full path, winget is an execution alias and we start without shell
    private static string? Winget()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps\winget.exe");
        return File.Exists(path) ? path : null;
    }

    //silent = timeout, with window = no timeout (user clicks through it)
    private static async Task Start(string file, string arguments, int timeout)
    {
        var info = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(info) ?? throw new InvalidOperationException(Loc.Get("Apps_Failed"));
        if (timeout == 0) { await Task.Run(() => process.WaitForExit()); return; }
        if (!await Task.Run(() => process.WaitForExit(timeout))) { try { process.Kill(); } catch { } }
    }

    //gone from the uninstall keys? exit codes of uninstallers can't be trusted
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
