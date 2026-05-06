using CommunityToolkit.Mvvm.ComponentModel;
using FluentCleaner.Models;
using FluentCleaner.Services;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;

namespace FluentCleaner.ViewModels;

//Terminal backend; pure command dispatcher
// All my domain logic lives in the three Cli*Module classes below.
public partial class CliViewModel : ObservableObject
{
    private readonly CliCleanerModule    _cleaner = new();   // winapp2 clean/analyze/list/categories
    private readonly CliDebloatModule    _appx    = new();   // appx debloater module
    private readonly CliExtensionsModule _tools   = new();   // Extensions\ tool runner

    // Hidden/experimental terminal commands. Keeping them centralized makes it obvious
    // which commands are not regular UI features.
    private static readonly IReadOnlyDictionary<string, string[]> HiddenCommandSuggestions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["backdrop"] = ["mica", "acrylic"]
        };

    [ObservableProperty] public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value) =>
        OnPropertyChanged(nameof(IsNotBusy));

    // everything the terminal has printed so far
    public ObservableCollection<string> Output { get; } = [];

    // --- startup ----------------------------------------------------------------

    public async Task InitAsync()
    {
        IsBusy = true;

        var (dbs, entries) = await _cleaner.InitAsync();
        await _appx.InitAsync();   // pre-load Winappx.ini so appx autocomplete works immediately

        Output.Add("FluentCleaner Terminal online.");
        Output.Add($"Databases: {dbs} active - {entries} detected entries");
        Output.Add(string.Join(", ", new[] { HeaderSpec.WindowsVersion, HeaderSpec.CpuName, HeaderSpec.RamLabel }.Where(s => !string.IsNullOrEmpty(s))));
        Output.Add("Type 'help' for commands.");
        IsBusy = false;
    }

    // --- autocomplete -----------------------------------------------------------

    // Autocomplete for the terminal input box.
    // Splits input into verb + query, picks the right source per verb, returns up to 10
    // full command strings ready to paste back into the input, e.g. "clean Firefox Cache".
    public List<string> GetSuggestions(string input)
    {
        var parts = input.TrimStart().Split(' ', 2);
        var query = parts.Length == 2 ? parts[1] : parts[0];
        if (string.IsNullOrWhiteSpace(query)) return [];

        var prefix = parts.Length == 2 ? parts[0] + " " : "";
        var verb   = parts.Length == 2 ? parts[0].ToLowerInvariant() : "";

        IEnumerable<string> source;

        if (TryGetHiddenCommandSuggestions(verb, query, out var hidden))
            // Hidden/experimental commands (backdrop …);centralized in HiddenCommandSuggestions
            source = hidden;

        else if (verb == "run")
            // "run <tool>"; delegates to CliExtensionsModule (live Extensions\ scan)
            source = _tools.GetSuggestions(query);

        else if (verb == "appx")
        {
            // "appx <sub>" ; delegates to CliDebloatModule (Winappx.ini entries)
            prefix = "appx ";
            source = _appx.GetSuggestions(query);
        }
        else if ((verb is "clean" or "analyze" or "scan") &&
                 query.StartsWith("category ", StringComparison.OrdinalIgnoreCase))
        {
            // "clean/analyze/scan category <name>";suggests category names via CliCleanerModule
            var catQuery = query["category ".Length..];
            prefix = $"{verb} category ";
            source = _cleaner.GetCategorySuggestions(catQuery);
        }
        else
            // Default;suggest winapp2 entry names for clean/analyze/scan/list
            source = _cleaner.GetEntrySuggestions(query);

        return source.Take(10)              // lets cap at 10 suggestions
                     .Select(n => prefix + n)   // prepend verb so the full command is ready to execute, e.g. "clean Firefox Cache"
                     .ToList();
    }

    // Returns suggestions for experimental commands such as "backdrop".
    // Normal cleaner entries and extension tools are handled by GetSuggestions itself.
    private static bool TryGetHiddenCommandSuggestions(string verb, string query, out IEnumerable<string> suggestions)
    {
        if (HiddenCommandSuggestions.TryGetValue(verb, out var values))
        {
            suggestions = values.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        suggestions = [];
        return false;
    }

    // --- command dispatch -------------------------------------------------------

    public async Task ExecuteAsync(string input)
    {
        input = input.Trim();
        if (string.IsNullOrWhiteSpace(input)) return;

        Output.Add($"> {input}");

        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var verb  = parts[0].ToLowerInvariant();
        var arg   = parts.Length > 1 ? parts[1] : "";

        switch (verb)
        {
            case "clean":
            case "analyze":
            case "scan":
            case "list":
            case "categories": await _cleaner.ExecuteAsync(verb, arg, Output, v => IsBusy = v); break;
            case "run":        await _tools.RunAsync(arg, Output, v => IsBusy = v);             break;
            case "tools":      _tools.List(Output);                                             break;
            case "appx":       await _appx.ExecuteAsync(arg, Output, v => IsBusy = v);          break;
            case "theme":      RunTheme(arg);                                                    break;
            case "backdrop":   RunBackdrop(arg);                                                 break;
            case "drives":     RunDrives();                                                      break;
            case "version":    Output.Add($"  FluentCleaner {AppInfo.VersionString}");           break;
            case "clear":      Output.Clear();                                                   break;
            case "help":       RunHelp();                                                        break;
            default:
                Output.Add($"  Unknown command '{verb}'. Type 'help'.");
                break;
        }
    }

    // --- builtins (drives, theme, backdrop, help, version, clear) ---------------

    private void RunDrives()
    {
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
        if (drives.Count == 0) { Output.Add("  No drives found."); return; }

        foreach (var d in drives)
        {
            var used   = d.TotalSize - d.AvailableFreeSpace;
            var pct    = (int)(used * 100.0 / d.TotalSize);
            var filled = pct / 5; // 20-char bar, each block = 5%
            var bar    = new string('█', filled) + new string('░', 20 - filled);
            Output.Add($"  {d.Name[..2],-3} [{bar}]  {ScanResult.FormatBytes(used),9} / {ScanResult.FormatBytes(d.TotalSize),-9}  {pct}%");
        }
    }

    private void RunTheme(string arg)
    {
        var theme = arg.ToLowerInvariant() switch
        {
            "dark"   => "Dark",
            "light"  => "Light",
            "system" => null,
            _        => "unknown"
        };

        if (theme == "unknown")
        {
            Output.Add("  Usage: theme dark | light | system");
            return;
        }

        AppSettings.Instance.Theme = theme;
        AppSettings.Instance.Save();
        (Application.Current as App)?.ApplyTheme(theme);
        Output.Add($"  Theme set to '{arg}'.");
    }

    // Hidden/experimental visual feature. This intentionally lives in Terminal
    // only, so power users can opt in without adding another Settings UI switch.
    private void RunBackdrop(string arg)
    {
        var value = arg.ToLowerInvariant() switch
        {
            "mica"    => "mica",
            "acrylic" => "acrylic",
            _         => ""
        };

        if (value == "")
        {
            Output.Add("  Usage: backdrop mica | acrylic");
            return;
        }

        AppSettings.Instance.Backdrop = value;
        AppSettings.Instance.Save();
        (Application.Current as App)?.ApplyBackdrop(value);
        Output.Add($"  Backdrop set to '{value}'.");
    }

    private void RunHelp()
    {
        foreach (var line in """
              Core
                analyze <entry>            scan a single entry
                analyze selected           scan your Cleaner page selection
                analyze all                scan every installed entry
                analyze category <name>    scan a whole category
                scan                       alias for analyze
                clean <entry>              scan and clean a single entry
                clean selected             clean your Cleaner page selection
                clean all                  clean every installed entry
                clean category <name>      clean a whole category
                list [filter]              list entries
                categories                 list categories

              Tools
                tools                      list available tools
                run <tool> [option]        run a tool

              Debloat
                appx list                  list all installed AppX packages
                appx scan                  list bloatware from Winappx.ini only
                appx remove <name>         remove a specific package
                appx remove all            remove all detected bloatware

              Appearance
                theme dark|light|system    change app theme

              Experimental / hidden flags
                These terminal-only flags have no Settings UI and may change or disappear.
                backdrop mica|acrylic      switch window backdrop effect

              Other
                drives                     show disk usage for all drives
                version                    show app version
                clear                      clear output
            """.Split('\n'))
            Output.Add(line.TrimEnd());
    }
}
