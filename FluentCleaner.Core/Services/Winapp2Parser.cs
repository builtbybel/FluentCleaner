using FluentCleaner.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace FluentCleaner.Services;

// Parses the Winapp2.ini format into CleanerEntry objects.
// The format is INI-like but with numbered multi-value keys:
// FileKey1=..., FileKey2=..., Detect, Detect1, Detect2, etc.
public class Winapp2Parser
{
    private static readonly Regex RxFileKey = new(@"^FileKey\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled); // FileKey1, FileKey2 ...; number required
    private static readonly Regex RxRegKey = new(@"^RegKey\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled); // RegKey1, RegKey2 ...
    private static readonly Regex RxExcludeKey = new(@"^ExcludeKey\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled); // ExcludeKey1, ExcludeKey2 ...
    private static readonly Regex RxDetect = new(@"^Detect\d*$", RegexOptions.IgnoreCase | RegexOptions.Compiled); // Detect or Detect1; number optional
    private static readonly Regex RxDetectFile = new(@"^DetectFile\d*$", RegexOptions.IgnoreCase | RegexOptions.Compiled); // DetectFile or DetectFile1; number optional

    public List<CleanerEntry> Parse(string content)
    {
        var entries = new List<CleanerEntry>();
        CleanerEntry? current = null;
        var raw = new StringBuilder();   // collects the verbatim block of the current entry for "Show source"

        //Split on both \r and \n;WinUI 3 TextBox saves with \r only (not \r\n),
        //so splitting on just \n would leave the entire file as a single line
        foreach (var rawLine in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                if (current is not null && IsValid(current)) { current.RawText = raw.ToString().TrimEnd(); entries.Add(current); }

                raw.Clear().AppendLine(line);   // new block starts at the [header]

                var name = line[1..^1].Trim();
                //Skip the files own header block
                if (name.StartsWith("Winapp2", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("version", StringComparison.OrdinalIgnoreCase))
                {
                    current = null;
                    continue;
                }

                //Strip the trailing " *" Winapp2 uses to mark community entries
                current = new CleanerEntry { Name = name.TrimEnd('*').TrimEnd() };
                continue;
            }

            if (current is null) continue;

            raw.AppendLine(line);   // keep every key line so the source view matches the ini

            var eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;

            var key = line[..eqIdx].Trim();
            var value = line[(eqIdx + 1)..].Trim();
            if (value.Length == 0) continue;

            if (key.Equals("LangSecRef", StringComparison.OrdinalIgnoreCase)) { if (int.TryParse(value, out var n)) current.LangSecRef = n; }
            else if (key.Equals("Section", StringComparison.OrdinalIgnoreCase)) current.Section = value;
            else if (key.Equals("SpecialDetect", StringComparison.OrdinalIgnoreCase)) current.SpecialDetect = value;
            else if (key.Equals("Warning", StringComparison.OrdinalIgnoreCase)) current.Warning = value;
            else if (key.Equals("Default", StringComparison.OrdinalIgnoreCase)) current.Default = value.Equals("True", StringComparison.OrdinalIgnoreCase);
            else if (key.Equals("RunningProcess", StringComparison.OrdinalIgnoreCase) || key.Equals("CloseProcesses", StringComparison.OrdinalIgnoreCase)) current.RunningProcess = value;
            else if (RxDetect.IsMatch(key)) current.DetectKeys.Add(value);
            else if (RxDetectFile.IsMatch(key)) current.DetectFiles.Add(value);
            else if (RxFileKey.IsMatch(key)) current.FileKeys.Add(FileKeyEntry.Parse(value));
            else if (RxRegKey.IsMatch(key)) current.RegKeys.Add(RegKeyEntry.Parse(value));
            else if (RxExcludeKey.IsMatch(key)) current.ExcludeKeys.Add(ExcludeKeyEntry.Parse(value));
        }

        if (current is not null && IsValid(current)) { current.RawText = raw.ToString().TrimEnd(); entries.Add(current); }
        return entries;
    }

    // An entry is only useful if it can be detected AND has something to clean
    private static bool IsValid(CleanerEntry e) =>
        (e.DetectKeys.Count > 0 || e.DetectFiles.Count > 0 || e.SpecialDetect is not null) &&
        (e.FileKeys.Count > 0 || e.RegKeys.Count > 0);

    public async Task<List<CleanerEntry>> ParseFileAsync(string filePath)
    {
        // netstandard2.0/net48 don't have File.ReadAllTextAsync;StreamReader does the same job
        string content;
        using (var reader = new StreamReader(filePath))
            content = await reader.ReadToEndAsync();
        var entries = Parse(content);

        // Winapp3 has no Default= lines, missing Default = ticked, so everything got ticked (#217).
        // done here, not in the file, Winapp3.ini gets replaced on every update
        if (Path.GetFileName(filePath).Equals("Winapp3.ini", StringComparison.OrdinalIgnoreCase))
            foreach (var entry in entries)
                entry.Default = false;

        return entries;
    }
}
