using FluentCleaner.Models;
using System.Text.RegularExpressions;

namespace FluentCleaner.Services;

// Parses the Winapp2.ini format into CleanerEntry objects.
// The format is INI-like but with numbered multi-value keys:
// FileKey1=..., FileKey2=..., Detect, Detect1, Detect2, etc.
public class Winapp2Parser
{
    private static readonly Regex RxFileKey    = new(@"^FileKey\d+$",    RegexOptions.IgnoreCase | RegexOptions.Compiled); // FileKey1, FileKey2 ...; number required
    private static readonly Regex RxRegKey     = new(@"^RegKey\d+$",     RegexOptions.IgnoreCase | RegexOptions.Compiled); // RegKey1, RegKey2 ...
    private static readonly Regex RxExcludeKey = new(@"^ExcludeKey\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled); // ExcludeKey1, ExcludeKey2 ...
    private static readonly Regex RxDetect     = new(@"^Detect\d*$",     RegexOptions.IgnoreCase | RegexOptions.Compiled); // Detect or Detect1; number optional
    private static readonly Regex RxDetectFile = new(@"^DetectFile\d*$", RegexOptions.IgnoreCase | RegexOptions.Compiled); // DetectFile or DetectFile1; number optional

    public List<CleanerEntry> Parse(string content)
    {
        var entries = new List<CleanerEntry>();
        CleanerEntry? current = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (current is not null && IsValid(current)) entries.Add(current);

                var name = line[1..^1].Trim();
                //Skip the files own header block
                if (name.StartsWith("Winapp2", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("version",  StringComparison.OrdinalIgnoreCase))
                {
                    current = null;
                    continue;
                }

                //Strip the trailing " *" Winapp2 uses to mark community entries
                current = new CleanerEntry { Name = name.TrimEnd('*').TrimEnd() };
                continue;
            }

            if (current is null) continue;

            var eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;

            var key   = line[..eqIdx].Trim();
            var value = line[(eqIdx + 1)..].Trim();
            if (value.Length == 0) continue;

            if      (key.Equals("LangSecRef",    StringComparison.OrdinalIgnoreCase)) { if (int.TryParse(value, out var n)) current.LangSecRef = n; }
            else if (key.Equals("Section",       StringComparison.OrdinalIgnoreCase)) current.Section       = value;
            else if (key.Equals("SpecialDetect", StringComparison.OrdinalIgnoreCase)) current.SpecialDetect = value;
            else if (key.Equals("Warning",       StringComparison.OrdinalIgnoreCase)) current.Warning       = value;
            else if (key.Equals("Default",       StringComparison.OrdinalIgnoreCase)) current.Default       = value.Equals("True", StringComparison.OrdinalIgnoreCase);
            else if (RxDetect.IsMatch(key))     current.DetectKeys.Add(value);
            else if (RxDetectFile.IsMatch(key)) current.DetectFiles.Add(value);
            else if (RxFileKey.IsMatch(key))    current.FileKeys.Add(FileKeyEntry.Parse(value));
            else if (RxRegKey.IsMatch(key))     current.RegKeys.Add(RegKeyEntry.Parse(value));
            else if (RxExcludeKey.IsMatch(key)) current.ExcludeKeys.Add(ExcludeKeyEntry.Parse(value));
        }

        if (current is not null && IsValid(current)) entries.Add(current);
        return entries;
    }

    // An entry is only useful if it can be detected AND has something to clean
    private static bool IsValid(CleanerEntry e) =>
        (e.DetectKeys.Count > 0 || e.DetectFiles.Count > 0 || e.SpecialDetect is not null) &&
        (e.FileKeys.Count  > 0  || e.RegKeys.Count  > 0);

    public async Task<List<CleanerEntry>> ParseFileAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        return Parse(content);
    }
}
