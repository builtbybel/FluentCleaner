// Imported and adapted from my Winslopr app https://github.com/builtbybel/Winslopr/blob/main/docs/extensions.md
// Jut reused here with namespace changes only.
using FluentCleaner.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace FluentCleaner.Views;

public sealed partial class ToolsPage : Page
{
    private readonly List<ToolsDefinition>              _allTools     = new();
    private readonly ObservableCollection<ToolsDefinition> _visibleTools = new();
    private ToolsDefinition? _selectedTool;

    private ToolsCategory _category    = ToolsCategory.All;
    private string        _searchQuery = "";

    // Folder name next to the exe that holds .ps1 extension scripts
    private static readonly string ExtensionsDir =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Extensions");

    // GitHub URL where the extension pack can be downloaded
    private const string ExtensionsGitHub = "https://github.com/builtbybel/FluentCleaner/releases";

    public ToolsPage()
    {
        InitializeComponent();
        listTools.ItemsSource = _visibleTools;

        foreach (var cat in new[] { "All", "System", "Privacy", "Network", "Apps", "Debloat" })
            comboFilter.Items.Add(cat);
        comboFilter.SelectedIndex = 0;

        ClearDetails();
        LoadToolsAsync();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ApplySearch(sender.Text);
    }

    public void ApplySearch(string query)
    {
        _searchQuery = query ?? "";
        ApplyFilterAndSearch();
    }

    // ---------------- Loading ----------------

    private async void LoadToolsAsync()
    {
        lblStatus.Text = "Loading...";

        _allTools.Clear();
        _visibleTools.Clear();
        ClearDetails();

        // Extensions folder is optional
        if (!Directory.Exists(ExtensionsDir))
        {
            ShowNoFolder();
            return;
        }

        ShowList();

        string[] files = await Task.Run(() => Directory.GetFiles(ExtensionsDir, "*.ps1"));

        var loaded = await Task.Run(() =>
        {
            var list = new List<ToolsDefinition>();
            foreach (var path in files)
            {
                var title = Path.GetFileNameWithoutExtension(path);
                var meta  = ReadMetadataFromScript(path);
                list.Add(new ToolsDefinition(title, PickIconForScript(title), path, meta));
            }
            return list;
        });

        _allTools.AddRange(loaded);
        ApplyFilterAndSearch();

        lblStatus.Text = _allTools.Count == 1 ? "1 extension loaded." : $"{_allTools.Count} extensions loaded.";

        if (_visibleTools.Count > 0)
            listTools.SelectedIndex = 0;
    }

    // ---------------- Filter / Search ----------------

    private void comboFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _category = comboFilter.SelectedItem?.ToString() switch
        {
            "System"  => ToolsCategory.System,
            "Privacy" => ToolsCategory.Privacy,
            "Network" => ToolsCategory.Network,
            "Apps"    => ToolsCategory.Apps,
            "Debloat" => ToolsCategory.Debloat,
            _         => ToolsCategory.All
        };
        ApplyFilterAndSearch();
    }

    private void ApplyFilterAndSearch()
    {
        var q = (_searchQuery ?? "").Trim().ToLowerInvariant();

        var filtered = _allTools
            .Where(t =>
                (_category == ToolsCategory.All || t.Category == _category) &&
                (string.IsNullOrEmpty(q) ||
                 t.Title.ToLowerInvariant().Contains(q) ||
                 t.Description.ToLowerInvariant().Contains(q)))
            .OrderBy(t => t.Title)
            .ToList();

        _visibleTools.Clear();
        foreach (var t in filtered)
            _visibleTools.Add(t);

        if (_visibleTools.Count == 0)
            ClearDetails();
    }

    // ---------------- Selection ----------------

    private void listTools_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SetTool(listTools.SelectedItem as ToolsDefinition);

    // ---------------- Details panel ----------------

    private void SetTool(ToolsDefinition? tool)
    {
        _selectedTool = tool;

        if (tool is null) { ClearDetails(); return; }

        panelPlaceholder.Visibility = Visibility.Collapsed;
        scrollDetails.Visibility    = Visibility.Visible;

        lblIcon.Text        = tool.Icon ?? "";
        lblTitle.Text       = tool.Title ?? "";
        lblDescription.Text = tool.Description ?? "";
        progressRing.IsActive = false;

        // Options dropdown
        comboOptions.Items.Clear();
        if (tool.Options.Count > 0)
        {
            comboOptions.Visibility = Visibility.Visible;
            foreach (var opt in tool.Options)
                comboOptions.Items.Add(opt);
            comboOptions.SelectedIndex = 0;
        }
        else
        {
            comboOptions.Visibility = Visibility.Collapsed;
        }

        // Optional text input
        if (tool.SupportsInput)
        {
            textInput.Visibility     = Visibility.Visible;
            textInput.PlaceholderText = string.IsNullOrWhiteSpace(tool.InputPlaceholder)
                ? "Enter input (e.g. IDs or raw arguments)"
                : tool.InputPlaceholder;
            textInput.Text = "";
        }
        else
        {
            textInput.Visibility = Visibility.Collapsed;
        }

        // Powered-by attribution
        if (!string.IsNullOrWhiteSpace(tool.PoweredByText) && !string.IsNullOrWhiteSpace(tool.PoweredByUrl))
        {
            linkPoweredBy.Content    = tool.PoweredByText.Trim();
            linkPoweredBy.Tag        = tool.PoweredByUrl.Trim();
            linkPoweredBy.Visibility = Visibility.Visible;
        }
        else
        {
            linkPoweredBy.Visibility = Visibility.Collapsed;
        }

        // Help info bar
        bool hasHelp      = tool.Options.Any(o => o.Contains("help", StringComparison.OrdinalIgnoreCase));
        infoHelp.Title    = "Help available";
        btnShowHelp.Content = "Show help";
        infoHelp.IsOpen   = hasHelp;

        btnRun.Visibility       = Visibility.Visible;
        btnUninstall.Visibility = Visibility.Visible;
    }

    private void ClearDetails()
    {
        _selectedTool = null;

        lblIcon.Text        = "";
        lblTitle.Text       = "";
        lblDescription.Text = "";

        comboOptions.Visibility = Visibility.Collapsed;
        comboOptions.Items.Clear();

        textInput.Visibility = Visibility.Collapsed;
        textInput.Text       = "";

        linkPoweredBy.Visibility = Visibility.Collapsed;
        infoHelp.IsOpen          = false;
        progressRing.IsActive    = false;

        btnRun.Visibility       = Visibility.Collapsed;
        btnUninstall.Visibility = Visibility.Collapsed;

        scrollDetails.Visibility    = Visibility.Collapsed;
        panelPlaceholder.Visibility = Visibility.Visible;
    }

    // ---------------- Button handlers ----------------

    private async void btnRun_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTool is null) return;

        if (!File.Exists(_selectedTool.ScriptPath))
        {
            lblStatus.Text = "Script not found: " + _selectedTool.ScriptPath;
            return;
        }

        btnRun.IsEnabled       = false;
        btnUninstall.IsEnabled = false;
        progressRing.IsActive  = true;
        lblStatus.Text         = "Running...";
        txtLog.Text            = "";
        AppendLog($"── {_selectedTool.Title} ──");

        try
        {
            bool useConsole = _selectedTool.UseConsole;
            bool useLog     = _selectedTool.UseLog;

            string? optionArg = null;
            if (comboOptions.Visibility == Visibility.Visible && comboOptions.SelectedItem is not null)
            {
                optionArg = comboOptions.SelectedItem.ToString()!;

                if (optionArg.EndsWith(" (console)", StringComparison.Ordinal))
                { useConsole = true; useLog = false; optionArg = optionArg[..^" (console)".Length].Trim(); }
                else if (optionArg.EndsWith(" (silent)", StringComparison.Ordinal))
                { useConsole = false; useLog = false; optionArg = optionArg[..^" (silent)".Length].Trim(); }
                else if (optionArg.EndsWith(" (log)", StringComparison.Ordinal))
                { useLog = true; useConsole = false; optionArg = optionArg[..^" (log)".Length].Trim(); }
            }

            string? inputArg = null;
            if (_selectedTool.SupportsInput && textInput.Visibility == Visibility.Visible)
            {
                var t = textInput.Text.Trim();
                if (!string.IsNullOrEmpty(t)) inputArg = t;
            }

            var extra = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(optionArg)) extra.Append(' ').Append(QuoteForPs(optionArg));
            if (!string.IsNullOrWhiteSpace(inputArg))  extra.Append(' ').Append(QuoteForPs(inputArg));

            await RunScriptAsync(_selectedTool.ScriptPath, extra.ToString(), useConsole, AppendLog);

            lblStatus.Text = useConsole ? "Opened in console." : "Done.";
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Error: " + ex.Message;
        }
        finally
        {
            progressRing.IsActive  = false;
            btnRun.IsEnabled       = true;
            btnUninstall.IsEnabled = true;
        }
    }

    private async void btnUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTool is null) return;

        if (!File.Exists(_selectedTool.ScriptPath))
        {
            lblStatus.Text = "File already missing.";
            ClearDetails();
            LoadToolsAsync();
            return;
        }

        var dialog = new ContentDialog
        {
            Title             = "Remove script",
            Content           = $"Remove \"{_selectedTool.Title}\" from the Extensions folder?",
            PrimaryButtonText = "Remove",
            CloseButtonText   = "Cancel",
            XamlRoot          = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            File.Delete(_selectedTool.ScriptPath);
            ClearDetails();
            LoadToolsAsync();
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Could not delete: " + ex.Message;
        }
    }

    private void btnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        // Open the Extensions folder if it exists, otherwise open the app folder
        var target = Directory.Exists(ExtensionsDir)
            ? ExtensionsDir
            : AppDomain.CurrentDomain.BaseDirectory;
        try { Process.Start("explorer.exe", target); }
        catch { }
    }

    private void btnGitHub_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(ExtensionsGitHub) { UseShellExecute = true }); }
        catch { }
    }

    // ---------------- Empty state helpers ----------------

    private void ShowNoFolder()
    {
        panelNoFolder.Visibility = Visibility.Visible;
        listTools.Visibility     = Visibility.Collapsed;

        // Hide toolbar + output panel if nothing to filter or run
        borderToolbar.Visibility = Visibility.Collapsed;
        borderOutput.Visibility  = Visibility.Collapsed;
    }

    private void ShowList()
    {
        panelNoFolder.Visibility = Visibility.Collapsed;
        listTools.Visibility     = Visibility.Visible;

        borderToolbar.Visibility = Visibility.Visible;
        borderOutput.Visibility  = Visibility.Visible;
    }

    private async void btnShowHelp_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTool is null) return;
        var helpOpt = _selectedTool.Options
            .FirstOrDefault(o => o.Contains("help", StringComparison.OrdinalIgnoreCase));
        if (helpOpt is null) return;

        txtLog.Text = "";
        AppendLog($"── {_selectedTool.Title} — Help ──");
        await RunScriptAsync(_selectedTool.ScriptPath, " " + QuoteForPs(helpOpt), false, AppendLog);
    }

    private void btnClearLog_Click(object sender, RoutedEventArgs e) =>
        txtLog.Text = "";

    private void linkPoweredBy_Click(object sender, RoutedEventArgs e)
    {
        var url = linkPoweredBy.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    // ---------------- Log output ----------------

    private void AppendLog(string line) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            txtLog.Text += line + "\n";
            txtLog.SelectionStart = txtLog.Text.Length;
        });

    // ---------------- Script execution ----------------

    // onOutput is called live for every stdout/stderr line.
    // Pass AppendLog to stream output into the log panel.
    private static Task RunScriptAsync(string scriptPath, string extraArgs, bool useConsole,
                                       Action<string>? onOutput = null) =>
        Task.Run(() =>
        {
            var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"{extraArgs}";

            if (useConsole)
            {
                Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit " + args)
                    { UseShellExecute = true });
                return;
            }

            var psi = new ProcessStartInfo("powershell.exe", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, ev) => { if (!string.IsNullOrEmpty(ev.Data)) onOutput?.Invoke(ev.Data); };
            p.ErrorDataReceived  += (_, ev) => { if (!string.IsNullOrEmpty(ev.Data)) onOutput?.Invoke("ERR: " + ev.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
        });

    private static string QuoteForPs(string? value)
    {
        if (value is null) return "\"\"";
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    // ---------------- Metadata parsing ----------------

    private static ScriptMeta ReadMetadataFromScript(string scriptPath)
    {
        string description = "No description available.";
        var options        = new List<string>();
        var category       = ToolsCategory.All;
        bool useConsole = false, useLog = false, inputEnabled = false;
        string inputPh = "", poweredByText = "", poweredByUrl = "";

        try
        {
            foreach (var line in File.ReadLines(scriptPath).Take(15))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("# Description:", StringComparison.OrdinalIgnoreCase))
                    description = line[14..].Trim();
                else if (line.StartsWith("# Category:", StringComparison.OrdinalIgnoreCase))
                    category = line[11..].Trim().ToLowerInvariant() switch
                    {
                        "system"  => ToolsCategory.System,
                        "privacy" => ToolsCategory.Privacy,
                        "network" => ToolsCategory.Network,
                        "apps"    => ToolsCategory.Apps,
                        "debloat" => ToolsCategory.Debloat,
                        _         => ToolsCategory.All
                    };
                else if (line.StartsWith("# Options:", StringComparison.OrdinalIgnoreCase))
                    options = line[10..].Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                else if (line.StartsWith("# Host:", StringComparison.OrdinalIgnoreCase))
                {
                    var raw  = line[7..].Trim().ToLowerInvariant();
                    useConsole = raw == "console";
                    useLog     = raw == "log";
                }
                else if (line.StartsWith("# Input:", StringComparison.OrdinalIgnoreCase))
                    inputEnabled = line[8..].Trim().ToLowerInvariant() is "true" or "yes" or "1";
                else if (line.StartsWith("# InputPlaceholder:", StringComparison.OrdinalIgnoreCase))
                    inputPh = line[19..].Trim();
                else if (line.StartsWith("# PoweredBy:", StringComparison.OrdinalIgnoreCase))
                    poweredByText = line[12..].Trim();
                else if (line.StartsWith("# PoweredUrl:", StringComparison.OrdinalIgnoreCase))
                    poweredByUrl = line[13..].Trim();
                else if (line.StartsWith('#') && description == "No description available.")
                    description = line.TrimStart('#').Trim();
            }
        }
        catch { }

        return new ScriptMeta
        {
            Description      = description,
            Options          = options,
            Category         = category,
            UseConsole       = useConsole,
            UseLog           = useLog,
            SupportsInput    = inputEnabled,
            InputPlaceholder = inputPh,
            PoweredByText    = poweredByText,
            PoweredByUrl     = poweredByUrl
        };
    }

    // Maps keywords in script names to emoji icons
    private static readonly Dictionary<string, string> _iconMap = new()
    {
        ["debloat"]     = "🧹", ["network"]     = "🌐",
        ["explorer"]    = "📂", ["update"]      = "🔄",
        ["context"]     = "📋", ["backup"]      = "💾",
        ["security"]    = "🛡️",["performance"] = "⚡",
        ["privacy"]     = "🔒", ["app"]         = "📦",
        ["setup"]       = "⚙️",["restore"]     = "♻️",
        ["cache"]       = "🗑️",["defender"]    = "🛡️",
        ["power"]       = "🔌", ["install"]     = "📥",
        ["boot"]        = "🚀", ["clean"]       = "🧼"
    };

    private static string PickIconForScript(string name)
    {
        name = (name ?? "").ToLowerInvariant();
        foreach (var kv in _iconMap)
            if (name.Contains(kv.Key)) return kv.Value;
        return "🔧";
    }
}
