using FluentCleaner.Models;
using FluentCleaner.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCleaner.Views;

public sealed partial class FileLocksmithDialog : ContentDialog
{
    private List<LockedFileItem> _remaining;
    public int  AdditionalRemovedCount { get; private set; }
    public long AdditionalFreedBytes   { get; private set; }
    public IReadOnlyList<LockedFileItem> RemainingLockedFiles => _remaining;

    public FileLocksmithDialog(IReadOnlyList<LockedFileItem> lockedFiles)
    {
        InitializeComponent();
        _remaining = lockedFiles.DistinctBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
        Loaded += async (_, _) => await RefreshLocksAsync();
    }

    private async Task RefreshLocksAsync()
    {
        LoadingBar.Visibility = Visibility.Visible;
        BtnRetryAll.IsEnabled = false;
        BtnScheduleReboot.IsEnabled = false;

        _remaining = _remaining.Where(f => File.Exists(f.FilePath)).ToList();
        UpdateSummaryText();

        if (_remaining.Count == 0)
        {
            ProcessList.ItemsSource = Array.Empty<LockingProcessGroup>();
            EmptyProcessesText.Visibility = Visibility.Collapsed;
            LoadingBar.Visibility = Visibility.Collapsed;
            ShowActionInfo(InfoBarSeverity.Success, "All Locked Files Cleared", "Every previously locked file has been removed or excluded.");
            return;
        }

        var (processes, _) = await FileLocksmithService.FindLockingProcessesAsync(_remaining);
        ProcessList.ItemsSource = processes;
        EmptyProcessesText.Visibility = processes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LoadingBar.Visibility = Visibility.Collapsed;
        BtnRetryAll.IsEnabled = true;
        BtnScheduleReboot.IsEnabled = true;
    }

    private void UpdateSummaryText()
    {
        long totalBytes = _remaining.Sum(f => f.SizeBytes);
        DialogSummaryText.Text = _remaining.Count > 0
            ? $"{_remaining.Count:N0} locked files remaining · {ScanResult.FormatBytes(totalBytes)}"
            : "0 locked files remaining";
    }

    private async void CloseAndDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LockingProcessGroup group }) return;

        LoadingBar.Visibility = Visibility.Visible;
        bool closed = await FileLocksmithService.CloseProcessAsync(group.ProcessId, group.ProcessName);

        int deleted = 0;
        long freed = 0;
        await Task.Run(() =>
        {
            foreach (var item in group.Files)
            {
                if (FileLocksmithService.TryForceDeleteOrTruncate(item.FilePath, out long b))
                {
                    deleted++;
                    freed += b;
                }
            }
        });

        AdditionalRemovedCount += deleted;
        AdditionalFreedBytes   += freed;

        ShowActionInfo(
            deleted > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
            closed ? $"Closed {group.AppDisplayName}" : $"Could not close {group.AppDisplayName}",
            deleted > 0
                ? $"Deleted {deleted:N0} files and reclaimed {ScanResult.FormatBytes(freed)}."
                : "Files remained locked after closing. Try running as administrator or schedule delete on reboot.");

        await RefreshLocksAsync();
    }

    private async void ExcludeLockedFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LockedFileItem item }) return;

        var dir  = Path.GetDirectoryName(item.FilePath);
        var file = Path.GetFileName(item.FilePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;

        var rule = $"FILE|{dir}|{file}";
        var settings = AppSettings.Instance;
        if (!settings.GlobalExclusions.Contains(rule, StringComparer.OrdinalIgnoreCase))
        {
            settings.GlobalExclusions.Add(rule);
            settings.GlobalExclusionsEnabled = true;
            settings.Save();
        }

        _remaining.RemoveAll(r => string.Equals(r.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
        ShowActionInfo(InfoBarSeverity.Informational, "Excluded File", $"Added {file} to Global Exclusions.");
        await RefreshLocksAsync();
    }

    private async void RetryAll_Click(object sender, RoutedEventArgs e)
    {
        LoadingBar.Visibility = Visibility.Visible;
        int deleted = 0;
        long freed = 0;

        await Task.Run(() =>
        {
            foreach (var item in _remaining.ToList())
            {
                if (FileLocksmithService.TryForceDeleteOrTruncate(item.FilePath, out long b))
                {
                    deleted++;
                    freed += b;
                }
            }
        });

        AdditionalRemovedCount += deleted;
        AdditionalFreedBytes   += freed;

        ShowActionInfo(
            deleted > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational,
            "Retry Complete",
            deleted > 0
                ? $"Freed {ScanResult.FormatBytes(freed)} across {deleted:N0} files."
                : "Remaining files are still held open by active processes or Windows services.");

        await RefreshLocksAsync();
    }

    private void ScheduleReboot_Click(object sender, RoutedEventArgs e)
    {
        int queued = FileLocksmithService.ScheduleDeleteOnReboot(_remaining.Select(r => r.FilePath));
        ShowActionInfo(
            queued > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
            queued > 0 ? "Scheduled for Next Reboot" : "Administrator Rights Needed",
            queued > 0
                ? $"Queued {queued:N0} locked files for automatic Windows deletion on next restart."
                : "Windows requires administrator privileges to schedule PendingFileRenameOperations on reboot.");
    }

    private void ShowActionInfo(InfoBarSeverity severity, string title, string message)
    {
        ActionInfoBar.Severity = severity;
        ActionInfoBar.Title    = title;
        ActionInfoBar.Message  = message;
        ActionInfoBar.IsOpen   = true;
    }
}
