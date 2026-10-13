using System.Text.Json;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChessLens.Core.Analysis;

public sealed class ImportAnalysisBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public Guid ImportJobId { get; set; }
    public required string Profile { get; set; }
    public string State { get; set; } = "queued";
    public required string RunIdsJson { get; set; }
    public int ReusedReviews { get; set; }
    public bool CancelRequested { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Reserves bounded reviews on submission and admits them fairly under the existing three-review limit.</summary>
public sealed class ImportBatchProcessor(LensDbContext db, ILogger<ImportBatchProcessor>? logger = null)
{
    public async Task<bool> RunNext(CancellationToken ct)
    {
        var candidates = await db.ImportAnalysisBatches.AsNoTracking().Where(b => b.State == "queued" || b.State == "running")
            .OrderBy(b => b.UpdatedAt).Select(b => new { b.Id, b.OwnerId }).Take(20).ToListAsync(ct);
        foreach (var candidate in candidates)
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({candidate.OwnerId}, 0))", ct);
            var batch = await db.ImportAnalysisBatches.FromSqlInterpolated($"SELECT * FROM \"ImportAnalysisBatches\" WHERE \"Id\" = {candidate.Id} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (batch is null || batch.State is not ("queued" or "running")) continue;
            var ids = JsonSerializer.Deserialize<Guid[]>(batch.RunIdsJson)!;
            var runs = await db.AnalysisRuns.Where(r => r.OwnerId == candidate.OwnerId && ids.Contains(r.Id)).ToListAsync(ct);
            var changed = false;
            if (batch.CancelRequested)
            {
                foreach (var run in runs.Where(r => r.BatchId == batch.Id && r.State is "waiting" or "queued" or "running"))
                {
                    var runChanged = false;
                    if (run.State == "waiting") { run.State = "cancelled"; runChanged = true; }
                    if (!run.CancelRequested) { run.CancelRequested = true; runChanged = true; }
                    if (runChanged) { changed = true; run.Version++; run.UpdatedAt = DateTimeOffset.UtcNow; }
                }
            }
            else
            {
                var active = await db.AnalysisRuns.CountAsync(r => r.OwnerId == candidate.OwnerId && (r.State == "queued" || r.State == "running"), ct);
                var occupied = await db.AnalysisRuns.Where(r => r.OwnerId == candidate.OwnerId && (r.State == "queued" || r.State == "running")).Select(r => r.GameId).ToArrayAsync(ct);
                foreach (var run in runs.Where(r => r.State == "waiting" && !occupied.Contains(r.GameId)).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Take(Math.Max(0, 3 - active)))
                { run.State = "queued"; run.Version++; run.UpdatedAt = DateTimeOffset.UtcNow; changed = true; }
            }
            var state = runs.Count != ids.Length ? "failed" : runs.All(r => r.State is "completed" or "failed" or "cancelled")
                ? batch.CancelRequested ? "cancelled" : runs.All(r => r.State == "completed") ? "completed" : "completed-with-errors" : "running";
            if (state != batch.State) { batch.State = state; changed = true; }
            if (changed)
            {
                batch.Version++; batch.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                logger?.LogInformation("Analysis batch {JobId} recorded state {JobState} at version {JobVersion}", batch.Id, batch.State, batch.Version);
                return true;
            }
            await tx.CommitAsync(ct);
        }
        return false;
    }
}
