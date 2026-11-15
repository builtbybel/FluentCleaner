using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCleaner.Services;   // Loc and WorkProgress

namespace FluentCleaner.Plugins.Apps;

//everything about installed apps: the app asks windows through powershell, because that is the
//only way to the appx world. nothing here writes to the registry.
internal static class PackageService
{
    private const string ProgressMark = "__FC_PROGRESS__";
    private const string ErrorMark = "__FC_ERROR__";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    //lists the apps the start menu shows, with the identity and the protection flags of each.
    //appsfolder decides what the user can see, appxpackage says what it really is
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

    //asks windows for the installed apps; a failure becomes the error line above the list, not a crash
    public static async Task<AppScan> ScanAsync(IProgress<WorkProgress>? progress = null)
    {
        progress?.Report(new WorkProgress(0, 0, Loc.Get("Progress_Apps"), indeterminate: true));
        try
        {
            var rules = AppRules.Load();
            //the registry answers even when powershell does not, so these survive a failed inventory
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

    //turns the json into one entry per package family, each with its safety level and its tick
    internal static List<InstalledApp> ParseInventory(string json, AppRules rules)
    {
        var text = json.Trim().TrimStart('﻿');
        //ConvertTo-Json writes a bare object when the array holds exactly one package
        if (text.StartsWith('{')) text = "[" + text + "]";

        var apps = JsonSerializer.Deserialize<List<InstalledApp>>(text, Json)
            ?? throw new InvalidOperationException("The package inventory returned no data.");
        if (apps.Any(app => !ValidFamily(app.Family))) throw new InvalidOperationException("Invalid package identity.");
        //one family can come back as several entries and versions, and is one row to the user
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

    //a family name is letters, digits, dots and one underscore; anything else never reaches a script
    internal static bool ValidFamily(string family) => Regex.IsMatch(family ?? "", @"\A[A-Za-z0-9.-]+_[A-Za-z0-9]+\z");

    //builds the uninstall script for one family: it checks the protection again, removes every
    //package of that family, and then verifies that none of them is left
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

    //uninstalls one app, after asking the rules a second time right before it happens
    public static async Task RemoveAsync(InstalledApp app)
    {
        var rules = AppRules.Load();
        if (!app.CanRemove || rules.Classify(app) == AppSafety.Protected)
            throw new InvalidOperationException(Loc.Get("Apps_Protected"));
        await RunAsync(RemovalScript(app.Family), 180000);
    }

    //runs a script in a hidden powershell and returns its output; the exit code decides, and a
    //process that outstays the timeout is ended rather than waited for
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
        //powershell may write progress to stderr even on success; the wrapper sets the exit code
        await error;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(text.StartsWith(ErrorMark, StringComparison.Ordinal)
                ? text.Substring(ErrorMark.Length).Trim() : Loc.Get("Apps_Failed"));
        return text;
    }

    //reads the output line by line, takes the progress lines out of it and keeps the json intact
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
