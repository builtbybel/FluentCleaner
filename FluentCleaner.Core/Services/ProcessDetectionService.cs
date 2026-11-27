using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentCleaner.Models;

namespace FluentCleaner.Services;

/// <summary>
/// Provides high-performance 64-bit multi-threaded running process detection for cleaner entries.
/// Maintains an asynchronous, thread-safe process snapshot cache to eliminate UI thread latency
/// and prevent handle leaks through proper process object disposal.
/// </summary>
public static class ProcessDetectionService
{
    private static readonly object _lock = new();
    private static HashSet<string> _cachedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastCacheTime = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(3);

    // Well-known signatures mapping application names and detection codes to process executable names
    private static readonly Dictionary<string, string[]> WellKnownAppProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google Chrome"] = new[] { "chrome" },
        ["Chrome"] = new[] { "chrome" },
        ["DET_CHROME"] = new[] { "chrome" },
        ["Microsoft Edge"] = new[] { "msedge" },
        ["Edge"] = new[] { "msedge" },
        ["DET_EDGE"] = new[] { "msedge" },
        ["Mozilla Firefox"] = new[] { "firefox" },
        ["Firefox"] = new[] { "firefox" },
        ["DET_FIREFOX"] = new[] { "firefox" },
        ["Brave"] = new[] { "brave" },
        ["Vivaldi"] = new[] { "vivaldi" },
        ["Opera"] = new[] { "opera" },
        ["Opera GX"] = new[] { "opera" },
        ["Visual Studio Code"] = new[] { "code" },
        ["VS Code"] = new[] { "code" },
        ["Code"] = new[] { "code" },
        ["Cursor"] = new[] { "cursor" },
        ["Windsurf"] = new[] { "windsurf" },
        ["Antigravity"] = new[] { "antigravity" },
        ["Discord"] = new[] { "discord" },
        ["Spotify"] = new[] { "spotify" },
        ["Steam"] = new[] { "steam" },
        ["Telegram"] = new[] { "telegram" },
        ["Docker Desktop"] = new[] { "docker desktop", "com.docker.backend" },
        ["Docker"] = new[] { "docker desktop", "com.docker.backend" },
        ["Android Studio"] = new[] { "studio64" },
        ["Slack"] = new[] { "slack" },
        ["Notepad++"] = new[] { "notepad++" },
        ["Sublime Text"] = new[] { "sublime_text" },
        ["WhatsApp"] = new[] { "whatsapp" },
        ["Zoom"] = new[] { "zoom" },
        ["Microsoft Teams"] = new[] { "teams", "ms-teams" },
        ["Thunderbird"] = new[] { "thunderbird" },
        ["Waterfox"] = new[] { "waterfox" },
        ["Zen"] = new[] { "zen" },
    };

    /// <summary>
    /// Asynchronously captures or retrieves the current running process snapshot on a threadpool thread.
    /// Safely disposes all native process handle wrappers.
    /// </summary>
    public static async Task<HashSet<string>> GetRunningProcessesAsync(bool forceRefresh = false)
    {
        return await Task.Run(() => GetRunningProcesses(forceRefresh));
    }

    /// <summary>
    /// Synchronously retrieves the running process snapshot using thread-safe TTL caching.
    /// </summary>
    public static HashSet<string> GetRunningProcesses(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (!forceRefresh && (DateTime.UtcNow - _lastCacheTime) < CacheTtl && _cachedProcesses.Count > 0)
            {
                return new HashSet<string>(_cachedProcesses, StringComparer.OrdinalIgnoreCase);
            }

            var snapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return snapshot;
            }

            foreach (var proc in processes)
            {
                try
                {
                    snapshot.Add(proc.ProcessName);
                }
                catch
                {
                    // Ignore inaccessible process names
                }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            }

            _cachedProcesses = snapshot;
            _lastCacheTime = DateTime.UtcNow;
            return new HashSet<string>(snapshot, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Determines whether the process for a cleaner entry is currently running.
    /// Returns the matched running process name, or null if not running.
    /// </summary>
    public static bool IsProcessRunning(CleanerEntry? entry, out string runningName, HashSet<string>? snapshot = null)
    {
        runningName = string.Empty;
        if (entry is null) return false;

        var set = snapshot ?? GetRunningProcesses();

        // 1. Check explicit RunningProcess or CloseProcesses if set
        if (!string.IsNullOrWhiteSpace(entry.RunningProcess))
        {
            var candidates = entry.RunningProcess!.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var cand in candidates)
            {
                var clean = CleanProcessName(cand);
                if (set.Contains(clean))
                {
                    runningName = clean;
                    return true;
                }
            }
        }

        // 2. Check SpecialDetect mapping if present
        if (!string.IsNullOrWhiteSpace(entry.SpecialDetect) && WellKnownAppProcesses.TryGetValue(entry.SpecialDetect!, out var specialNames))
        {
            foreach (var name in specialNames)
            {
                if (set.Contains(name))
                {
                    runningName = name;
                    return true;
                }
            }
        }

        // 3. Check well-known app name dictionary
        if (!string.IsNullOrWhiteSpace(entry.Name))
        {
            foreach (var kvp in WellKnownAppProcesses)
            {
                if (entry.Name.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (var name in kvp.Value)
                    {
                        if (set.Contains(name))
                        {
                            runningName = name;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static string CleanProcessName(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring(0, trimmed.Length - 4);
        return trimmed;
    }
}
