using CommunityToolkit.Mvvm.ComponentModel;
using FluentCleaner.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace FluentCleaner.Views;

// Lists the user's own Winapp2 entries. CustomEntryService does all the file work;
// the enabled ones show up in the cleaner on its next visit.
public sealed partial class CustomPage : Page, IPageActions, ISearchablePage
{
    private readonly ObservableCollection<CustomEntryVm> _entries = [];
    private readonly List<CustomEntryVm> _allEntries = [];   //unfiltered source of truth
    private string _search = "";

    public CustomPage()
    {
        InitializeComponent();
        listCustom.ItemsSource = _entries;
        LoadEntries();
    }

    // --- Loading -----------------------------------------------------------

    private async void LoadEntries()
    {
        var loaded = await Task.Run(() => CustomEntryService.ListFiles().Select(path => new CustomEntryVm
        {
            Name       = CustomEntryService.NameOf(path),
            FilePath   = path,
            EntryCount = CustomEntryService.CountEntries(path),
            IsEnabled  = CustomEntryService.IsEnabled(path)   // safe: IsEnabledChanged not yet subscribed
        }).ToList());

        _allEntries.Clear();
        foreach (var vm in loaded)
        {
            vm.IsEnabledChanged += OnEntryToggled;
            _allEntries.Add(vm);
        }
        ApplyFilter();
    }

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
        foreach (var e in _allEntries.Where(e => q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            _entries.Add(e);
        UpdateUi();
    }

    private void UpdateUi()
    {
        var any = _entries.Count > 0;
        panelEmpty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        panelList.Visibility  = any ? Visibility.Visible   : Visibility.Collapsed;
        lblStatus.Text = any ? ResourceService.Fmt("St_CustomStatus", _entries.Count, _entries.Count(e => e.IsEnabled)) : "";
    }

    private async void NewCleanerHeader_Click(object sender, RoutedEventArgs e) =>
        await ShowEditorAsync(null);

    private void OpenFolderHeader_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(CustomEntryService.CustomDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = CustomEntryService.CustomDir,
            UseShellExecute = true
        });
    }

    // --- IPageActions ----------------------------------------------------------

    public void BuildActions(MenuFlyout flyout)
    {
        // same text as the button
        flyout.AddItem(ResourceService.Get("BtnNewCleaner/Content"), async () => await ShowEditorAsync(null));
        flyout.AddItem(ResourceService.Get("St_CustomMenuOpenFolder"), () =>
        {
            Directory.CreateDirectory(CustomEntryService.CustomDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = CustomEntryService.CustomDir,
                UseShellExecute = true
            });
        });
    }

    // --- Toggle ------------------------------------------------------------

    // Subscribed only after the VMs exist, so loading never renames anything.
    private void OnEntryToggled(object? sender, bool isEnabled)
    {
        if (sender is not CustomEntryVm vm || CustomEntryService.IsEnabled(vm.FilePath) == isEnabled) return;

        try
        {
            vm.FilePath = CustomEntryService.SetEnabled(vm.FilePath, isEnabled);
            UpdateUi();
        }
        catch (Exception ex)
        {
            // the switch goes back to what the file says, once the binding has finished its update
            DispatcherQueue.TryEnqueue(() => vm.IsEnabled = CustomEntryService.IsEnabled(vm.FilePath));
            lblStatus.Text = ResourceService.Fmt("St_CustomToggleFailed", ex.Message);
        }
    }

    // --- New / Edit / Test / Delete ------------------------------------------

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
        if (sender is not MenuFlyoutItem { Tag: CustomEntryVm vm }) return;

        try
        {
            var content = await File.ReadAllTextAsync(vm.FilePath);
            RuleLabPage.Open(vm.Name, content);
        }
        catch (Exception ex) { lblStatus.Text = $"{vm.Name}: {ex.Message}"; }
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

        try { File.Delete(vm.FilePath); }
        catch (Exception ex) { lblStatus.Text = $"{vm.Name}: {ex.Message}"; return; }
        LoadEntries();
    }

    // The dialog saves on its own, so a failed save keeps it open with the text still in it.
    private async Task ShowEditorAsync(CustomEntryVm? existing)
    {
        var dialog = new NewCleanerDialog(existing)
        {
            XamlRoot       = XamlRoot,
            RequestedTheme = ActualTheme
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            LoadEntries();
    }
}

// =============================================================================
// View model for a single card in the custom cleaners list
// Kept in this file intentionally;it exists solely to back the CustomPage UI
// =============================================================================
public sealed class CustomEntryVm : ObservableObject
{
    public required string Name       { get; init; }
    public          string FilePath   { get; set; } = "";
    public          int    EntryCount { get; init; }

    // card visuals: dimming for disabled entries
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
                OnPropertyChanged(nameof(StatusText));     //1 entry · enabled/disabled
                OnPropertyChanged(nameof(EnabledOpacity)); //card dimming
                IsEnabledChanged?.Invoke(this, value);     //file rename .disabled
            }
        }
    }
}
