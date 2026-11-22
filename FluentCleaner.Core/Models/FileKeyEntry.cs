using System;
using System.Text.RegularExpressions;

namespace FluentCleaner.Models;

// Controls how a FileKey directory is scanned.
public enum FileKeyFlag
{
    None,       // Top-level files only.
    Recurse,    // Scan all subdirectories recursively.
    RemoveSelf  // Like Recurse, but also prune empty directories afterwards.
}

/* Parsed representation of one FileKeyN= line from Winapp2.ini.
   Format:  FileKey1=<path>|<pattern(s)>[|RECURSE|REMOVESELF][|wholeregex=<regex>][|nwholeregex=<regex>]
   Example: FileKey1=%LocalAppData%\Temp|*.tmp;*.log|RECURSE */
public class FileKeyEntry
{
    // Directory path to scan. May contain %EnvVar% tokens and * wildcards in path segments.
    public string Path { get; set; } = "";

    /* Semicolon-separated file filter(s), e.g. "*.tmp" or "*.log;*.bak".
       Defaults to "*.*" when no pattern is specified in the ini. */
    public string Pattern { get; set; } = "*.*";

    // Whether to recurse into subdirectories and whether to remove empty dirs afterwards.
    public FileKeyFlag Flag { get; set; } = FileKeyFlag.None;

    // Compiled regex for full path inclusion (BleachBit 'wholeregex')
    public Regex? IncludeRegex { get; set; }
    public string? IncludeRegexPattern { get; set; }

    // Compiled regex for full path exclusion (BleachBit 'nwholeregex')
    public Regex? ExcludeRegex { get; set; }
    public string? ExcludeRegexPattern { get; set; }

    public static FileKeyEntry Parse(string value)
    {
        var parts = value.Split('|');
        var entry = new FileKeyEntry { Path = parts[0].Trim() };

        for (int i = 1; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            if (string.IsNullOrEmpty(p)) continue;

            if (p.StartsWith("wholeregex=", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("include_regex=", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("regex=", StringComparison.OrdinalIgnoreCase))
            {
                var pattern = p.Substring(p.IndexOf('=') + 1).Trim();
                entry.IncludeRegexPattern = pattern;
                try { entry.IncludeRegex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
                catch { }
            }
            else if (p.StartsWith("nwholeregex=", StringComparison.OrdinalIgnoreCase) ||
                     p.StartsWith("exclude_regex=", StringComparison.OrdinalIgnoreCase) ||
                     p.StartsWith("nregex=", StringComparison.OrdinalIgnoreCase))
            {
                var pattern = p.Substring(p.IndexOf('=') + 1).Trim();
                entry.ExcludeRegexPattern = pattern;
                try { entry.ExcludeRegex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
                catch { }
            }
            else if (p.Equals("RECURSE", StringComparison.OrdinalIgnoreCase))
            {
                entry.Flag = FileKeyFlag.Recurse;
            }
            else if (p.Equals("REMOVESELF", StringComparison.OrdinalIgnoreCase))
            {
                entry.Flag = FileKeyFlag.RemoveSelf;
            }
            else if (i == 1)
            {
                // First segment after path is the file pattern if not a flag or regex
                entry.Pattern = p;
            }
        }

        return entry;
    }
}
