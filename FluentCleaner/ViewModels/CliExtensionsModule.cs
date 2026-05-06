using System.Collections.ObjectModel;
using System.Diagnostics;

namespace FluentCleaner.ViewModels;

//Self-contained CLI module for Extensions\ tool management.
// CliViewModel owns one instance and forwards "tools" and "run ..." commands here
public class CliExtensionsModule
{
    private static readonly string ExtensionsDir =
        Path.Combine(AppContext.BaseDirectory, "Extensions");

    // Live scan — no caching, always reflects the current Extensions\ folder
    private List<string> ToolNames =>
        Directory.Exists(ExtensionsDir)
            ? Directory.GetFiles(ExtensionsDir, "*.ps1")
                       .Select(f => Path.GetFileNameWithoutExtension(f)!)
                       .OrderBy(n => n)
                       .ToList()
            : [];

    // --- Autocomplete -----------------------------------------------------------

    // Returns tool names that contain the query;CliViewModel prepends "run "
    public IEnumerable<string> GetSuggestions(string query) =>
        ToolNames.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase));

    // --- Commands ---------------------------------------------------------------

    // Lists all .ps1 scripts in Extensions\
    public void List(ObservableCollection<string> output)
    {
        var names = ToolNames;
        if (names.Count == 0) { output.Add("  No tools found (Extensions folder missing)."); return; }
        foreach (var n in names) output.Add($"  {n}");
        output.Add($"  — {names.Count} tools.");
    }

    // Runs a .ps1 from Extensions\ and streams its stdout to output.
    // syntax: run <tool>           runs without option
    //         run <tool> <option>  passes option as argument, e.g. "run ChrisTitusApp utility"
    // Progress<T> captures the UI sync context so callbacks land on the right thread.
    public async Task RunAsync(string arg, ObservableCollection<string> output, Action<bool> setBusy)
    {
        if (!Directory.Exists(ExtensionsDir)) { output.Add("  Extensions folder not found."); return; }

        // Tool names can contain spaces ("Power Actions"), so match the longest script name
        // that arg starts with, then treat the remainder as the option argument.
        // e.g. "Power Actions Restart Explorer" >> tool="Power Actions", option="Restart Explorer"
        var allScripts = Directory.GetFiles(ExtensionsDir, "*.ps1");

        var script = allScripts
            .Where(f => arg.StartsWith(Path.GetFileNameWithoutExtension(f)!, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => Path.GetFileNameWithoutExtension(f)!.Length)
            .FirstOrDefault()
            ?? allScripts.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)!
                .Contains(arg.Split(' ')[0], StringComparison.OrdinalIgnoreCase));

        if (script is null) { output.Add($"  Tool '{arg}' not found. Type 'tools' to list available."); return; }

        var toolLen   = Path.GetFileNameWithoutExtension(script)!.Length;
        var optionArg = arg.Length > toolLen ? arg[(toolLen + 1)..].Trim() : null;
        if (string.IsNullOrWhiteSpace(optionArg)) optionArg = null;

        // If the script declares options but none was given, list them and bail
        var options = ReadScriptOptions(script);
        if (optionArg is null && options.Count > 0)
        {
            output.Add($"  '{Path.GetFileNameWithoutExtension(script)}' requires an option:");
            foreach (var o in options) output.Add($"    run {Path.GetFileNameWithoutExtension(script)} {o}");
            return;
        }

        setBusy(true);
        var display = optionArg is not null
            ? $"{Path.GetFileNameWithoutExtension(script)} ({optionArg})"
            : Path.GetFileNameWithoutExtension(script);
        output.Add($"  Running {display}...");

        var extra    = optionArg is not null ? $" \"{optionArg.Replace("\"", "\\\"")}\"" : "";
        var progress = new Progress<string>(line => output.Add("  " + line));
        await Task.Run(() =>
        {
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"{extra}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, ev) => { if (ev.Data is not null) ((IProgress<string>)progress).Report(ev.Data); };
            p.ErrorDataReceived  += (_, ev) => { if (ev.Data is not null) ((IProgress<string>)progress).Report("ERR: " + ev.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
        });

        output.Add("  Done.");
        setBusy(false);
    }

    // --- Helpers ----------------------------------------------------------------

    // Reads "# Options: a;b;c" from the first 15 lines of a .ps1;this is basically the same logic as ToolsPage
    private static List<string> ReadScriptOptions(string scriptPath)
    {
        try
        {
            foreach (var line in File.ReadLines(scriptPath).Take(15))
                if (line.StartsWith("# Options:", StringComparison.OrdinalIgnoreCase))
                    return line[10..].Split(';')
                                     .Select(x => x.Trim())
                                     .Where(x => x.Length > 0)
                                     .ToList();
        }
        catch { }
        return [];
    }
}
