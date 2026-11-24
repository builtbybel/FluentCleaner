using FluentCleaner.Services;
using FluentCleaner.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace FluentCleaner.Views;

// FluentSeed: share your selection or apply someone else's.
// Apply = replace selection + analyze, cleaning is still up to Run Cleaner
public sealed partial class FluentSeedDialog : ContentDialog
{
    // selection before the last apply, as a code (for undo). Gone when the app closes
    private static string? _previous;

    private readonly CleanerPageViewModel _cleaner;
    private readonly string _ownCode;
    private readonly Task<HashSet<uint>> _catalogue;
    private FluentSeed.Resolution? _match;

    public static async Task OpenAsync(CleanerPageViewModel cleaner, XamlRoot root, ElementTheme theme)
    {
        await cleaner.Ready;

        var dialog = new FluentSeedDialog(cleaner) { XamlRoot = root, RequestedTheme = theme };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog._match is { } match)
        {
            _previous = dialog._ownCode;
            cleaner.ApplySelection(match.Found);
            cleaner.AnalyzeCommand.Execute(null);
        }
        else if (result == ContentDialogResult.Secondary && FluentSeed.TryParse(_previous ?? "") is { } previous)
        {
            _previous = null;
            cleaner.ApplySelection(FluentSeed.Resolve(previous, InstalledNames(cleaner), []).Found);
        }
    }

    private static IEnumerable<string> InstalledNames(CleanerPageViewModel cleaner) =>
        cleaner.LoadedEntries.Select(e => e.Name);

    // all ticked entries, also the ones hidden by search (saved selection, not the tree)
    private static List<string> SelectedNames(CleanerPageViewModel cleaner)
    {
        var settings = AppSettings.Instance;
        return cleaner.LoadedEntries
            .Where(e => settings.HasSavedCleanerSelection ? settings.SelectedEntries.Contains(e.Name) : e.Default)
            .Select(e => e.Name)
            .ToList();
    }

    private FluentSeedDialog(CleanerPageViewModel cleaner)
    {
        InitializeComponent();
        _cleaner = cleaner;

        Title = ResourceService.Get("SC_Title");
        PrimaryButtonText = ResourceService.Get("SC_Apply");
        CloseButtonText = ResourceService.Get("SC_Close");
        if (_previous is not null) SecondaryButtonText = ResourceService.Get("SC_Restore");

        Intro.Text = ResourceService.Get("SC_Intro");
        OwnHeader.Text = ResourceService.Get("SC_OwnHeader");
        CopyText.Text = ResourceService.Get("SC_Copy");
        CopyLinkText.Text = ResourceService.Get("SC_CopyLink");
        PasteHeader.Text = ResourceService.Get("SC_PasteHeader");
        PasteBox.PlaceholderText = ResourceService.Get("SC_PastePlaceholder");

        var own = SelectedNames(cleaner);
        _ownCode = FluentSeed.Encode(own);
        OwnCode.Text = _ownCode;
        OwnCodeBox.Visibility = own.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = CopyLinkButton.IsEnabled = own.Count > 0;
        OwnSummary.Text = own.Count > 0
            ? ResourceService.Fmt("SC_OwnSummary", own.Count)
            : ResourceService.Get("SC_OwnEmpty");

        // load in the background, only needed once something is pasted
        _catalogue = FluentSeed.CatalogueAsync(CleanerPageViewModel.DatabasePaths());

        IsPrimaryButtonEnabled = false;
        PasteResult.IsOpen = false;
        Opened += async (_, _) => await PrefillFromClipboardAsync();
    }

    // code in the clipboard? put it in the box. not your own one, that would change nothing
    private async Task PrefillFromClipboardAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;

            var text = await content.GetTextAsync();
            if (FluentSeed.TryParse(text) is { } code
                && !code.SetEquals(FluentSeed.TryParse(_ownCode) ?? [])
                && PasteBox.Text.Length == 0)
                PasteBox.Text = text.Trim();
        }
        catch { /* clipboard held by another app; the box simply stays empty */ }
    }

    // seed.html shows the entries by name. code after the # (never sent to a server).
    // pasting the whole link works too, TryParse finds the code
    private const string SeedPage = "https://builtbybel.github.io/FluentCleaner/seed.html#";

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyOwn(_ownCode, CopyGlyph, CopyText);

    private void CopyLink_Click(object sender, RoutedEventArgs e) => CopyOwn(SeedPage + _ownCode, CopyLinkGlyph, CopyLinkText);

    private static void CopyOwn(string text, FontIcon glyph, TextBlock label)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);

        glyph.Glyph = "";   // checkmark
        label.Text = ResourceService.Get("SC_Copied");
    }

    private async void PasteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = PasteBox.Text;
        _match = null;
        IsPrimaryButtonEnabled = false;

        if (string.IsNullOrWhiteSpace(text)) { PasteResult.IsOpen = false; return; }

        if (FluentSeed.TryParse(text) is not { } code)
        {
            ShowResult(InfoBarSeverity.Error, ResourceService.Get("SC_Invalid"), "");
            return;
        }

        var catalogue = await _catalogue;
        if (PasteBox.Text != text) return;   // typed on while the catalogue loaded

        var match = FluentSeed.Resolve(code, InstalledNames(_cleaner), catalogue);
        var notes = new List<string>();
        if (match.NotInstalled > 0) notes.Add(ResourceService.Fmt("SC_NotInstalled", match.NotInstalled));
        if (match.Unknown > 0) notes.Add(ResourceService.Fmt("SC_Unknown", match.Unknown));

        if (match.Found.Count == 0)
        {
            ShowResult(InfoBarSeverity.Warning, ResourceService.Get("SC_None"), string.Join("\n", notes));
            return;
        }

        var title = ResourceService.Fmt("SC_Match", Math.Min(match.Found.Count, match.Total), match.Total);
        if (_cleaner.IsBusy)
        {
            ShowResult(InfoBarSeverity.Informational, title, ResourceService.Get("SC_Busy"));
            return;
        }

        notes.Add(ResourceService.Get("SC_ApplyNote"));
        ShowResult(match.Unknown > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, title, string.Join("\n", notes));

        _match = match;
        IsPrimaryButtonEnabled = true;
    }

    private void ShowResult(InfoBarSeverity severity, string title, string message)
    {
        PasteResult.Severity = severity;
        PasteResult.Title = title;
        PasteResult.Message = message;
        PasteResult.IsOpen = true;
    }
}
