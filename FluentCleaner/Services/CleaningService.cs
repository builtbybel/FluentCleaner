using System.IO.Enumeration;
using System.Runtime.InteropServices;
using FluentCleaner.Models;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FluentCleaner.Services;

/* Responsible for two phases of the clean cycle:
   Analyze — walks FileKeys and RegKeys to build a list of what can be deleted, without touching anything.
             Locked files (held open without FILE_SHARE_DELETE) are silently skipped, i tried here matching the CCleaner behavior.
   Clean   — takes a completed ScanResult and actually deletes the files and registry entries. */
public class CleaningService
{
    private readonly PathExpander _expander = new();

    // Kicks off an analysis run for a single CleanerEntry in the background.
    public Task<ScanResult> AnalyzeAsync(CleanerEntry entry, IProgress<string>? progress = null) =>
        Task.Run(() => Analyze(entry, progress));

    /* Kicks off a clean run for an already-completed scan result in the background.
       Returns the number of items deleted and the total bytes freed. */
    public Task<(int count, long bytes)> CleanAsync(ScanResult result, IProgress<string>? progress = null) =>
        Task.Run(() => Clean(result, progress));

    /* Walks all FileKeys and RegKeys for one entry, collecting everything that would be deleted.
       Nothing is modified on disk or in the registry during this phase.
       Files that are locked by the OS or another process are skipped entirely;they would not
       be deletable anyway and should not appear in the result count or size. */
    private ScanResult Analyze(CleanerEntry entry, IProgress<string>? progress)
    {
        var result   = new ScanResult { Entry = entry };
        var excluded = BuildExclusions(entry);

        foreach (var fileKey in entry.FileKeys)
        {
            try
            {
                foreach (var file in FindFiles(fileKey, excluded))
                {
                    if (result.FilesToDelete.Contains(file)) continue;

                    // Request DELETE access the same way File.Delete() would.
                    // If the file is held open without FILE_SHARE_DELETE (e.g. system logs,
                    // in-use executables), this returns -1 and we skip it.
                    var size = TryGetDeletableSize(file);
                    if (size < 0) continue;

                    result.FilesToDelete.Add(file);
                    result.TotalBytes += size;
                }
            }
            catch { }
            progress?.Report($"Scanning {entry.Name}…");
        }

        foreach (var regKey in entry.RegKeys)
        {
            try { result.RegistryToDelete.AddRange(FindRegistryItems(regKey)); }
            catch { }
        }

        return result;
    }

    /* Resolves the FileKey path (which may contain wildcards and env vars) to one or more
       concrete directories, then yields every matching file that is not excluded. */
    private IEnumerable<string> FindFiles(FileKeyEntry fileKey, List<ExclusionRule> excluded)
    {
        bool recurse = fileKey.Flag is FileKeyFlag.Recurse or FileKeyFlag.RemoveSelf;

        foreach (var dir in _expander.ResolvePaths(fileKey.Path))
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var pattern in fileKey.Pattern.Split(';', StringSplitOptions.RemoveEmptyEntries))
                foreach (var f in EnumerateFilesSafe(dir, pattern.Trim(), recurse))
                    if (!IsExcluded(f, excluded)) //check if the file is excluded by any rule before yielding it
                        yield return f;
        }
    }

    /* Directory.GetFiles with AllDirectories is atomic; one inaccessible subfolder
       (e.g. INetCache\Low) aborts the entire tree. This walks directory-by-directory
       so a single access denial only skips that one folder. */
    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, bool recurse)
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
            foreach (var f in EnumerateFilesSafe(sub, pattern, recurse: true))
                yield return f;
    }

    /* Checks whether a registry key or value named in a RegKey entry actually exists.
       Yields a deletion descriptor only if it does; no point queueing keys that aren't there. */
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
            // No value name — queue the entire key for deletion.
            yield return new RegistryItemToDelete { KeyPath = regKey.KeyPath };
        }
    }

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
            catch { } // file in use or already gone; skip silently
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

        // REMOVESELF: resolve wildcards first, then prune each matched directory.
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
            key?.DeleteValue(item.ValueName, throwOnMissingValue: false);
        }
        else
        {
            var parentSubKey = Path.GetDirectoryName(subKey)?.Replace('/', '\\') ?? "";
            var keyName      = Path.GetFileName(subKey);
            using var parent = root.OpenSubKey(parentSubKey, writable: true);
            parent?.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    private static long TryGetDeletableSize(string path)
    {
        const uint DELETE        = 0x00010000;
        const uint FILE_SHARE_ALL = 0x7;   // Read | Write | Delete
        const uint OPEN_EXISTING = 3;

        using var handle = CreateFileW(path, DELETE, FILE_SHARE_ALL,
                                       IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return -1;   // locked; now skip!

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
            //      PATH|_Instances\|*     :  every file anywhere under _Instances\
            if (Pattern.Contains('*') || Pattern.Contains('?'))
            {
                var fileName = Path.GetFileName(filePath);
                return FileSystemName.MatchesSimpleExpression(Pattern, fileName, ignoreCase: true);
            }

            // Literal pattern > the file must be a direct child of DirPrefix, not deeper.
            // e.g. FILE|docs\|readme.pdf  →  protects docs\readme.pdf but NOT docs\sub\readme.pdf
            var relativePath = filePath[DirPrefix.Length..];
            return relativePath.Equals(Pattern, StringComparison.OrdinalIgnoreCase);
        }
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
}
