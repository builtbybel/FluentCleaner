using CommunityToolkit.Mvvm.ComponentModel;
using FluentCleaner.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace FluentCleaner.Views;

public sealed partial class CustomPage : Page, IPageActions, ISearchablePage
{
    private static string CustomDir => CustomEntryService.CustomDir;

    private readonly ObservableCollection<CustomEntryVm> _entries = [];
    private readonly List<CustomEntryVm> _allEntries = [];   //unfiltered source of truth
    private string _search = "";

    public CustomPage()
    {
        InitializeComponent();
        listCustom.ItemsSource = _entries;
        _ = LoadEntriesAsync();
    }

    // --- Loading -----------------------------------------------------------

    // INI:loads enabled + disabled (toggle controls state, active ones go into Cleaner)
    // PS1:loads only active scripts (no toggle; run directly from here)
    private async Task LoadEntriesAsync()
    {
        _allEntries.Clear();

        var loaded = await Task.Run(() =>
        {
            if (!Directory.Exists(CustomDir))
                return [];

            // INI:both enabled + disabled (toggle controls state)
            // PS1:only active scripts (no toggle, no .disabled concept)
            var allFiles =
                Directory.GetFiles(CustomDir, "*.ini")
                .Concat(Directory.GetFiles(CustomDir, "*.ini.disabled"))
                .Concat(Directory.GetFiles(CustomDir, "*.ps1")
                    .Where(f => !f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(Path.GetFileName);

            var list = new List<CustomEntryVm>();
            foreach (var file in allFiles)
            {
                var isPs1 = file.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
                var isOn  = isPs1 || !file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

                // Strip extension(s): .ini.disabled >> name, .ini >> name, etc.
                var name = isOn
                    ? Path.GetFileNameWithoutExtension(file)
                    : Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));

                var vm = new CustomEntryVm
                {
                    Name        = name,
                    FilePath    = file,
                    IsScript    = isPs1,
                    EntryCount  = isPs1 ? 0 : CountEntries(file),
                    Description = isPs1 ? ReadScriptDescription(file) : null,
                    IsEnabled   = isOn   //safe: IsEnabledChanged not yet subscribed
                };
                list.Add(vm);
            }
            return list;
        });

        // Back on UI thread;wire up events and populate the observable collection
        foreach (var vm in loaded)
        {
            vm.IsEnabledChanged += OnEntryToggled;
            _allEntries.Add(vm);
        }

        ApplyFilter();
    }

    //just a thin synchronous wrapper used after edits/deletes where we're already on the UI thread.
    private void LoadEntries() => _ = LoadEntriesAsync();

    // --- ISearchablePage ----------------------------------------------------------

    public void OnSearch(string text)
    {
        _search = text ?? "";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = _search.Trim();
        _entries.Clear();

        var filtered = string.IsNullOrEmpty(q)
            ? _allEntries
            : _allEntries.Where(e =>
                e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (e.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var e in filtered)
            _entries.Add(e);

        UpdateUi();
    }

    private void UpdateUi()
    {
        var any = _entries.Count > 0;
        panelEmpty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        panelList.Visibility  = any ? Visibility.Visible   : Visibility.Collapsed;

        var iniEntries = _entries.Where(e => !e.IsScript).ToList();
        var scriptCount = _entries.Count(e => e.IsScript);
        var enabledCount = iniEntries.Count(e => e.IsEnabled);

        var parts = new List<string>();
        if (iniEntries.Count > 0) parts.Add(ResourceService.Fmt("St_CustomStatus", iniEntries.Count, enabledCount));
        if (scriptCount > 0)      parts.Add(ResourceService.Fmt("St_CustomStatusScripts", scriptCount));

        lblStatus.Text = parts.Count > 0 ? string.Join(" · ", parts) : "";
    }

    private async void NewCleanerHeader_Click(object sender, RoutedEventArgs e) =>
        await ShowEditorAsync(null);

    private void OpenFolderHeader_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(CustomDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = CustomDir,
            UseShellExecute = true
        });
    }

    // --- IPageActions ----------------------------------------------------------

    public void BuildActions(MenuFlyout flyout)
    {
        var newItem = new MenuFlyoutItem { Text = ResourceService.Get("St_CustomMenuNew") };
        newItem.Click += async (_, _) => await ShowEditorAsync(null);
        flyout.Items.Add(newItem);

        var openFolder = new MenuFlyoutItem { Text = ResourceService.Get("St_CustomMenuOpenFolder") };
        openFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(CustomDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = CustomDir,
                UseShellExecute = true
            });
        };
        flyout.Items.Add(openFolder);

        flyout.Items.Add(new MenuFlyoutSeparator());

        void AddLink(string label, string url)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += async (_, _) => await AppLinks.OpenAsync(url);
            flyout.Items.Add(item);
        }

        AddLink(ResourceService.Get("St_CustomMenuReddit"),    AppLinks.Reddit);
        AddLink(ResourceService.Get("St_CustomMenuNeowin"),    AppLinks.Neowin);
        AddLink(ResourceService.Get("St_CustomMenuDeskmodder"),AppLinks.Deskmodder);
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddLink(ResourceService.Get("St_CustomMenuShare"),     AppLinks.ShareCleaner);
    }

    // counts [SectionHeaders] in the file as a quick proxy for entry count
    private static int CountEntries(string path)
    {
        try { return File.ReadLines(path).Count(l => l.StartsWith('[') && l.EndsWith(']')); }
        catch { return 0; }
    }

    // reads the first non-empty # comment line from a .ps1 file as a short description
    private static string? ReadScriptDescription(string path)
    {
        try
        {
            return File.ReadLines(path)
                       .Select(l => l.Trim())
                       .FirstOrDefault(l => l.StartsWith('#') && l.Length > 1)
                       ?[1..]   //strip the leading #
                       .Trim();
        }
        catch { return null; }
    }

    // --- Toggle ------------------------------------------------------------

    //Fires when the user flips a ToggleSwitch. Safe during load because
    //IsEnabledChanged is subscribed only after the VMs are created
    private void OnEntryToggled(object? sender, bool isEnabled)
    {
        if (sender is not CustomEntryVm vm || vm.IsScript) return;

        var newPath = isEnabled
            ? Path.Combine(CustomDir, vm.Name + ".ini")
            : Path.Combine(CustomDir, vm.Name + ".ini.disabled");

        if (string.Equals(newPath, vm.FilePath, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            File.Move(vm.FilePath, newPath, overwrite: true);
            vm.FilePath = newPath;
            UpdateUi();
        }
        catch (Exception ex)
        {
            lblStatus.Text = ResourceService.Fmt("St_CustomToggleFailed", ex.Message);
        }
    }

    // --- New / Edit / Delete -----------------------------------------------

    private async void BtnNew_Click(object sender, RoutedEventArgs e) =>
        await ShowEditorAsync(null);

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CustomEntryVm vm })
            await ShowEditorAsync(vm);
    }

    // opens the ini in the Rule Lab so you can dry-run it right after building
    private async void TestInLab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: CustomEntryVm vm } || vm.IsScript) return;

        string content;
        try   { content = await File.ReadAllTextAsync(vm.FilePath); }
        catch (Exception ex)
        {
            lblStatus.Text = ResourceService.Fmt("St_CustomScriptError", vm.Name, ex.Message);
            return;
        }

        new RuleLabWindow(vm.Name, content, ActualTheme).Activate();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: CustomEntryVm vm }) return;

        var dialog = new ContentDialog
        {
            XamlRoot          = XamlRoot,
            RequestedTheme    = ActualTheme,
            CornerRadius      = new CornerRadius(8),
            Title             = ResourceService.Fmt("St_CustomDeleteTitle", vm.Name),
            Content           = ResourceService.Get("St_CustomDeleteMessage"),
            PrimaryButtonText = ResourceService.Get("St_CustomDeleteConfirm"),
            CloseButtonText   = ResourceService.Get("St_CustomDeleteCancel"),
            DefaultButton     = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try { File.Delete(vm.FilePath); } catch { }
        LoadEntries();
    }

    private async void RunScript_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CustomEntryVm vm } || !vm.IsScript) return;

        lblStatus.Text = ResourceService.Fmt("St_CustomRunning", vm.Name);
        int exitCode = -1;

        void Report(string line) =>
            DispatcherQueue.TryEnqueue(() => lblStatus.Text = line);

        // Pipe script content via stdin so PowerShell doesn't need -File
        string scriptContent;
        try   { scriptContent = await File.ReadAllTextAsync(vm.FilePath); }
        catch (Exception ex)
        {
            lblStatus.Text = ResourceService.Fmt("St_CustomScriptError", vm.Name, ex.Message);
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(
                "powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command -")
            {
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var p = new System.Diagnostics.Process { StartInfo = psi };
            p.OutputDataReceived += (_, ev) => { if (ev.Data is not null) Report(ev.Data); };
            p.ErrorDataReceived  += (_, ev) => { if (ev.Data is not null) Report("ERR: " + ev.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            // Write the script to stdin and close the stream so PowerShell knows we're done
            await p.StandardInput.WriteAsync(scriptContent);
            p.StandardInput.Close();

            await p.WaitForExitAsync();
            exitCode = p.ExitCode;
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"{vm.Name}: error — {ex.Message}";
            return;
        }

        lblStatus.Text = exitCode == 0
            ? ResourceService.Fmt("St_CustomCompleted", vm.Name)
            : ResourceService.Fmt("St_CustomFailed", vm.Name, exitCode);
    }

    private async Task ShowEditorAsync(CustomEntryVm? existing)
    {
        var dialog = new NewCleanerDialog(existing)
        {
            XamlRoot       = XamlRoot,
            RequestedTheme = ActualTheme
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var name    = dialog.EntryName;
        var content = dialog.EntryContent;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(content)) return;

        var ext = dialog.IsScript ? ".ps1" : ".ini";

        try
        {
            Directory.CreateDirectory(CustomDir);
            if (existing is not null && File.Exists(existing.FilePath))
                File.Delete(existing.FilePath);
            File.WriteAllText(Path.Combine(CustomDir, name + ext), content);
            LoadEntries();
        }
        catch { }
    }
}

// =============================================================================
// View model for a single row in the custom cleaners list
// Kept in this file intentionally;it exists solely to back the CustomPage UI
// =============================================================================
public sealed class CustomEntryVm : ObservableObject
{
    public required string  Name        { get; init; }
    public          string  FilePath    { get; set; } = "";
    public          int     EntryCount  { get; init; }
    public          bool    IsScript    { get; init; }
    public          string? Description { get; init; }

    public Visibility ScriptVisibility => IsScript ? Visibility.Visible   : Visibility.Collapsed;
    public Visibility IniVisibility    => IsScript ? Visibility.Collapsed : Visibility.Visible;

    // card visuals: accent strip + dimming for disabled entries
    public double EnabledOpacity => IsEnabled ? 1.0 : 0.7;

    public string StatusText => EntryCount switch
    {
        0 => ResourceService.Get("St_CustomEntryNone"),
        1 => ResourceService.Fmt("St_CustomEntryOne", ResourceService.Get(IsEnabled ? "St_CustomEntryEnabled" : "St_CustomEntryDisabled")),
        _ => ResourceService.Fmt("St_CustomEntryMany", EntryCount, ResourceService.Get(IsEnabled ? "St_CustomEntryEnabled" : "St_CustomEntryDisabled"))
    };

    public event EventHandler<bool>? IsEnabledChanged;

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                OnPropertyChanged(nameof(StatusText));   //1 entry · enabled/disabled
                OnPropertyChanged(nameof(EnabledOpacity));//card dimming
                IsEnabledChanged?.Invoke(this, value);      //file rename .disabled
            }
        }
    }
}
