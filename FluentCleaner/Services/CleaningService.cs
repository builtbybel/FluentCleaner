using System.IO.Enumeration;
using System.Runtime.InteropServices;
using FluentCleaner.Models;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FluentCleaner.Services;

/* Two-phase clean cycle:
   Analyze ; walks FileKeys/RegKeys, builds a deletion list without touching anything
             Locked files (held open without FILE_SHARE_DELETE) are silently skipped, i tried here matching the CCleaner behavior
   Clean   ; takes the completed ScanResult and does the actual deleting. */
public class CleaningService
{
    private readonly PathExpander _expander = new();

    // --- Public api --------------------------------------------------
    public Task<ScanResult> AnalyzeAsync(CleanerEntry entry, IProgress<string>? progress = null) =>
        Task.Run(() => Analyze(entry, progress));

    public Task<(int count, long bytes)> CleanAsync(ScanResult result, IProgress<string>? progress = null) =>
        Task.Run(() => Clean(result, progress));

    // --- Analyze --------------------------------------------------

    /* Walks all FileKeys and RegKeys for one entry, collecting everything that would be deleted.
       Nothing is modified on disk or in the registry during this phase.
       Locked files (held open without FILE_SHARE_DELETE) are silently skipped; they would
       fail at delete time anyway and inflate the reported size for no reason. */
    private ScanResult Analyze(CleanerEntry entry, IProgress<string>? progress)
    {
        var result   = new ScanResult { Entry = entry };
        var excluded = BuildExclusions(entry);

        // Wrap the caller's progress so every path report is prefixed with the entry name.
        // e.g. "Firefox Cache  ›  C:\Users\...\Cache\Cache_Data"
        // PrefixedProgress delegates to the original Progress<T> which already captured the
        // UI sync context, so the callback still safely lands on the UI thread.
        IProgress<string>? entryProgress = progress is null ? null
            : new PrefixedProgress(entry.Name, progress);

        foreach (var fileKey in entry.FileKeys)
        {
            try
            {
                foreach (var file in FindFiles(fileKey, excluded, entryProgress))
                {
                    if (result.FilesToDelete.Contains(file)) continue;

                    // open with DELETE access; same as File.Delete() internally.
                    // returns -1 if another process holds it without FILE_SHARE_DELETE, so we skip.
                    var size = TryGetDeletableSize(file);
                    if (size < 0) continue;

                    result.FilesToDelete.Add(file);
                    result.TotalBytes += size;
                }
            }
            catch { }
        }

        foreach (var regKey in entry.RegKeys)
        {
            try { result.RegistryToDelete.AddRange(FindRegistryItems(regKey)); }
            catch { }
        }

        return result;
    }

    /* Resolves the FileKey path (which may contain wildcards and env vars) to one or more
       concrete directories, then yields every matching file that is not excluded.
       Progress is reported once per resolved root directory, then once per subdirectory during recursion. */
    private IEnumerable<string> FindFiles(FileKeyEntry fileKey, List<ExclusionRule> excluded, IProgress<string>? progress)
    {
        bool recurse = fileKey.Flag is FileKeyFlag.Recurse or FileKeyFlag.RemoveSelf;

        foreach (var dir in _expander.ResolvePaths(fileKey.Path))
        {
            if (!Directory.Exists(dir)) continue;

            // Report the root directory once, before iterating all patterns.
            // This avoids N identical reports (one per pattern) for the same folder.
            progress?.Report(dir);

            foreach (var pattern in fileKey.Pattern.Split(';', StringSplitOptions.RemoveEmptyEntries))
                foreach (var f in EnumerateFilesSafe(dir, pattern.Trim(), recurse, progress))
                    if (!IsExcluded(f, excluded))
                        yield return f;
        }
    }

    /* Walks a directory tree safely; one inaccessible folder skips just that folder, not the whole tree
       Progress is forwarded so the caller sees which subdirectory is being scanned live. */
    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, bool recurse, IProgress<string>? progress = null)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, pattern); }
        catch { files = []; }
        foreach (var f in files) yield return f;

        if (!recurse) yield break;

        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root); }
        catch { yield break; }

        foreach (var sub in dirs)
        {
            progress?.Report(sub);
            foreach (var f in EnumerateFilesSafe(sub, pattern, recurse: true, progress))
                yield return f;
        }
    }

    // Checks whether a registry key/value exists before queuing it for deletion
    private static IEnumerable<RegistryItemToDelete> FindRegistryItems(RegKeyEntry regKey)
    {
        var (hive, subKey) = SplitHiveSubKey(regKey.KeyPath);
        using var root = OpenHive(hive);
        if (root is null) yield break;

        using var key = root.OpenSubKey(subKey, writable: false);
        if (key is null) yield break;

        if (regKey.ValueName is not null)
        {
            // Only queue the specific value, not the whole key.
            if (key.GetValue(regKey.ValueName) is not null)
                yield return new RegistryItemToDelete { KeyPath = regKey.KeyPath, ValueName = regKey.ValueName };
        }
        else
        {
            // No value name; queue the entire key for deletion.
            yield return new RegistryItemToDelete { KeyPath = regKey.KeyPath };
        }
    }

    // --- Clean ----------------------------------------------------

    /* Deletes every file and registry entry collected during Analyze.
       Files that are in use or already gone are silently skipped.
       Returns the count of successfully deleted items and the total bytes freed. */
    private (int count, long bytes) Clean(ScanResult result, IProgress<string>? progress)
    {
        int  count = 0;
        long bytes = 0;

        foreach (var file in result.FilesToDelete)
        {
            try
            {
                var size = new FileInfo(file).Length;
                File.Delete(file);
                count++;
                bytes += size;
                progress?.Report($"Deleted: {file}");
            }
            catch { } // in use or already gone; skip silently
                      //catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CleanFail] {file}\n  {ex.GetType().Name}: {ex.Message}"); }
        }

        foreach (var regItem in result.RegistryToDelete)
        {
            try
            {
                DeleteRegistryItem(regItem);
                count++;
                progress?.Report($"Registry: {regItem}");
            }
            catch { }
        }

        // REMOVESELF: prune directories that are now empty
        foreach (var fk in result.Entry.FileKeys.Where(fk => fk.Flag == FileKeyFlag.RemoveSelf))
            foreach (var resolved in _expander.ResolvePaths(fk.Path))
                TryPruneEmptyDirs(resolved);

        return (count, bytes);
    }

    /* Deletes a single registry value or an entire key tree, depending on whether
       ValueName is set. Both paths are no-ops if the target no longer exists. */
    private static void DeleteRegistryItem(RegistryItemToDelete item)
    {
        var (hive, subKey) = SplitHiveSubKey(item.KeyPath);
        using var root = OpenHive(hive);
        if (root is null) return;

        if (item.ValueName is not null)
        {
            using var key = root.OpenSubKey(subKey, writable: true);
            key?.DeleteValue(item.ValueName, throwOnMissingValue: false); //only delete the value, not the whole key
        }
        else
        {
            var parentSubKey = Path.GetDirectoryName(subKey)?.Replace('/', '\\') ?? "";
            var keyName      = Path.GetFileName(subKey);
            using var parent = root.OpenSubKey(parentSubKey, writable: true);
            parent?.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false); // delete the whole key tree; if it's already gone, skip silently
        }
    }

    /* Removes empty directories left behind after a REMOVESELF clean.
       Walks deepest-first so parent directories become empty before we try to delete them.
       The root folder itself is deleted last if it ends up empty too. */
    private static void TryPruneEmptyDirs(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (var sub in Directory.GetDirectories(path, "*", SearchOption.AllDirectories)
                                         .OrderByDescending(d => d.Length))
            {
                if (Directory.GetFileSystemEntries(sub).Length == 0)
                    Directory.Delete(sub);
            }

            // Delete the root folder itself if it's now empty
            if (Directory.GetFileSystemEntries(path).Length == 0)
                Directory.Delete(path);
        }
        catch { }
    }

    // --- Helpers --------------------------------------------------

    /* Builds the list of exclusion rules for a scan.
       Each rule carries a directory prefix and an optional filename pattern.
       REG exclusions are not relevant here and are skipped. */
    private List<ExclusionRule> BuildExclusions(CleanerEntry entry)
    {
        var rules = new List<ExclusionRule>();
        foreach (var ex in entry.ExcludeKeys)
        {
            if (ex.Type is ExcludeType.Reg) continue;
            foreach (var p in _expander.ResolvePaths(ex.Path))
            {
                // Always ensure the prefix ends with '\' so "Cache\" never
                // accidentally matches a sibling folder like "CacheExtra\".
                rules.Add(new ExclusionRule(p.TrimEnd('\\') + "\\", ex.Pattern));
            }
        }
        return rules;
    }

    /* Attempts to open the file with Windows DELETE access, this should exactly the access right
       that File.Delete() requests internally. If another process holds the file open
       without FILE_SHARE_DELETE (e.g. a system service keeping an event log open),
       CreateFileW returns INVALID_HANDLE_VALUE and we return -1 to signal "skip this file".
       On success the handle is closed immediately; the file size is read from FileInfo.
       FileAccess.ReadWrite cannot be used here because it does not map to DELETE. */
    // try to open with DELETE access;if it fails the file is locked, skip it
    private static long TryGetDeletableSize(string path)
    {
        const uint DELETE         = 0x00010000;
        const uint FILE_SHARE_ALL = 0x7;   // Read | Write | Delete
        const uint OPEN_EXISTING  = 3;

        using var handle = CreateFileW(path, DELETE, FILE_SHARE_ALL,
                                       IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return -1;   // locked; skip!

        try { return new FileInfo(path).Length; }
        catch { return -1; }
    }

    // Returns true if the given file path is covered by any exclusion rule.
    private static bool IsExcluded(string path, List<ExclusionRule> rules)
    {
        foreach (var rule in rules)
            if (rule.Matches(path))
                return true;
        return false;
    }

    // Splits "HKCU\Software\Foo" into ("HKCU", "Software\Foo").
    private static (string hive, string subKey) SplitHiveSubKey(string path)
    {
        var idx = path.IndexOf('\\');
        return idx < 0 ? (path.ToUpperInvariant(), "") : (path[..idx].ToUpperInvariant(), path[(idx + 1)..]);
    }

    // Shared with DetectionService; maps hive abbreviations to registry root keys. Yeah, a shared RegistryHelper would be cleaner, but im too lazy here
    internal static RegistryKey? OpenHive(string hive) => hive switch
    {
        "HKCU" or "HKEY_CURRENT_USER"   => Registry.CurrentUser,
        "HKLM" or "HKEY_LOCAL_MACHINE"  => Registry.LocalMachine,
        "HKU"  or "HKEY_USERS"          => Registry.Users,
        "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
        "HKCR" or "HKEY_CLASSES_ROOT"   => Registry.ClassesRoot,
        _ => null
    };

    // --- Nested Types ---------------------------------------------

    /* One exclusion rule built from an ExcludeKeyN= line.
       DirPrefix always ends with '\' so "Cache\" never accidentally matches "CacheExtra\".
       Pattern is the optional filename filter (e.g. "*.db", "readme.pdf").
       No pattern means the entire directory subtree is excluded. */
    private readonly record struct ExclusionRule(string DirPrefix, string? Pattern)
    {
        public bool Matches(string filePath)
        {
            if (!filePath.StartsWith(DirPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            // No pattern > whole directory tree is excluded.
            if (Pattern is null) return true;

            // Wildcard pattern > glob-match against just the filename, covering the whole subtree.
            // e.g. PATH|_Instances\|*.db  : every .db file anywhere under _Instances\
            //      PATH|_Instances\|*     : every file anywhere under _Instances\
            if (Pattern.Contains('*') || Pattern.Contains('?'))
            {
                var fileName = Path.GetFileName(filePath);
                return FileSystemName.MatchesSimpleExpression(Pattern, fileName, ignoreCase: true);
            }

            // Literal pattern > the file must be a direct child of DirPrefix, not deeper.
            // e.g. FILE|docs\|readme.pdf > protects docs\readme.pdf but NOT docs\sub\readme.pdf
            var relativePath = filePath[DirPrefix.Length..];
            return relativePath.Equals(Pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    /* Thin IProgress<string> wrapper that prepends an entry name to every path report.
       The inner Progress<T> already captured the UI sync context, so marshalling is handled
       by it; this wrapper is just a prefix transform, no threading magic needed */
    private sealed class PrefixedProgress(string prefix, IProgress<string> inner) : IProgress<string>
    {
        public void Report(string path) => inner.Report($"{prefix}  ›  {path}");
    }

    // --- P/Invoke -------------------------------------------------

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);
}
