using System.Text.Json;
using ChessLens.Core.Games;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Core.Imports;

public sealed class ImportProcessor(LensDbContext db, PgnReader reader)
{
    public async Task<bool> RunNext(CancellationToken ct)
    {
        var token = Guid.NewGuid().ToString("N");
        Guid id;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.ImportJobs.Where(j => j.State == "running" && j.LeaseUntil < DateTimeOffset.UtcNow && j.Attempts >= 4)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "failed").SetProperty(j => j.Error, "Import stopped after four interrupted attempts.")
                    .SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            var job = await db.ImportJobs.FromSqlRaw("""
                SELECT * FROM "ImportJobs"
                WHERE ("State" = 'queued' OR ("State" = 'running' AND "LeaseUntil" < now()))
                AND "Attempts" < 4 ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            var claimed = job.SingleOrDefault();
            if (claimed is null) { await tx.CommitAsync(ct); return false; }
            id = claimed.Id;
            claimed.LeaseToken = token;
            claimed.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(2);
            claimed.State = "running";
            claimed.Attempts++;
            claimed.Error = null;
            Touch(claimed);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        try
        {
            var initial = await db.ImportJobs.AsNoTracking().SingleAsync(j => j.Id == id, ct);
            var payload = JsonSerializer.Deserialize<PgnImportRequest>(initial.PayloadJson) ?? throw new ArgumentException("Missing import payload.");
            var batch = reader.Split(payload.Pgn, payload.MaxGames);
            if (batch.Count == 0) throw new ArgumentException("No games were found in this upload.");
            while (true)
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var job = await db.ImportJobs.FromSqlInterpolated($"SELECT * FROM \"ImportJobs\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
                if (job is null || job.LeaseToken != token || job.State != "running") return true;
                job.Total = batch.Count;
                if (job.CancelRequested || job.Processed >= batch.Count)
                {
                    job.State = job.CancelRequested ? "cancelled" : "completed";
                    job.PayloadJson = "{}";
                    job.LeaseUntil = null;
                    Touch(job);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return true;
                }
                SourceGame? parsed = null;
                try { parsed = reader.Parse(batch[job.Processed]); }
                catch (ArgumentException e)
                {
                    var failures = JsonSerializer.Deserialize<List<ImportFailure>>(job.FailuresJson) ?? [];
                    failures.Add(new(job.Processed + 1, e.Message));
                    job.FailuresJson = JsonSerializer.Serialize(failures);
                }
                if (parsed is not null)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({parsed.Fingerprint}, 0))", ct);
                    var existing = await db.Games.SingleOrDefaultAsync(g => g.Fingerprint == parsed.Fingerprint, ct);
                    var game = existing ?? parsed;
                    if (existing is null) db.Games.Add(game);
                    if (await db.UserGames.AnyAsync(g => g.OwnerId == job.OwnerId && g.GameId == game.Id, ct)) job.Duplicates++;
                    else
                    {
                        db.UserGames.Add(new UserGame { OwnerId = job.OwnerId, GameId = game.Id, PlayerColour = PgnReader.Identify(game, payload.PlayerName) });
                        job.Imported++;
                    }
                }
                job.Processed++;
                job.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(2);
                Touch(job);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            db.ChangeTracker.Clear();
            var job = await db.ImportJobs.SingleOrDefaultAsync(j => j.Id == id && j.LeaseToken == token, ct);
            if (job is not null)
            {
                job.State = e is ArgumentException || job.Attempts >= 4 ? "failed" : "queued";
                job.Error = e is ArgumentException ? e.Message : "Import interrupted. Retrying within the configured limit.";
                job.LeaseUntil = null;
                Touch(job);
                await db.SaveChangesAsync(ct);
            }
            if (e is not ArgumentException) throw;
        }
        return true;
    }

    public static void Touch(ImportJob job) { job.Version++; job.UpdatedAt = DateTimeOffset.UtcNow; }
}
