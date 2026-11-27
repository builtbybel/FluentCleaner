using FluentCleaner.Models;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.IO.Enumeration;
using System.Runtime.InteropServices;

namespace FluentCleaner.Services;

/* Two-phase clean cycle:
   Analyze ; walks FileKeys/RegKeys, builds a deletion list without touching anything
             Locked files (held open without FILE_SHARE_DELETE) are silently skipped, i tried here matching the CCleaner behavior
   Clean   ; takes the completed ScanResult and does the actual deleting. */
public class CleaningService
{
    private readonly PathExpander _expander = new();

    // --- Public api --------------------------------------------------
    public Task<ScanResult> AnalyzeAsync(CleanerEntry entry, IProgress<string>? progress = null, CancellationToken token = default)
        => Task.Run(() => Analyze(entry, progress, token), token);

    public Task<(int count, long bytes)> CleanAsync(ScanResult result, IProgress<string>? progress = null, CancellationToken token = default)
        => Task.Run(() =>
        {
            var outcome = CleanDetailed(result, progress, token);
            return (outcome.RemovedCount, outcome.FreedBytes);
        }, token);

    public Task<CleanOutcome> CleanDetailedAsync(ScanResult result, IProgress<string>? progress = null, CancellationToken token = default)
        => Task.Run(() => CleanDetailed(result, progress, token), token);

    // --- Analyze --------------------------------------------------

    /* Read-only phase. Walks FileKeys and RegKeys, builds the deletion list, touches nothing.
       Locked files get skipped here too;they'd fail at delete time anyway and would just
       inflate the reported size for no reason. */
    private ScanResult Analyze(CleanerEntry entry, IProgress<string>? progress, CancellationToken token = default)
    {
        var result = new ScanResult { Entry = entry };
        var fileExclusions = BuildFileExclusions(entry);
        var registryExclusions = BuildRegistryExclusions(entry);

        // Wrap the caller's progress so every path report is prefixed with the entry name.
        // e.g. "Firefox Cache >>C:\Users\...\Cache\Cache_Data"
        // PrefixedProgress delegates to the original Progress<T> which already captured the
        // UI sync context, so the callback still safely lands on the UI thread.
        IProgress<string>? entryProgress = progress is null ? null
            : new PrefixedProgress(entry.Name, progress);

        // dedup across FileKeys via a set;List.Contains would crawl on big entries
        // (Firefox Caches alone is ~8600 files)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileKey in entry.FileKeys)
        {
            try
            {
                foreach (var file in FindFiles(fileKey, fileExclusions, entryProgress, token))
                {
                    if (!seen.Add(file)) continue;

                    // Skip files that are truly inaccessible (hard lock / no permissions).
                    var size = TryGetDeletableSize(file);
                    if (size < 0) continue;

                    result.FilesToDelete.Add(file);
                    result.TotalBytes += size;
                }
            }
            catch (OperationCanceledException) { throw; }  //cancel must reach the caller, not get swallowed
            catch { }
        }

        foreach (var regKey in entry.RegKeys)
        {
            try { result.RegistryToDelete.AddRange(FindRegistryItems(regKey, registryExclusions)); }
            catch { }
        }

        return result;
    }

    /* Resolves the FileKey path to real directories and yields every matching file.
       Patterns get split here upfront so the tree walk only happens once down below. */
    private IEnumerable<string> FindFiles(FileKeyEntry fileKey, List<ExclusionRule> excluded, IProgress<string>? progress, CancellationToken token = default)
    {
        bool recurse = fileKey.Flag is FileKeyFlag.Recurse or FileKeyFlag.RemoveSelf;

        var patterns = fileKey.Pattern
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var dir in _expander.ResolvePaths(fileKey.Path))
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var f in EnumerateFilesSafe(dir, patterns, excluded, recurse, progress, token))
                if (!IsExcluded(f, excluded) && !IsProtected(f))
                    yield return f;
        }
    }

    /* Walks the tree once; lets the OS match files per pattern (FindFirstFile knows about
       8.3 short-name aliases,we don't). HashSet drops files that match more than one pattern.
       Reparse points skipped;Windows ships with fun traps like
     C:\Users\All Users >> C:\ProgramData >> All Users >>....forever */
    private static IEnumerable<string> EnumerateFilesSafe(string root, string[] patterns, List<ExclusionRule> excluded, bool recurse, IProgress<string>? progress = null, CancellationToken token = default)
    {
        //Whole-root check:skip an excluded branch before touching anything below it
        var scanRoot = root.TrimEnd('\\') + "\\";
        if (excluded.Any(rule => rule.Pattern is null &&
            scanRoot.StartsWith(rule.DirPrefix, StringComparison.OrdinalIgnoreCase)))
            yield break;

        progress?.Report(root);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in patterns)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, p); }
            catch { files = []; }
            foreach (var f in files)
                if (seen.Add(f))   //skip if another pattern already matched this file
                    yield return f;
        }

        if (!recurse) yield break;

        IEnumerable<string> dirs;
        //!FIX!Skip reparse points (junctions & symlinks);Windows ships with traps like
        //C:\Users\All Users >> C:\ProgramData >>> All Users >>...forever ;)
        //Real content is always reachable via the canonical path;no need to follow aliases
        try
        {
            dirs = Directory.EnumerateDirectories(root)
                              .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0);
        }
        catch { yield break; }

        foreach (var sub in dirs)
        {
            token.ThrowIfCancellationRequested(); //one check per folder is enough;no need to go per-file
            foreach (var f in EnumerateFilesSafe(sub, patterns, excluded, recurse: true, progress, token))
                yield return f;
        }
    }

    // Checks whether a registry key/value exists before queuing it for deletion
    private static IEnumerable<RegistryItemToDelete> FindRegistryItems(RegKeyEntry regKey, List<string> exclusions)
    {
        if (IsRegistryPathExcluded(regKey.KeyPath, exclusions))
            yield break;

        var (hive, subKey) = SplitHiveSubKey(regKey.KeyPath);
        using var root = RegistryHelpers.OpenHive(hive);
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

    /* Deletes everything the Analyze phase queued up.
       Files that are in use or already gone get skipped silently;no point in spamming errors. 
     Also returns the count of successfully deleted items and the total bytes freed.*/
    private CleanOutcome CleanDetailed(ScanResult result, IProgress<string>? progress, CancellationToken token = default)
    {
        int count = 0;
        long bytes = 0;
        var lockedFiles = new List<LockedFileItem>();
        var registryExclusions = BuildRegistryExclusions(result.Entry);

        foreach (var file in result.FilesToDelete)
        {
            token.ThrowIfCancellationRequested(); //stop between files so we never delete half an entry
            try
            {
                // A keep-list replaces Winapp2's whole-file delete for known browser stores.
                // CookieService owns the SQL work and blocks unsafe fallback deletion.
                var cookieClean = CookieService.TryCleanProtectedStore(
                    file, AppSettings.Instance.CookieDomainsToKeep);
                if (cookieClean.Handled)
                {
                    if (cookieClean.Succeeded)
                    {
                        count += cookieClean.CookiesRemoved;
                        bytes += cookieClean.BytesFreed;
                        if (cookieClean.CookiesRemoved > 0)
                            progress?.Report(ResourceService.Fmt("Cleaning_CleanedCookies", cookieClean.CookiesRemoved, file));
                    }
                    continue;
                }

                var size = new FileInfo(file).Length;
                try
                {
                    var attrs = File.GetAttributes(file);
                    if ((attrs & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                        File.SetAttributes(file, attrs & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System));

                    File.Delete(file);
                    count++;
                    bytes += size;
                    progress?.Report(ResourceService.Fmt("Prog_Deleted", file));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (TryTruncateFile(file))
                    {
                        count++;
                        bytes += size;
                        progress?.Report(ResourceService.Fmt("Prog_Deleted", file));
                    }
                    else if (File.Exists(file))
                    {
                        lockedFiles.Add(new LockedFileItem(file, size, result.Entry.Name, ex.Message));
                    }
                }
            }
            catch { } //already gone or inaccessible metadata; skip silently
        }

        foreach (var regItem in result.RegistryToDelete)
        {
            try
            {
                if (DeleteRegistryItem(regItem, registryExclusions))
                {
                    count++;
                    progress?.Report(ResourceService.Fmt("Prog_Registry", regItem));
                }
            }
            catch { }
        }

        // REMOVESELF: prune directories that are now empty
        foreach (var fk in result.Entry.FileKeys.Where(fk => fk.Flag == FileKeyFlag.RemoveSelf))
            foreach (var resolved in _expander.ResolvePaths(fk.Path))
                TryPruneEmptyDirs(resolved);

        // Recycle Bin only: desktop icon stayed full after cleaning until you opened it, so tell Explorer
        if (result.FilesToDelete.Any(f => f.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase)))
            SHUpdateRecycleBinIcon();

        return new CleanOutcome(count, bytes, lockedFiles);
    }

    internal static bool TryTruncateFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            fs.SetLength(0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /* Deletes a registry value or key tree. If a REG exclusion sits below the target,
       the tree is cleaned one branch at a time so the protected key stays intact. */
    private static bool DeleteRegistryItem(RegistryItemToDelete item, List<string> exclusions)
    {
        var itemPath = NormalizeRegistryPath(item.KeyPath);
        if (IsRegistryPathExcluded(itemPath, exclusions))
            return false;

        var (hive, subKey) = SplitHiveSubKey(item.KeyPath);
        using var root = RegistryHelpers.OpenHive(hive);
        if (root is null) return false;

        if (item.ValueName is not null)
        {
            using var key = root.OpenSubKey(subKey, writable: true);
            if (key is null) return false;
            key.DeleteValue(item.ValueName, throwOnMissingValue: false);
            return true;
        }

        using (var key = root.OpenSubKey(subKey, writable: false))
            if (key is null) return false;

        var protectedKeys = exclusions
            .Where(path => IsSameOrChild(path, itemPath) && RegistryKeyExists(path))
            .ToList();

        if (protectedKeys.Count == 0)
        {
            var parentSubKey = Path.GetDirectoryName(subKey)?.Replace('/', '\\') ?? "";
            var keyName = Path.GetFileName(subKey);
            using var parent = root.OpenSubKey(parentSubKey, writable: true);
            if (parent is null) return false;
            parent.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
            return true;
        }

        using var target = root.OpenSubKey(subKey, writable: true);
        if (target is null) return false;
        DeleteRegistryTreeExcept(target, itemPath, protectedKeys);
        return true;
    }

    // Removes a key's contents while leaving excluded branches and their parents in place.
    private static void DeleteRegistryTreeExcept(RegistryKey key, string keyPath, List<string> exclusions)
    {
        foreach (var valueName in key.GetValueNames())
            key.DeleteValue(valueName, throwOnMissingValue: false);

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            var childPath = keyPath + "\\" + subKeyName;
            if (IsRegistryPathExcluded(childPath, exclusions))
                continue;

            if (exclusions.Any(path => IsSameOrChild(path, childPath)))
            {
                using var child = key.OpenSubKey(subKeyName, writable: true);
                if (child is not null)
                    DeleteRegistryTreeExcept(child, childPath, exclusions);
            }
            else
            {
                key.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);
            }
        }
    }

    /* Cleans up empty folders left behind by a REMOVESELF clean.
       Order matters: deepest first, so parent directories become empty before we try to delete them.
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

            //Delete the root folder itself if it's now empty
            if (Directory.GetFileSystemEntries(path).Length == 0)
                Directory.Delete(path);
        }
        catch { }
    }

    // --- Helpers --------------------------------------------------

    /* Turns the entry's ExcludeKey lines into rules we can actually match against during the scan.
       REG exclusions are skipped here;they don't apply to file paths anyway.
       Global exclusions from Settings are layered on top,they override everything. */
    private List<ExclusionRule> BuildFileExclusions(CleanerEntry entry)
    {
        var rules = new List<ExclusionRule>();

        // per-entry ExcludeKeys from the INI
        foreach (var ex in entry.ExcludeKeys)
            AddFileRule(ex, rules);

        // app-level exclusions;so this are the paths the user never wants touched, regardless of INI
        var settings = AppSettings.Instance;
        if (settings.GlobalExclusionsEnabled)
            foreach (var line in settings.GlobalExclusions)
                AddFileRule(ExcludeKeyEntry.Parse(line), rules);

        return rules;
    }

    private void AddFileRule(ExcludeKeyEntry ex, List<ExclusionRule> rules)
    {
        if (ex.Type is ExcludeType.Reg) return;
        foreach (var p in _expander.ResolvePaths(ex.Path))
            rules.Add(new ExclusionRule(p.TrimEnd('\\') + "\\", ex.Pattern));
    }

    // Registry exclusions stay separate because they protect key branches, not file paths.
    private static List<string> BuildRegistryExclusions(CleanerEntry entry)
    {
        var paths = entry.ExcludeKeys
            .Where(ex => ex.Type == ExcludeType.Reg)
            .Select(ex => NormalizeRegistryPath(ex.Path))
            .Where(path => path.Length > 0)
            .ToList();

        var settings = AppSettings.Instance;
        if (settings.GlobalExclusionsEnabled)
        {
            paths.AddRange(settings.GlobalExclusions
                .Select(ExcludeKeyEntry.Parse)
                .Where(ex => ex.Type == ExcludeType.Reg)
                .Select(ex => NormalizeRegistryPath(ex.Path))
                .Where(path => path.Length > 0));
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Matches the excluded key itself and every key below it.
    private static bool IsRegistryPathExcluded(string path, List<string> exclusions)
    {
        var normalized = NormalizeRegistryPath(path);
        return exclusions.Any(excluded => IsSameOrChild(normalized, excluded));
    }

    private static bool IsSameOrChild(string path, string parent) =>
        path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);

    private static bool RegistryKeyExists(string path)
    {
        var (hive, subKey) = SplitHiveSubKey(path);
        using var root = RegistryHelpers.OpenHive(hive);
        using var key = root?.OpenSubKey(subKey, writable: false);
        return key is not null;
    }

    private static string NormalizeRegistryPath(string path)
    {
        var (hive, subKey) = SplitHiveSubKey(path.Trim().TrimEnd('\\'));
        hive = hive switch
        {
            "HKEY_CURRENT_USER"   => "HKCU",
            "HKEY_LOCAL_MACHINE"  => "HKLM",
            "HKEY_USERS"          => "HKU",
            "HKEY_CURRENT_CONFIG" => "HKCC",
            "HKEY_CLASSES_ROOT"   => "HKCR",
            _ => hive
        };

        return subKey.Length == 0 ? hive : hive + "\\" + subKey.Trim('\\');
    }

    // Probe whether a file is deletable right now by requesting DELETE access via CreateFileW.
    // If another process holds it open without FILE_SHARE_DELETE, this fails and we skip it.
    // Yes, theres a TOCTOU gap between Analyze and Clean;file state can change in between
    // Worst case: we report a slightly off size or try to delete something that moved. Both are caught silently.
    //The goal here is simply to avoid counting files that are already undeletable right now
    private static long TryGetDeletableSize(string path)
    {
        const uint DELETE = 0x00010000;
        const uint FILE_SHARE_ALL = 0x7;   // Read | Write | Delete
        const uint OPEN_EXISTING = 3;

        using var handle = CreateFileW(path, DELETE, FILE_SHARE_ALL,
                                       IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return -1;   // locked; skip!

        try { return new FileInfo(path).Length; }
        catch { return -1; }
    }

    // True if any rule matches;short-circuits on the first hit
    private static bool IsExcluded(string path, List<ExclusionRule> rules)
    {
        foreach (var rule in rules)
            if (rule.Matches(path))
                return true;
        return false;
    }

    /* Built-in safety net;paths we never delete no matter what the database says.
       These hold data that breaks apps when wiped, so a broad REMOVESELF rule
       shouldn't be able to touch them. Add a line to protect more. */
    private static readonly string[] ProtectedSegments =
    {
        // browser extension databases (1Password, Bitwarden, uBlock filter lists…);
        // normal site storage ("https_...") stays cleanable, only "chrome-extension_" is off-limits
        @"\IndexedDB\chrome-extension_",
    };

    // Same list, read-only;lets the Settings page show users exactly what's protected
    public static IReadOnlyList<string> ProtectedPaths => ProtectedSegments;

    // True when the path sits under one of the protected segments
    private static bool IsProtected(string path) =>
        ProtectedSegments.Any(s => path.Contains(s, StringComparison.OrdinalIgnoreCase));

    // Splits "HKCU\Software\Foo" into ("HKCU", "Software\Foo").
    private static (string hive, string subKey) SplitHiveSubKey(string path)
    {
        var idx = path.IndexOf('\\');
        return idx < 0 ? (path.ToUpperInvariant(), "") : (path[..idx].ToUpperInvariant(), path[(idx + 1)..]);
    }

    // --- Nested Types ---------------------------------------------

    /* One rule parsed from an ExcludeKeyN= line.
       DirPrefix always ends with '\' so "Cache\" doesn't accidentally swallow "CacheExtra\".
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

    /* Tiny wrapper that just prepends the entry name to every progress message.
       The inner Progress<T> already grabbed the UI sync context, so no threading magic needed here;
       this is purely a string-prefix transform. */
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

    [DllImport("shell32.dll")]
    private static extern void SHUpdateRecycleBinIcon();
}
