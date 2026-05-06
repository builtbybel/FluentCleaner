using FluentCleaner.Models;
using System.Text;

namespace FluentCleaner.Services;

//Handles /AUTO and /AUTO /SHUTDOWN command-line flags for silent cleaning without UI interaction.
// Called from App.OnLaunched when the flag is detected; writes a detailed log and exits
public static class SilentRunner
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentCleaner", "auto.log");

    public static async Task RunAsync(bool shutdown)
    {
        var paths = AppSettings.Instance.ResolveDatabasePaths().ToList();
        if (paths.Count == 0)
            paths.Add(Path.Combine(AppContext.BaseDirectory, "Winapp2.ini"));

        var parser    = new Winapp2Parser();
        var detection = new DetectionService();
        var cleaner   = new CleaningService();

        var allEntries = new List<CleanerEntry>();
        foreach (var p in paths)
            allEntries.AddRange(await parser.ParseFileAsync(p));

        allEntries = allEntries
            .DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var saved    = AppSettings.Instance.SelectedEntries;
        var selected = allEntries
            .Where(detection.IsInstalled)
            .Where(e => saved.Count > 0 ? saved.Contains(e.Name) : e.Default)
            .ToList();

        var log          = new StringBuilder();
        int  totalItems  = 0;
        long totalBytes  = 0;

        log.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm}]  FluentCleaner /AUTO{(shutdown ? " /SHUTDOWN" : "")}");
        log.AppendLine(new string('─', 60));

        foreach (var entry in selected)
        {
            var result = await cleaner.AnalyzeAsync(entry);

            if (result.FilesToDelete.Count == 0 && result.RegistryToDelete.Count == 0)
                continue;

            var (count, bytes) = await cleaner.CleanAsync(result);
            totalItems += count;
            totalBytes += bytes;

            log.AppendLine();
            log.AppendLine(entry.Name);

            foreach (var file in result.FilesToDelete)
                log.AppendLine($"  {file}");

            foreach (var reg in result.RegistryToDelete)
                log.AppendLine($"  {reg}");

            log.AppendLine($"  → {count} items · {ScanResult.FormatBytes(bytes)}");
        }

        log.AppendLine();
        log.AppendLine(new string('─', 60));
        log.AppendLine($"Total: {totalItems} items · {ScanResult.FormatBytes(totalBytes)}");
        log.AppendLine();

        await WriteLogAsync(log.ToString());
        await RunPostCleanTasksAsync();

        if (shutdown)
            System.Diagnostics.Process.Start("shutdown.exe", "/s /t 0");

        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    // Runs post-clean commands from settings, if enabled
    private static async Task RunPostCleanTasksAsync()
    {
        if (!AppSettings.Instance.PostCleanEnabled) return;

        var lines = AppSettings.Instance.PostCleanCommands
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "cmd.exe",
                    Arguments       = $"/c {line}",
                    UseShellExecute = false,
                    CreateNoWindow  = true
                });
                if (p is not null) await p.WaitForExitAsync();
            }
            catch { }
        }
    }

    // Writes the log to a file
    private static async Task WriteLogAsync(string content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            await File.AppendAllTextAsync(LogFile, content);
        }
        catch { }
    }
}
