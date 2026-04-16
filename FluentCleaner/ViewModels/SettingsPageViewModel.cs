using FluentCleaner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net.Http;

namespace FluentCleaner.ViewModels;

public partial class SettingsPageViewModel : ObservableObject
{
    private const string Winapp2Url  = "https://raw.githubusercontent.com/MoscaDotTo/Winapp2/master/Winapp2.ini";
    private const string Winapp3Url  = "https://raw.githubusercontent.com/MoscaDotTo/Winapp2/master/Winapp3/Winapp3.ini";

    private static string Winapp3LocalPath => Path.Combine(AppContext.BaseDirectory, "Winapp3.ini");

    [ObservableProperty] private int    _databaseSourceIndex;  // 0=default 1=custom 2=winapp3
    [ObservableProperty] private bool   _isDefaultSource = true;
    [ObservableProperty] private bool   _isCustomSource;
    [ObservableProperty] private bool   _isWinapp3Source;
    [ObservableProperty] private string _activePath = "";
    [ObservableProperty] private string _customPath = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _fileInfo   = "";
    [ObservableProperty] private int    _themeIndex;
    [ObservableProperty] private bool   _restartRequired;

    private bool _refreshing;

    public SettingsPageViewModel() => Refresh();

    partial void OnThemeIndexChanged(int value)
    {
        if (_refreshing) return;
        var theme = value switch { 1 => "Light", 2 => "Dark", _ => "" };
        AppSettings.Instance.Theme = theme;
        AppSettings.Instance.Save();
        (Microsoft.UI.Xaml.Application.Current as App)?.ApplyTheme(theme);
    }

    partial void OnDatabaseSourceIndexChanged(int value)
    {
        if (_refreshing) return;

        IsDefaultSource = value == 0;
        IsCustomSource  = value == 1;
        IsWinapp3Source = value == 2;

        switch (value)
        {
            case 0:
                AppSettings.Instance.CustomWinapp2Path = null;
                AppSettings.Instance.Save();
                ActivePath = AppSettings.Instance.ResolveWinapp2Path();
                StatusText = "";
                RefreshFileInfo();
                break;

            case 2:
                if (File.Exists(Winapp3LocalPath))
                {
                    AppSettings.Instance.CustomWinapp2Path = Winapp3LocalPath;
                    AppSettings.Instance.Save();
                    ActivePath = Winapp3LocalPath;
                    StatusText = "";
                    RefreshFileInfo();
                }
                else
                {
                    StatusText = "Winapp3 not downloaded yet — click Download to get started.";
                }
                break;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        DownloadLatestCommand.NotifyCanExecuteChanged();
        DownloadWinapp3Command.NotifyCanExecuteChanged();
    }

    public void Refresh()
    {
        AppSettings.Reload();
        var s = AppSettings.Instance;

        _refreshing = true;

        var custom = s.CustomWinapp2Path ?? "";
        DatabaseSourceIndex = string.IsNullOrEmpty(custom)  ? 0
            : custom.Equals(Winapp3LocalPath, StringComparison.OrdinalIgnoreCase) ? 2
            : 1;

        IsDefaultSource = DatabaseSourceIndex == 0;
        IsCustomSource  = DatabaseSourceIndex == 1;
        IsWinapp3Source = DatabaseSourceIndex == 2;
        CustomPath      = custom;
        ActivePath      = s.ResolveWinapp2Path();
        ThemeIndex      = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };

        _refreshing = false;
        RefreshFileInfo();
    }

    [RelayCommand]
    private void ApplyCustomPath()
    {
        var path = CustomPath.Trim();
        if (string.IsNullOrEmpty(path)) { DatabaseSourceIndex = 0; return; }
        if (!File.Exists(path))         { StatusText = $"File not found: {path}"; return; }

        AppSettings.Instance.CustomWinapp2Path = path;
        AppSettings.Instance.Save();
        Refresh();
        StatusText = "Custom path saved.";
    }

    // Downloads latest Winapp2.ini;available when Default source is active
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadLatestAsync()
    {
        var dest = Path.Combine(AppContext.BaseDirectory, "Winapp2.ini");
        await DownloadFileAsync(Winapp2Url, dest, "Winapp2");
        Refresh();
    }

    // Downloads Winapp3.ini and immediately switches to it
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadWinapp3Async()
    {
        await DownloadFileAsync(Winapp3Url, Winapp3LocalPath, "Winapp3");
        AppSettings.Instance.CustomWinapp2Path = Winapp3LocalPath;
        AppSettings.Instance.Save();
        Refresh();
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

    private void RefreshFileInfo()
    {
        try
        {
            var path = AppSettings.Instance.ResolveWinapp2Path();
            if (!File.Exists(path)) { FileInfo = "File not found."; return; }
            var fi    = new FileInfo(path);
            var lines = File.ReadLines(path).Count(l => l.StartsWith('[') && !l.StartsWith("[Winapp2"));
            FileInfo  = $"{lines} entries  •  {fi.Length / 1024} KB  •  {fi.LastWriteTime:yyyy-MM-dd}";
        }
        catch { FileInfo = ""; }
    }
}
