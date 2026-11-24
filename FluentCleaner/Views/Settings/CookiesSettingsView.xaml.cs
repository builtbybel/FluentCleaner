using FluentCleaner.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluentCleaner.Views.Settings;

// Modern front end for the Cookie Manager. Browser SQL stays inside CookieService.
public sealed partial class CookiesSettingsView : UserControl
{
    private readonly HashSet<string> _detected = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _kept = new(StringComparer.OrdinalIgnoreCase);
    private bool _loading;
    private bool _loadedOnce;

    public CookiesSettingsView()
    {
        InitializeComponent();
        ApplyLocalization();
        LoadFromSettings();
        Loaded += async (_, _) =>
        {
            if (_loadedOnce) return;
            _loadedOnce = true;
            await RefreshDomainsAsync();
        };
    }

    private void ApplyLocalization()
    {
        TitleText.Text = ResourceService.Get("Cookies_Title");
        SubtitleText.Text = ResourceService.Get("Cookies_Subtitle");
        AvailableLabel.Text = ResourceService.Get("Cookies_Available");
        KeptLabel.Text = ResourceService.Get("Cookies_Kept");
        AvailableSearch.PlaceholderText = KeptSearch.PlaceholderText = ResourceService.Get("Cookies_Search");
        RefreshButton.Content = ResourceService.Get("Cookies_Refresh");
        KeepMenuItem.Text = ResourceService.Get("Cookies_MiKeep");
        DeleteMenuItem.Text = ResourceService.Get("Cookies_MiDelete");
        RemoveMenuItem.Text = ResourceService.Get("Cookies_MiRemove");
        ImportMenuItem.Text = ResourceService.Get("Cookies_MiImport");
        ExportMenuItem.Text = ResourceService.Get("Cookies_MiExport");
        ToolTipService.SetToolTip(KeepButton, KeepMenuItem.Text);
        ToolTipService.SetToolTip(RemoveButton, RemoveMenuItem.Text);
    }

    public void ReloadSettings()
    {
        LoadFromSettings();
        RefreshLists();
    }

    private void LoadFromSettings()
    {
        _kept.Clear();
        foreach (var domain in AppSettings.Instance.CookieDomainsToKeep.Select(CookieService.NormalizeDomain))
            if (domain.Length > 0) _kept.Add(domain);
        RefreshLists();
    }

    private void SaveKeepList()
    {
        AppSettings.Instance.CookieDomainsToKeep = _kept
            .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .ToList();
        AppSettings.Instance.Save();
    }

    private async Task RefreshDomainsAsync()
    {
        if (_loading) return;
        _loading = true;
        RefreshButton.IsEnabled = false;
        StatusText.Text = ResourceService.Get("Cookies_Scanning");

        try
        {
            var scan = await Task.Run(CookieService.ScanDomains);
            _detected.Clear();
            foreach (var domain in scan.Domains) _detected.Add(domain);
            RefreshLists();
            StatusText.Text = scan.StoresUnavailable > 0
                ? ResourceService.Fmt("Cookies_StatusUnavailable", scan.Domains.Count, scan.StoresFound, scan.StoresUnavailable)
                : ResourceService.Fmt("Cookies_Status", scan.Domains.Count, scan.StoresFound);
        }
        finally
        {
            _loading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        foreach (var domain in AvailableList.SelectedItems.Cast<string>()) _kept.Add(domain);
        SaveKeepList();
        RefreshLists();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        foreach (var domain in KeptList.SelectedItems.Cast<string>().ToArray()) _kept.Remove(domain);
        SaveKeepList();
        RefreshLists();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var domains = AvailableList.SelectedItems.Cast<string>().ToArray();
        if (_loading || domains.Length == 0) return;
        var kept = _kept.ToArray();

        _loading = true;
        RefreshButton.IsEnabled = false;
        StatusText.Text = ResourceService.Get("Cookies_Scanning");
        var unavailable = 0;
        try { unavailable = await Task.Run(() => CookieService.DeleteDomains(domains, kept)); }
        finally
        {
            _loading = false;
            RefreshButton.IsEnabled = true;
        }

        await RefreshDomainsAsync();
        if (unavailable > 0)
            StatusText.Text = ResourceService.Fmt("Cookies_DeleteUnavailable", unavailable);
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".txt");
        InitializePicker(picker);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            foreach (var line in File.ReadLines(file.Path))
            {
                var value = line.Trim();
                if (value.Length == 0 || value.StartsWith('#') || value.StartsWith(';') || value.StartsWith('['))
                    continue;

                var domain = CookieService.NormalizeDomain(value);
                if (domain.Length > 0) _kept.Add(domain);
            }
            SaveKeepList();
            RefreshLists();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "cookies"
        };
        picker.FileTypeChoices.Add(ResourceService.Get("Cookies_TextFiles"), [".txt"]);
        InitializePicker(picker);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            File.WriteAllLines(file.Path, _kept.OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private static void InitializePicker(object picker)
    {
        var hwnd = WindowNative.GetWindowHandle((Application.Current as App)?.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDomainsAsync();
    private void Search_TextChanged(object sender, TextChangedEventArgs e) => RefreshLists();
    private void Available_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Keep_Click(sender, e);
    private void Kept_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Remove_Click(sender, e);

    // A context click acts on the row under the pointer, never on a stale selection.
    private void CookieList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not ListView list || e.OriginalSource is not FrameworkElement element || element.DataContext is not string domain)
            return;
        if (list.SelectedItems.Contains(domain)) return;
        list.SelectedItems.Clear();
        list.SelectedItem = domain;
    }

    private void AvailableMenu_Opening(object sender, object e)
    {
        var enabled = AvailableList.SelectedItems.Count > 0;
        KeepMenuItem.IsEnabled = DeleteMenuItem.IsEnabled = enabled;
    }

    private void KeptMenu_Opening(object sender, object e)
    {
        RemoveMenuItem.IsEnabled = KeptList.SelectedItems.Count > 0;
        ExportMenuItem.IsEnabled = _kept.Count > 0;
    }

    private void RefreshLists()
    {
        AvailableList.ItemsSource = Filter(_detected.Where(domain => !CookieService.IsProtected(domain, _kept)), AvailableSearch.Text);
        KeptList.ItemsSource = Filter(_kept, KeptSearch.Text);
    }

    private static string[] Filter(IEnumerable<string> domains, string search)
    {
        var filter = search.Trim();
        return domains
            .Where(domain => filter.Length == 0 || domain.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
