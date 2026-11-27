namespace FluentCleaner.Models;

/* One ScanResult per entry always. "Analyze All" just creates a list of these.
   After scanning, im passing it straight into CleaningService.Clean to delete everything. */
public class ScanResult
{
    public CleanerEntry Entry { get; set; } = null!;                        // The entry that was analyzed; used after cleaning for REMOVESELF logic.
    public List<string> FilesToDelete { get; set; } = new();                // Absolute file paths collected during analysis that are safe to delete.
    public Dictionary<string, long> FileSizes { get; set; } = new(StringComparer.OrdinalIgnoreCase); // Size captured during analysis; avoids touching every file again in detail views.
    public List<(string FilePath, long SizeBytes)> LockedFiles { get; set; } = new(); // Files detected as hard-locked during Analyze so File Locksmith can surface them.
    public List<RegistryItemToDelete> RegistryToDelete { get; set; } = new(); // Registry keys/values collected during analysis that are safe to delete.
    public long TotalBytes { get; set; }                                    // Sum of deletable file sizes at scan time, updated incrementally as files are found.
    public long LockedBytes { get; set; }                                   // Sum of locked file sizes discovered during Analyze.
    public string FormattedSize => FormatBytes(TotalBytes);                 // Just the human-readable size string (e.g. "1.2 MB") derived from TotalBytes.

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}

/* Describes a single registry key or value to delete.
   If ValueName is null the entire key tree is removed; otherwise only that value is deleted. */
public class RegistryItemToDelete
{
    public string KeyPath { get; set; } = "";       // Full registry path, e.g. "HKCU\Software\Microsoft\Windows\Foo".
    public string? ValueName { get; set; }          // If set, only this named value under KeyPath is deleted.
    public bool IsDeleteKey => ValueName == null;   // True when the whole key (not just a value) is targeted for deletion.

    public override string ToString() =>
        ValueName != null ? $"{KeyPath} → {ValueName}" : KeyPath;
}
