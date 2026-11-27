using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FluentCleaner.Services;

public sealed record LockedFileItem(string FilePath, long SizeBytes, string EntryName, string Reason);

public sealed class LockingProcessGroup
{
    public int ProcessId { get; }
    public string ProcessName { get; }
    public string AppDisplayName { get; }
    public bool IsSystemProtected { get; }
    public List<LockedFileItem> Files { get; }

    public LockingProcessGroup(
        int processId,
        string processName,
        string appDisplayName,
        bool isSystemProtected,
        List<LockedFileItem> files)
    {
        ProcessId = processId;
        ProcessName = processName;
        AppDisplayName = appDisplayName;
        IsSystemProtected = isSystemProtected;
        Files = files;
    }

    public int    FileCount => Files.Count;
    public long   TotalBytes => Files.Sum(f => f.SizeBytes);
    public string SummaryText => $"{FileCount:N0} locked {(FileCount == 1 ? "file" : "files")} · {FluentCleaner.Models.ScanResult.FormatBytes(TotalBytes)} · PID {ProcessId}";
    public bool   CanClose => !IsSystemProtected;
}

public sealed record CleanOutcome(int RemovedCount, long FreedBytes, List<LockedFileItem> LockedFiles);

/// <summary>
/// PowerToys File Locksmith-style locked-file analyzer and unlocker.
/// Uses native Windows Restart Manager (rstrtmgr.dll) and MoveFileExW (MOVEFILE_DELAY_UNTIL_REBOOT).
/// 100% NativeAOT-safe with zero external drivers or third-party dependencies.
/// </summary>
public static class FileLocksmithService
{
    // Critical Windows OS processes that must never be terminated by a cleaner
    private static readonly HashSet<string> ProtectedSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsm",
        "dwm", "fontdrvhost", "sihost", "svchost", "explorer", "ctfmon",
        "RuntimeBroker", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchHost", "SearchIndexer", "MsMpEng", "NisSrv", "SecurityHealthService"
    };

    /// <summary>
    /// Identifies all processes currently holding locks on the supplied file paths using Windows Restart Manager.
    /// Batched in chunks of 64 paths to stay well within Restart Manager resource limits.
    /// </summary>
    public static Task<(List<LockingProcessGroup> Processes, List<LockedFileItem> UnattributedFiles)> FindLockingProcessesAsync(
        IReadOnlyList<LockedFileItem> lockedFiles,
        CancellationToken token = default)
        => Task.Run(() => FindLockingProcessesInternal(lockedFiles, token), token);

    private static (List<LockingProcessGroup> Processes, List<LockedFileItem> UnattributedFiles) FindLockingProcessesInternal(
        IReadOnlyList<LockedFileItem> lockedFiles,
        CancellationToken token)
    {
        var byPid = new Dictionary<int, (string ProcName, string DisplayName, bool IsProtected, List<LockedFileItem> Files)>();
        var unattributed = new List<LockedFileItem>();

        const int batchSize = 64;
        for (int i = 0; i < lockedFiles.Count; i += batchSize)
        {
            token.ThrowIfCancellationRequested();
            var batch = lockedFiles.Skip(i).Take(batchSize).ToList();
            var pidsForBatch = QueryRestartManagerBatch(batch.Select(b => b.FilePath).ToArray());

            if (pidsForBatch.Count == 0)
            {
                unattributed.AddRange(batch);
                continue;
            }

            // If batch has multiple files and multiple processes, refine per-file for smaller batches
            if (batch.Count > 1 && pidsForBatch.Count > 1 && batch.Count <= 24)
            {
                foreach (var item in batch)
                {
                    token.ThrowIfCancellationRequested();
                    var singlePids = QueryRestartManagerBatch([item.FilePath]);
                    if (singlePids.Count == 0)
                    {
                        unattributed.Add(item);
                        continue;
                    }
                    foreach (var p in singlePids)
                        AddLockedItem(byPid, p.Pid, p.ProcName, p.DisplayName, item);
                }
            }
            else
            {
                foreach (var item in batch)
                    foreach (var p in pidsForBatch)
                        AddLockedItem(byPid, p.Pid, p.ProcName, p.DisplayName, item);
            }
        }

        var groups = byPid
            .Select(kvp => new LockingProcessGroup(
                kvp.Key,
                kvp.Value.ProcName,
                kvp.Value.DisplayName,
                kvp.Value.IsProtected,
                kvp.Value.Files.DistinctBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(g => g.IsSystemProtected)
            .ThenByDescending(g => g.TotalBytes)
            .ToList();

        return (groups, unattributed);
    }

    private static void AddLockedItem(
        Dictionary<int, (string ProcName, string DisplayName, bool IsProtected, List<LockedFileItem> Files)> map,
        int pid,
        string procName,
        string displayName,
        LockedFileItem item)
    {
        if (!map.TryGetValue(pid, out var entry))
        {
            bool isProtected = pid <= 4 || ProtectedSystemProcesses.Contains(procName);
            entry = (procName, string.IsNullOrWhiteSpace(displayName) ? procName : displayName, isProtected, new List<LockedFileItem>());
            map[pid] = entry;
        }
        entry.Files.Add(item);
    }

    /// <summary>
    /// Gracefully closes a user application process (CloseMainWindow -> 2.5s wait -> Kill tree).
    /// Refuses to terminate protected Windows OS processes.
    /// </summary>
    public static async Task<bool> CloseProcessAsync(int pid, CancellationToken token = default)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (ProtectedSystemProcesses.Contains(proc.ProcessName))
                return false;

            try
            {
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    proc.CloseMainWindow();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    cts.CancelAfter(TimeSpan.FromMilliseconds(2500));
                    try { await proc.WaitForExitAsync(cts.Token); } catch { }
                }
            }
            catch { }

            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts2.CancelAfter(TimeSpan.FromMilliseconds(3000));
                try { await proc.WaitForExitAsync(cts2.Token); } catch { }
            }

            // Brief settle time for Windows kernel handle release
            await Task.Delay(250, token);
            return proc.HasExited;
        }
        catch (ArgumentException)
        {
            return true; // already exited
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts attribute-cleared deletion and shared-handle truncation on a locked file.
    /// </summary>
    public static bool TryForceDeleteOrTruncate(string path, out long freedBytes)
    {
        freedBytes = 0;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return true;

        long size = 0;
        try { size = new FileInfo(path).Length; } catch { }

        try
        {
            ClearRestrictiveAttributes(path);
            File.Delete(path);
            freedBytes = size;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (CleaningService.TryTruncateFile(path))
            {
                freedBytes = size;
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clears ReadOnly, Hidden, and System file attributes so File.Delete does not fail with UnauthorizedAccessException.
    /// </summary>
    public static void ClearRestrictiveAttributes(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            const FileAttributes restrictive = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;
            if ((attrs & restrictive) != 0 && (attrs & FileAttributes.ReparsePoint) == 0)
                File.SetAttributes(path, attrs & ~restrictive);
        }
        catch { }
    }

    /// <summary>
    /// Schedules stubborn OS-locked files for deletion on the next Windows reboot via MoveFileExW.
    /// Returns the number of files successfully queued. (Requires elevation for system-owned paths.)
    /// </summary>
    public static int ScheduleDeleteOnReboot(IEnumerable<string> paths)
    {
        int scheduled = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            ClearRestrictiveAttributes(path);
            if (MoveFileExW(path, null, MOVEFILE_DELAY_UNTIL_REBOOT))
                scheduled++;
        }
        return scheduled;
    }

    // --- Windows Restart Manager P/Invoke (rstrtmgr.dll) ----------------------

    private const int ERROR_MORE_DATA = 234;
    private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    private enum RM_APP_TYPE
    {
        RmUnknownApp  = 0,
        RmMainWindow  = 1,
        RmOtherWindow = 2,
        RmService     = 3,
        RmExplorer    = 4,
        RmConsole     = 5,
        RmCritical    = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;
        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFileNames,
        uint nApplications,
        IntPtr rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

    private static List<(int Pid, string ProcName, string DisplayName)> QueryRestartManagerBatch(string[] filePaths)
    {
        var results = new List<(int Pid, string ProcName, string DisplayName)>();
        if (filePaths.Length == 0) return results;

        var sessionKey = Guid.NewGuid().ToString("N");
        if (RmStartSession(out uint handle, 0, sessionKey) != 0)
            return results;

        try
        {
            if (RmRegisterResources(handle, (uint)filePaths.Length, filePaths, 0, IntPtr.Zero, 0, null) != 0)
                return results;

            uint pnProcInfoNeeded = 0;
            uint pnProcInfo = 0;
            uint rebootReasons = 0;

            int res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, ref rebootReasons);
            if (res == ERROR_MORE_DATA && pnProcInfoNeeded > 0)
            {
                var infos = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;
                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, infos, ref rebootReasons);
                if (res == 0)
                {
                    var seen = new HashSet<int>();
                    for (int i = 0; i < pnProcInfo; i++)
                    {
                        int pid = infos[i].Process.dwProcessId;
                        if (pid <= 0 || !seen.Add(pid)) continue;

                        string procName = infos[i].strServiceShortName;
                        if (string.IsNullOrWhiteSpace(procName))
                        {
                            try
                            {
                                using var p = Process.GetProcessById(pid);
                                procName = p.ProcessName;
                            }
                            catch
                            {
                                procName = infos[i].strAppName;
                            }
                        }

                        var display = string.IsNullOrWhiteSpace(infos[i].strAppName) ? procName : infos[i].strAppName;
                        results.Add((pid, procName, display));
                    }
                }
            }
        }
        finally
        {
            RmEndSession(handle);
        }

        return results;
    }
}
