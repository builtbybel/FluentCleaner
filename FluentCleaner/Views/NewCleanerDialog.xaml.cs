using FluentCleaner.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCleaner.Views;

public sealed partial class NewCleanerDialog : ContentDialog
{
    private readonly CustomEntryVm? _existing;

    public NewCleanerDialog(CustomEntryVm? existing)
    {
        InitializeComponent();
        _existing = existing;

        // Localized strings that can't use x:Uid (ContentDialog buttons) or share plain code-side keys.
        PrimaryButtonText       = ResourceService.Get("DlgNewCleanerSave");
        CloseButtonText         = ResourceService.Get("DlgNewCleanerCancel");
        nameBox.Header          = ResourceService.Get("DlgNewCleanerNameHeader");
        nameBox.PlaceholderText = ResourceService.Get("DlgNewCleanerNamePlaceholder");
        promptBox.PlaceholderText = ResourceService.Get("DlgNewCleanerAiPlaceholder");
        contentLabel.Text       = ResourceService.Get("DlgNewCleanerContentLabel");

        Title = existing is null ? ResourceService.Get("DlgNewCleanerTitleNew") : ResourceService.Fmt("DlgNewCleanerTitleEdit", existing.Name);

        // Generation is available when the selected provider has a saved key
        // or its standard environment variable is present.
        aiRow.Visibility = AiExplainer.HasConfiguredKey ? Visibility.Visible : Visibility.Collapsed;

        if (existing is not null)
        {
            nameBox.Text = existing.Name;
            try { contentBox.Text = File.ReadAllText(existing.FilePath); } catch { }
        }
        Input_Changed(this, null);
    }

    // --- Event handlers --------------------------------------------------

    // Save needs a name and some content; any old error goes away once you type.
    private void Input_Changed(object sender, TextChangedEventArgs? e)
    {
        IsPrimaryButtonEnabled = nameBox.Text.Trim().Length > 0 && contentBox.Text.Trim().Length > 0;
        errorText.Visibility   = Visibility.Collapsed;
    }

    // Saves here rather than in the page, so a failed save keeps the dialog open with the text in it.
    private void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = nameBox.Text.Trim();
        string? error = null;
        try
        {
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                error = ResourceService.Get("DlgNewCleanerNameInvalid");
            else if (!CustomEntryService.Save(_existing?.FilePath, name, contentBox.Text.Trim()))
                error = ResourceService.Get("DlgNewCleanerNameTaken");
        }
        catch (Exception ex) { error = ResourceService.Fmt("DlgNewCleanerSaveFailed", ex.Message); }

        if (error is null) return;
        args.Cancel          = true;
        errorText.Text       = error;
        errorText.Visibility = Visibility.Visible;
    }

    private void TemplateBtn_Click(object sender, RoutedEventArgs e) =>
        contentBox.Text = IniTemplate;

    private async void GenerateBtn_Click(object sender, RoutedEventArgs e)
    {
        var desc = promptBox.Text.Trim();
        if (string.IsNullOrEmpty(desc)) return;

        generateBtn.IsEnabled = false;
        generateBtn.Content   = ResourceService.Get("DlgNewCleanerGenerating");
        contentBox.Text       = await AiExplainer.GenerateEntryAsync(desc);
        generateBtn.IsEnabled = true;
        generateBtn.Content   = ResourceService.Get("DlgNewCleanerGenerateBtn");
    }

    // --- Templates -------------------------------------------------------

    private const string IniTemplate =
        "[My App Name]\n" +
        "; Section: groups this entry on the Cleaner page (optional)\n" +
        "Section=Applications\n" +
        "\n" +
        "; DetectFile: entry only appears if this path exists (optional)\n" +
        "; Variables: %LocalAppData%  %AppData%  %Temp%  %WinDir%  %ProgramFiles%  %UserProfile%\n" +
        "DetectFile=%LocalAppData%\\MyApp\\*\n" +
        "\n" +
        "; FileKey: <folder path> | <file pattern> | optional flag\n" +
        ";   flag RECURSE    — delete files in all subfolders too\n" +
        ";   flag REMOVESELF — like RECURSE, also removes empty folders afterwards\n" +
        ";   multiple patterns: *.tmp;*.log\n" +
        "FileKey1=%LocalAppData%\\MyApp\\|*.log\n" +
        "FileKey2=%LocalAppData%\\MyApp\\Cache\\|*.*|RECURSE\n" +
        "FileKey3=%AppData%\\MyApp\\Temp\\|*.tmp;*.bak|REMOVESELF\n" +
        "\n" +
        "; RegKey: <HIVE>\\<SubKey>            — deletes the entire key\n" +
        "; RegKey: <HIVE>\\<SubKey>|ValueName  — deletes only that one value\n" +
        "; Hives: HKCU  HKLM  HKCR  HKU  HKCC\n" +
        "RegKey1=HKCU\\Software\\MyApp\\RecentFiles\n" +
        "RegKey2=HKCU\\Software\\MyApp\\Settings|LastSession";
}
