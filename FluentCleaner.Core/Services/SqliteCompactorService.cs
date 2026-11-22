namespace FluentCleaner.Services;

public sealed class CompactionProgress
{
    public int CompletedCount { get; }
    public int TotalCount { get; }
    public string CurrentDatabase { get; }
    public long ReclaimedBytes { get; }

    public CompactionProgress(int completedCount, int totalCount, string currentDatabase, long reclaimedBytes)
    {
        CompletedCount = completedCount;
        TotalCount = totalCount;
        CurrentDatabase = currentDatabase;
        ReclaimedBytes = reclaimedBytes;
    }
}

public sealed class CompactionSummary
{
    public int TotalDatabases { get; }
    public int SucceededCount { get; }
    public int FailedOrLockedCount { get; }
    public long TotalInitialBytes { get; }
    public long TotalFinalBytes { get; }
    public long TotalReclaimedBytes { get; }
    public IReadOnlyList<VacuumResult> Results { get; }

    public CompactionSummary(
        int totalDatabases,
        int succeededCount,
        int failedOrLockedCount,
        long totalInitialBytes,
        long totalFinalBytes,
        long totalReclaimedBytes,
        IReadOnlyList<VacuumResult> results)
    {
        TotalDatabases = totalDatabases;
        SucceededCount = succeededCount;
        FailedOrLockedCount = failedOrLockedCount;
        TotalInitialBytes = totalInitialBytes;
        TotalFinalBytes = totalFinalBytes;
        TotalReclaimedBytes = totalReclaimedBytes;
        Results = results;
    }
}

// 64-bit multi-core concurrent SQLite compaction service.
// Uses bounded parallel async workers to saturate CPU cores without thread starvation.
public class SqliteCompactorService
{
    public static int DefaultMaxConcurrency => Math.Max(2, Environment.ProcessorCount);

    public async Task<CompactionSummary> CompactAllAsync(
        IEnumerable<string> databasePaths,
        IProgress<CompactionProgress>? progress = null,
        int? maxConcurrency = null,
        CancellationToken token = default)
    {
        var targets = databasePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (targets.Count == 0)
        {
            return new CompactionSummary(0, 0, 0, 0, 0, 0, Array.Empty<VacuumResult>());
        }

        int concurrency = Math.Max(1, maxConcurrency ?? DefaultMaxConcurrency);
        using var throttle = new SemaphoreSlim(concurrency);

        var results = new System.Collections.Concurrent.ConcurrentBag<VacuumResult>();
        int completed = 0;
        int total = targets.Count;

        var tasks = targets.Select(async path =>
        {
            token.ThrowIfCancellationRequested();
            await throttle.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var result = await WinSqlite.VacuumDatabaseAsync(path, token).ConfigureAwait(false);
                results.Add(result);

                int done = Interlocked.Increment(ref completed);
                progress?.Report(new CompactionProgress(done, total, path, result.ReclaimedBytes));
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var allResults = results.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
        int succeeded = allResults.Count(r => r.Success);
        int failed = allResults.Count(r => !r.Success);
        long initial = allResults.Sum(r => r.InitialBytes);
        long final = allResults.Sum(r => r.FinalBytes);
        long reclaimed = allResults.Sum(r => r.ReclaimedBytes);

        return new CompactionSummary(total, succeeded, failed, initial, final, reclaimed, allResults);
    }
}
