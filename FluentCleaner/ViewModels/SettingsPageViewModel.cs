using FluentCleaner.Models;
using FluentCleaner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Net.Http;

namespace FluentCleaner.ViewModels;

public record HistoryRow(string Date, string Amount, string Bar, string Items);

public partial class SettingsPageViewModel : ObservableObject
{
    private const string Winapp2Url  = "https://raw.githubusercontent.com/builtbybel/FluentCleaner/master/Winapp2.ini";
    private const string Winapp3Url  = "https://raw.githubusercontent.com/MoscaDotTo/Winapp2/master/Winapp3/Winapp3.ini";
    private const string WinappxUrl  = "https://raw.githubusercontent.com/builtbybel/FluentCleaner/master/Winappx.ini";

    private static string Winapp2LocalPath  => Path.Combine(AppContext.BaseDirectory, "Winapp2.ini");
    private static string Winapp3LocalPath  => Path.Combine(AppContext.BaseDirectory, "Winapp3.ini");
    private static string WinappxLocalPath  => Path.Combine(AppContext.BaseDirectory, "Winappx.ini");

    // --- Observable state -----------------------------------------------------

    // Database toggles
    [ObservableProperty] public partial bool   EnableWinapp2 { get; set; } = true;
    [ObservableProperty] public partial bool   EnableWinapp3 { get; set; }
    [ObservableProperty] public partial bool   EnableWinappx { get; set; } = true;
    [ObservableProperty] public partial bool   Winapp3Available { get; set; }    // Winapp3.ini exists on disk
    [ObservableProperty] public partial bool   Winapp3NotAvailable { get; set; } // inverse; drives the Download button
    [ObservableProperty] public partial bool   WinappxAvailable { get; set; }    // Winappx.ini exists on disk
    [ObservableProperty] public partial bool   WinappxNotAvailable { get; set; } // inverse; drives the Download button
    [ObservableProperty] public partial bool   IsCustomSource { get; set; }      // custom path row has a saved value

    // File-info strings shown below each database row
    [ObservableProperty] public partial string Winapp2Info { get; set; } = "";
    [ObservableProperty] public partial string Winapp3Info { get; set; } = "";
    [ObservableProperty] public partial string WinappxInfo { get; set; } = "";

    // Custom database path
    [ObservableProperty] public partial string CustomPath { get; set; } = "";

    // Post-clean tasks
    [ObservableProperty] public partial bool   PostCleanEnabled  { get; set; }
    [ObservableProperty] public partial string PostCleanCommands { get; set; } = "";

    // History
    [ObservableProperty] public partial bool   CleanHistoryEnabled { get; set; } = true;
    [ObservableProperty] public partial string HistorySummary { get; set; } = "";
    public ObservableCollection<HistoryRow> HistoryRows { get; } = [];

    // Shared
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial bool   IsBusy { get; set; }             // single ring for all downloads
    [ObservableProperty] public partial int    ThemeIndex { get; set; }
    [ObservableProperty] public partial bool   RestartRequired { get; set; }

    private bool _refreshing;

    public SettingsPageViewModel() => Refresh();

    // --- Theme ----------------------------------------------------------------

    partial void OnThemeIndexChanged(int value)
    {
        if (_refreshing) return;
        var theme = value switch { 1 => "Light", 2 => "Dark", _ => "" };
        AppSettings.Instance.Theme = theme;
        AppSettings.Instance.Save();
        (Microsoft.UI.Xaml.Application.Current as App)?.ApplyTheme(theme);
    }

    // --- Database toggles -----------------------------------------------------

    partial void OnEnableWinapp2Changed(bool value)
    {
        if (_refreshing) return;
        AppSettings.Instance.EnableWinapp2 = value;
        AppSettings.Instance.Save();
        RefreshFileInfo();
    }

    partial void OnEnableWinapp3Changed(bool value)
    {
        if (_refreshing) return;
        AppSettings.Instance.EnableWinapp3 = value;
        AppSettings.Instance.Save();
        StatusText = value && !File.Exists(Winapp3LocalPath)
            ? "Winapp3 not downloaded yet — click Download."
            : "";
        RefreshFileInfo();
    }

    partial void OnEnableWinappxChanged(bool value)
    {
        if (_refreshing) return;
        AppSettings.Instance.EnableWinappx = value;
        AppSettings.Instance.Save();
        StatusText = value && !File.Exists(WinappxLocalPath)
            ? "Winappx not downloaded yet — click Download."
            : "";
        RefreshFileInfo();
    }

    partial void OnIsBusyChanged(bool value)
    {
        DownloadLatestCommand.NotifyCanExecuteChanged();
        DownloadWinapp3Command.NotifyCanExecuteChanged();
        DownloadWinappxCommand.NotifyCanExecuteChanged();
    }

    // --- Post-clean tasks -----------------------------------------------------

    // Auto-saves whenever the user edits the text box
    partial void OnPostCleanEnabledChanged(bool value)
    {
        if (_refreshing) return;
        AppSettings.Instance.PostCleanEnabled = value;
        AppSettings.Instance.Save();
    }

    partial void OnPostCleanCommandsChanged(string value)
    {
        if (_refreshing) return;
        AppSettings.Instance.PostCleanCommands = value;
        AppSettings.Instance.Save();
    }

    partial void OnCleanHistoryEnabledChanged(bool value)
    {
        if (_refreshing) return;
        AppSettings.Instance.CleanHistoryEnabled = value;
        AppSettings.Instance.Save();
    }

    // --- Junk Growth Tracker / Clean History -------------------------------------------------------------

    [RelayCommand]
    private void ClearHistory()
    {
        AppSettings.Instance.CleanHistory.Clear();
        AppSettings.Instance.Save();
        BuildHistoryRows();
    }

    private void BuildHistoryRows()
    {
        var history = AppSettings.Instance.CleanHistory;
        HistoryRows.Clear();

        if (history.Count == 0)
        {
            HistorySummary = "no runs recorded yet";
            return;
        }

        var totalFreed = history.Sum(e => e.BytesFreed);
        HistorySummary = $"{history.Count} runs · {ScanResult.FormatBytes(totalFreed)} total freed";

        var maxBytes  = history.Max(e => e.BytesFreed);
        const int w   = 20;

        foreach (var e in history.OrderByDescending(e => e.Date))
        {
            var filled = maxBytes > 0 ? (int)(e.BytesFreed * w / (double)maxBytes) : 0;
            HistoryRows.Add(new HistoryRow(
                Date:   e.Date.ToString("dd.MM.yyyy  HH:mm"),
                Amount: ScanResult.FormatBytes(e.BytesFreed),
                Bar:    new string('█', filled) + new string('░', w - filled),
                Items:  $"{e.ItemsRemoved} items"
            ));
        }
    }

    // --- Refresh --------------------------------------------------------------

    public void Refresh()
    {
        AppSettings.Reload();
        var s = AppSettings.Instance;

        _refreshing = true;

        EnableWinapp2       = s.EnableWinapp2;
        EnableWinapp3       = s.EnableWinapp3;
        EnableWinappx       = s.EnableWinappx;
        Winapp3Available    = File.Exists(Winapp3LocalPath);
        Winapp3NotAvailable = !Winapp3Available;
        WinappxAvailable    = File.Exists(WinappxLocalPath);
        WinappxNotAvailable = !WinappxAvailable;
        CustomPath          = s.CustomWinapp2Path ?? "";
        IsCustomSource      = !string.IsNullOrWhiteSpace(s.CustomWinapp2Path);
        PostCleanEnabled    = s.PostCleanEnabled;
        PostCleanCommands   = s.PostCleanCommands;
        CleanHistoryEnabled = s.CleanHistoryEnabled;
        ThemeIndex          = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };

        _refreshing = false;
        BuildHistoryRows();
        RefreshFileInfo();
    }

    // --- Custom database path -------------------------------------------------

    [RelayCommand]
    private void ApplyCustomPath()
    {
        var path = CustomPath.Trim();
        if (string.IsNullOrEmpty(path))
        {
            AppSettings.Instance.CustomWinapp2Path = null;
            AppSettings.Instance.Save();
            IsCustomSource = false;
            StatusText = "";
            return;
        }
        if (!File.Exists(path)) { StatusText = $"File not found: {path}"; return; }

        AppSettings.Instance.CustomWinapp2Path = path;
        AppSettings.Instance.Save();
        IsCustomSource = true;
        StatusText = "Custom path saved.";
        RefreshFileInfo();
    }

    [RelayCommand]
    private void RemoveCustomPath()
    {
        CustomPath = "";
        AppSettings.Instance.CustomWinapp2Path = null;
        AppSettings.Instance.Save();
        IsCustomSource = false;
        StatusText = "";
        RefreshFileInfo();
    }

    // --- Downloads ------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadLatestAsync()
    {
        await DownloadFileAsync(Winapp2Url, Winapp2LocalPath, "Winapp2");
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadWinapp3Async()
    {
        await DownloadFileAsync(Winapp3Url, Winapp3LocalPath, "Winapp3");
        AppSettings.Instance.EnableWinapp3 = true;
        AppSettings.Instance.Save();
        Refresh(); // picks up EnableWinapp3 = true from disk
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadWinappxAsync()
    {
        await DownloadFileAsync(WinappxUrl, WinappxLocalPath, "Winappx");
        AppSettings.Instance.EnableWinappx = true;
        AppSettings.Instance.Save();
        Refresh(); // picks up EnableWinappx = true from disk
    }

    private bool CanDownload() => !IsBusy;

    private async Task DownloadFileAsync(string url, string destination, string label)
    {
        IsBusy = true;
        StatusText = $"Downloading {label}…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var content = await http.GetStringAsync(url);
            await File.WriteAllTextAsync(destination, content);
            StatusText      = $"{label} downloaded — {content.Length / 1024} KB";
            RestartRequired = true;
        }
        catch (Exception ex) { StatusText = $"Download failed: {ex.Message}"; }
        finally              { IsBusy = false; }
    }

    // --- File info helpers ----------------------------------------------------

    private void RefreshFileInfo()
    {
        Winapp2Info = BuildFileInfo(Winapp2LocalPath);
        Winapp3Info = BuildFileInfo(Winapp3LocalPath);
        WinappxInfo = BuildFileInfo(WinappxLocalPath);
    }

    private static string BuildFileInfo(string path)
    {
        try
        {
            if (!File.Exists(path)) return "Not downloaded";
            var fi    = new FileInfo(path);
            var lines = File.ReadLines(path).Count(l => l.StartsWith('[') && !l.StartsWith("[Winapp2"));
            return $"{lines} entries  •  {fi.Length / 1024} KB  •  {fi.LastWriteTime:yyyy-MM-dd}";
        }
        catch { return ""; }
    }
}
