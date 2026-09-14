using System.Text.Json;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Core.Analysis;

/// <summary>A browser review channel orders navigation independently of HTTP arrival order.</summary>
public sealed class PositionReviewChannel
{
    public Guid Id { get; set; }
    public required string OwnerId { get; set; }
    public Guid GameId { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PositionAnalysisJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public Guid GameId { get; set; }
    public Guid ChannelId { get; set; }
    public int Sequence { get; set; }
    public int Ply { get; set; }
    public required string InitialFen { get; set; }
    public required string HistoryJson { get; set; }
    public required string Fen { get; set; }
    public string State { get; set; } = "queued";
    public string? ResultJson { get; set; }
    public string? CacheIdentity { get; set; }
    public int Attempts { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? Error { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PositionAnalysisProcessor(LensDbContext db, IChessEngine engine)
{
    public static SearchBudget Budget => new("position-review-v1", 100_000, 2000, 3);
    public async Task<bool> RunNext(CancellationToken ct)
    {
        var token = Guid.NewGuid().ToString("N");
        PositionAnalysisJob job;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.PositionAnalysisJobs.Where(j => j.State == "running" && j.LeaseUntil < DateTimeOffset.UtcNow && j.Attempts >= 3)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "failed").SetProperty(j => j.Error, "Position review stopped after three interrupted attempts.")
                    .SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            var rows = await db.PositionAnalysisJobs.FromSqlRaw("""
                SELECT * FROM "PositionAnalysisJobs" WHERE ("State" = 'queued' OR ("State" = 'running' AND "LeaseUntil" < now()))
                AND "Attempts" < 3 ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (rows.Count == 0) { await tx.CommitAsync(ct); return false; }
            job = rows[0]; job.State = "running"; job.Attempts++; job.LeaseToken = token;
            job.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(5); job.Version++; job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        try
        {
            var position = new EnginePosition(job.InitialFen, JsonSerializer.Deserialize<string[]>(job.HistoryJson)!);
            var result = await engine.Search(position, Budget, null, 20, ct);
            if (!result.Complete) throw new InvalidOperationException("Incomplete position search.");
            // Superseded requests are already terminal. A late result can never
            // revive them, even if navigation occurred during the engine search.
            await db.PositionAnalysisJobs.Where(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "completed")
                    .SetProperty(j => j.ResultJson, JsonSerializer.Serialize(result, (JsonSerializerOptions?)null))
                    .SetProperty(j => j.CacheIdentity, position.CacheIdentity(result.Engine, result.SettingsJson, Budget, null))
                    .SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null).SetProperty(j => j.Error, (string?)null)
                    .SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            await db.PositionAnalysisJobs.Where(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, job.Attempts >= 3 ? "failed" : "queued")
                    .SetProperty(j => j.Error, "Position analysis interrupted; retrying within its resource limit.")
                    .SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null).SetProperty(j => j.Version, j => j.Version + 1)
                    .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            throw;
        }
        return true;
    }
}
