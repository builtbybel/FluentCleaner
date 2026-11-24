using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCleaner.Services;   // Loc and WorkProgress

namespace FluentCleaner.Plugins.Apps;

//store apps via powershell (only way to get at appx);no registry writes
internal static class PackageService
{
    private const string ProgressMark = "__FC_PROGRESS__";
    private const string ErrorMark = "__FC_ERROR__";


    //apps from the start menu (AppsFolder) + their flags from Get-AppxPackage
    private const string Inventory = @"
$names = @{}
try {
    $shell = New-Object -ComObject Shell.Application
    foreach ($item in $shell.NameSpace('shell:AppsFolder').Items()) {
        $id = [string]$item.Path
        $cut = $id.IndexOf('!')
        if ($cut -gt 0) {
            $family = $id.Substring(0, $cut)
            if (-not $names.ContainsKey($family)) { $names[$family] = [string]$item.Name }
        }
    }
} catch { throw 'Windows AppsFolder could not be read: ' + $_.Exception.Message }
$packages = @(Get-AppxPackage -ErrorAction Stop | Where-Object {
    $names.ContainsKey($_.PackageFamilyName)
})
$index = 0
$apps = @($packages | ForEach-Object {
    $display = $names[$_.PackageFamilyName]
    if (-not $display) { $display = $_.Name }
    [Console]::Out.WriteLine('__FC_PROGRESS__' + $index + '|' + $packages.Count + '|' + ($display -replace '[\r\n]', ' '))
    $index++
    [pscustomobject]@{ Name=$display; Family=$_.PackageFamilyName;
        Publisher=$_.Publisher; IsFramework=[bool]$_.IsFramework;
        IsResourcePackage=[bool]$_.IsResourcePackage; NonRemovable=[bool]$_.NonRemovable }
})
ConvertTo-Json -InputObject $apps -Compress -Depth 3
";

    //programs + store apps;errors go to the line above the list
    public static async Task<AppScan> ScanAsync(IProgress<WorkProgress>? progress = null)
    {
        progress?.Report(new WorkProgress(0, 0, Loc.Get("Progress_Apps"), indeterminate: true));
        try
        {
            var rules = AppRules.Load();
            //registry first, still works when powershell fails
            var apps = ProgramService.Scan(rules);
            var error = "";
            try
            {
                apps.AddRange(ParseInventory(await RunAsync(Inventory, 90000, (done, total, name) =>
                    progress?.Report(new WorkProgress(done, total,
                        Loc.Format("Progress_AppChecking", name, done + 1, total)))), rules));
            }
            catch (Exception ex) { error = ex.Message; }
            apps = apps.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            progress?.Report(new WorkProgress(apps.Count, apps.Count, Loc.Format("Progress_AppsFound", apps.Count)));
            return new AppScan { Apps = apps, Error = error };
        }
        catch (Exception ex) { return new AppScan { Error = ex.Message }; }
    }

    //json -> one app per family
    internal static List<InstalledApp> ParseInventory(string json, AppRules rules)
    {
        var text = json.Trim().TrimStart('﻿');
        //ConvertTo-Json drops the [] when there is only one
        if (text.StartsWith('{')) text = "[" + text + "]";

        var apps = JsonSerializer.Deserialize(text, InventoryJson.Default.ListInstalledApp)
            ?? throw new InvalidOperationException("The package inventory returned no data.");
        if (apps.Any(app => !ValidFamily(app.Family))) throw new InvalidOperationException("Invalid package identity.");
        //same family can come back several times (versions), one row is enough
        return apps.GroupBy(app => app.Family, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var app = group.First();
            app.NonRemovable = group.Any(a => a.NonRemovable);
            app.IsFramework = group.Any(a => a.IsFramework);
            app.IsResourcePackage = group.Any(a => a.IsResourcePackage);
            app.Safety = rules.Classify(app);
            app.Selected = false;
            return app;
        }).OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    //only letters, digits, dots, one underscore;anything else doesn't go into a script
    internal static bool ValidFamily(string family) => Regex.IsMatch(family ?? "", @"\A[A-Za-z0-9.-]+_[A-Za-z0-9]+\z");

    //uninstall script: checks protection again, removes all packages of the family, checks they're gone
    internal static string RemovalScript(string family)
    {
        if (!ValidFamily(family)) throw new ArgumentException("Invalid package family.");
        return "$family = '" + family + @"'
$packages = @(Get-AppxPackage -ErrorAction Stop | Where-Object { $_.PackageFamilyName -eq $family })
if ($packages | Where-Object { $_.NonRemovable -or $_.IsFramework -or $_.IsResourcePackage }) { throw 'This package is protected by Windows.' }
foreach ($package in $packages) { Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop }
if (Get-AppxPackage -ErrorAction Stop | Where-Object { $_.PackageFamilyName -eq $family }) { throw 'The package is still installed.' }
";
    }

    //checks the rules again right before removing
    public static async Task RemoveAsync(InstalledApp app)
    {
        var rules = AppRules.Load();
        if (!app.CanRemove || rules.Classify(app) == AppSafety.Protected)
            throw new InvalidOperationException(Loc.Get("Apps_Protected"));
        await RunAsync(RemovalScript(app.Family), 180000);
    }

    //hidden powershell, returns stdout;exit code != 0 = error, timeout = kill
    internal static async Task<string> RunAsync(string script, int timeout,
        Action<int, int, string>? onProgress = null)
    {
        var wrapped = "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false); $ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; try {\n"
            + script + "\n} catch { [Console]::Out.WriteLine('" + ErrorMark + "' + $_.Exception.Message); exit 1 }";
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");
        using var process = new Process { StartInfo = new ProcessStartInfo(executable,
            "-NoLogo -NoProfile -NonInteractive -OutputFormat Text -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped)))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
        process.Start();
        var output = ReadOutputAsync(process.StandardOutput, onProgress);
        var error = process.StandardError.ReadToEndAsync();
        var exited = Task.Run(() => process.WaitForExit(timeout));
        if (!await exited)
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException(Loc.Get("Apps_Timeout"));
        }
        var text = await output;
        //stderr ignored, powershell writes stuff there even on success
        await error;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(text.StartsWith(ErrorMark, StringComparison.Ordinal)
                ? text.Substring(ErrorMark.Length).Trim() : Loc.Get("Apps_Failed"));
        return text;
    }

    //filters out the progress lines, rest is the json
    private static async Task<string> ReadOutputAsync(StreamReader reader, Action<int, int, string>? onProgress)
    {
        var result = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (onProgress != null && line.StartsWith(ProgressMark, StringComparison.Ordinal))
            {
                var parts = line.Substring(ProgressMark.Length).Split(new[] { '|' }, 3);
                if (parts.Length == 3 && int.TryParse(parts[0], out var done)
                    && int.TryParse(parts[1], out var total))
                { onProgress(done, total, parts[2]); continue; }
            }
            result.AppendLine(line);
        }
        return result.ToString();
    }
}
