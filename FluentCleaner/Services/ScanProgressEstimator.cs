namespace FluentCleaner.Services;

public readonly record struct ScanProgressSnapshot(double Percent, TimeSpan? Eta, string DisplayText);

/// <summary>
/// Computes scan completion percentage and estimated time remaining (ETA) from elapsed wall time.
/// </summary>
public static class ScanProgressEstimator
{
    public static ScanProgressSnapshot Estimate(int done, int total, TimeSpan elapsed)
    {
        if (total <= 0)
            return new ScanProgressSnapshot(0, null, "0%");

        int clampedDone = Math.Clamp(done, 0, total);
        double percent = clampedDone * 100.0 / total;
        int roundedPct = (int)Math.Round(percent);

        if (clampedDone >= total)
            return new ScanProgressSnapshot(100, TimeSpan.Zero, $"100% · {total:N0}/{total:N0} rules · Done");

        // Warm-up guard: avoid wild ETA spikes on the first 1-2 fast entries
        if (clampedDone < 3 || elapsed.TotalMilliseconds < 400)
            return new ScanProgressSnapshot(percent, null, $"{roundedPct}% · {clampedDone:N0}/{total:N0} rules · Calculating ETA…");

        double ratePerSec = clampedDone / elapsed.TotalSeconds;
        if (ratePerSec <= 0.001)
            return new ScanProgressSnapshot(percent, null, $"{roundedPct}% · {clampedDone:N0}/{total:N0} rules · Calculating ETA…");

        double remainingSec = (total - clampedDone) / ratePerSec;
        var eta = TimeSpan.FromSeconds(remainingSec);
        return new ScanProgressSnapshot(percent, eta, $"{roundedPct}% · {clampedDone:N0}/{total:N0} rules · {FormatEta(eta)}");
    }

    public static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalSeconds < 1) return "< 1s left";
        if (eta.TotalSeconds < 60) return $"~{(int)Math.Ceiling(eta.TotalSeconds)}s left";
        int mins = (int)eta.TotalMinutes;
        int secs = eta.Seconds;
        return secs > 0 ? $"~{mins}m {secs}s left" : $"~{mins}m left";
    }
}
