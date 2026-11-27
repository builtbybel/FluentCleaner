using System;

namespace FluentCleaner.Models;

// Controls how a FileKey directory is scanned and cleaned.
[Flags]
public enum FileKeyFlag
{
    None = 0,
    Recurse = 1 << 0,       // Scan all subdirectories recursively.
    RemoveSelf = 1 << 1,    // Like Recurse, but also prune empty directories afterwards.
    Truncate = 1 << 2       // Zero locked or active log files in-place instead of deleting directory entry.
}

/* Parsed representation of one FileKeyN= line from Winapp2.ini.
   Format:  FileKey1=<path>|<pattern(s)>[|RECURSE|REMOVESELF|TRUNCATE]
   Example: FileKey1=%LocalAppData%\Temp|*.tmp;*.log|RECURSE */
public class FileKeyEntry
{
    // Directory path to scan. May contain %EnvVar% tokens and * wildcards in path segments.
    public string Path { get; set; } = "";

    /* Semicolon-separated file filter(s), e.g. "*.tmp" or "*.log;*.bak".
       Defaults to "*.*" when no pattern is specified in the ini. */
    public string Pattern { get; set; } = "*.*";

    // Whether to recurse into subdirectories, remove empty dirs afterwards, or truncate in-place.
    public FileKeyFlag Flag { get; set; } = FileKeyFlag.None;

    public static FileKeyEntry Parse(string value)
    {
        var parts = value.Split('|');
        var entry = new FileKeyEntry { Path = parts[0].Trim() };

        // parts[1] can be either a file pattern OR a flag (when no pattern is given)
        if (parts.Length == 2)
        {
            var p = parts[1].Trim();
            if (TryParseFlag(p, out var flag))
            {
                entry.Flag = flag;
            }
            else
            {
                entry.Pattern = p;
            }
        }
        else if (parts.Length > 2)
        {
            entry.Pattern = parts[1].Trim();
            if (TryParseFlag(parts[2].Trim(), out var flag))
            {
                entry.Flag = flag;
            }
        }

        return entry;
    }

    private static bool TryParseFlag(string raw, out FileKeyFlag result)
    {
        result = FileKeyFlag.None;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var tokens = raw.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
        bool matched = false;

        foreach (var token in tokens)
        {
            var t = token.Trim().ToUpperInvariant();
            switch (t)
            {
                case "RECURSE":
                    result |= FileKeyFlag.Recurse;
                    matched = true;
                    break;
                case "REMOVESELF":
                    result |= FileKeyFlag.RemoveSelf | FileKeyFlag.Recurse;
                    matched = true;
                    break;
                case "TRUNCATE":
                    result |= FileKeyFlag.Truncate;
                    matched = true;
                    break;
            }
        }

        return matched;
    }
}
