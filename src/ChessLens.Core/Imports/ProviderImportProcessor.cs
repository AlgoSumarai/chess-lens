using Microsoft.Extensions.Logging;
using ChessLens.Core.Operations;
using System.Text;
using System.Text.Json;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Core.Imports;

/// <summary>Each tick commits one bounded provider page or 16 validated games, then yields to other work.</summary>
public sealed class ProviderImportProcessor(LensDbContext db, PgnReader reader, IProviderHttpClient http, ILogger<ProviderImportProcessor>? logger = null)
{
    public async Task<bool> RunNext(CancellationToken ct)
    {
        var token = Guid.NewGuid().ToString("N");
        ImportJob initial;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.ImportJobs.Where(j => j.Source != "pgn" && j.State == "running" && j.LeaseUntil < DateTimeOffset.UtcNow && j.Attempts >= 4)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "failed").SetProperty(j => j.Error, "Import stopped after four interrupted attempts. Resync from the import screen to try again.")
                    .SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            var jobs = await db.ImportJobs.FromSqlRaw("""
                SELECT * FROM "ImportJobs" WHERE "Source" IN ('chesscom','lichess')
                AND ("State" = 'queued' OR ("State" = 'running' AND "LeaseUntil" < now())) AND "Attempts" < 4
                AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= now() OR "CancelRequested")
                ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (jobs.Count == 0) { await tx.CommitAsync(ct); return false; }
            initial = jobs[0]; initial.State = "running"; initial.LeaseToken = token; initial.Attempts++;
            initial.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(2); initial.NextAttemptAt = null; initial.Error = null;
            ImportProcessor.Touch(initial);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        using var execution = new JobExecution(logger, "provider-import", initial.Id);
        db.ChangeTracker.Clear();
        try
        {
            var input = JsonSerializer.Deserialize<ProviderImportRequest>(initial.PayloadJson)!.Normalized(); input.Validate();
            if (initial.CancelRequested) { await Mutate(initial.Id, token, j => Finish(j, "cancelled"), ct); return true; }
            var checkpoint = initial.CheckpointJson is null ? null : JsonSerializer.Deserialize<ProviderCheckpoint>(initial.CheckpointJson);
            if (checkpoint?.Fetched == true) await ValidateRecords(initial.Id, token, input, checkpoint, ct);
            else await FetchPage(initial.Id, token, input, checkpoint, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            execution.Interrupted(error);
            db.ChangeTracker.Clear();
            await Mutate(initial.Id, token, job =>
            {
                var permanent = error is ArgumentException or JsonException || job.Attempts >= 4;
                job.State = permanent ? "failed" : "queued";
                job.Error = error is ArgumentException ? error.Message : permanent ? "Import stopped after repeated provider failures. Try resyncing a smaller date range." : "Import interrupted. Saved progress will resume automatically.";
                job.NextAttemptAt = permanent ? null : DateTimeOffset.UtcNow.AddSeconds(5 * (1 << (job.Attempts - 1)));
                job.LeaseUntil = null;
            }, ct);
            if (error is not (ArgumentException or JsonException or ProviderRetryException)) throw;
        }
        return true;
    }

    private async Task FetchPage(Guid id, string token, ProviderImportRequest input, ProviderCheckpoint? checkpoint, CancellationToken ct)
    {
        // A database advisory lock serializes all workers per provider, including across application instances.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var gate = "chesslens-public-provider:" + input.Provider;
        if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({gate}, 0)) AS \"Value\"").SingleAsync(ct))
        {
            await UpdateLocked(id, token, j => Yield(j, DateTimeOffset.UtcNow.AddSeconds(2)), ct);
            await tx.CommitAsync(ct); return;
        }
        var throttle = await db.ProviderThrottles.SingleOrDefaultAsync(t => t.Provider == input.Provider, ct);
        if (throttle?.NotBefore > DateTimeOffset.UtcNow)
        {
            await UpdateLocked(id, token, j => { Yield(j, throttle.NotBefore); j.Error = "The provider asked us to pause. Your import will resume automatically."; }, ct);
            await tx.CommitAsync(ct); return;
        }
        var endpoint = input.Provider == "lichess" ? ProviderContracts.Lichess(input) : checkpoint is null
            ? ProviderContracts.Archives(input) : ProviderContracts.Month(input, checkpoint.Months[checkpoint.NextMonth]);
        string body;
        try { body = await http.Get(endpoint, ct); }
        catch (ProviderRetryException retry)
        {
            var backoff = TimeSpan.FromSeconds(5 * (1 << Math.Max(0, Math.Min(3, (await Locked(id, token, ct))?.Attempts - 1 ?? 0))));
            var notBefore = DateTimeOffset.UtcNow.Add(retry.Delay < backoff ? backoff : retry.Delay);
            if (throttle is null) db.ProviderThrottles.Add(new ProviderThrottle { Provider = input.Provider, NotBefore = notBefore });
            else throttle.NotBefore = notBefore;
            await UpdateLocked(id, token, j =>
            {
                j.State = j.Attempts >= 4 ? "failed" : "queued"; j.NextAttemptAt = notBefore; j.LeaseUntil = null;
                j.Error = j.Attempts >= 4 ? "The provider remained unavailable after four bounded attempts. Resync later to try again." : retry.Message;
            }, ct);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return;
        }
        ProviderCheckpoint next;
        if (input.Provider == "lichess") next = new([], 0, ProviderContracts.ParseLichess(body, input), true);
        else if (checkpoint is null)
        {
            var months = ProviderContracts.ParseArchives(body, input);
            next = new(months, 0, [], months.Length == 0);
        }
        else
        {
            var added = ProviderContracts.ParseChessMonth(body, input, input.MaxGames - checkpoint.Records.Length);
            var records = checkpoint.Records.Concat(added).ToArray();
            next = checkpoint with { NextMonth = checkpoint.NextMonth + 1, Records = records,
                Fetched = records.Length >= input.MaxGames || checkpoint.NextMonth + 1 >= checkpoint.Months.Length };
        }
        // Bound durable staging as well as response size; no partly staged page enters the checkpoint.
        if (next.Records.Sum(r => Encoding.UTF8.GetByteCount(r.Pgn ?? "")) > PgnReader.MaxBytes)
            throw new ArgumentException("This selection exceeds 5 MB of PGN data. Choose fewer games.");
        await UpdateLocked(id, token, job =>
        {
            job.CheckpointJson = JsonSerializer.Serialize(next); job.Total = next.Records.Length;
            job.Phase = next.Fetched ? "validating" : "fetching";
            Yield(job);
        }, ct);
        await tx.CommitAsync(ct);
    }

    private async Task ValidateRecords(Guid id, string token, ProviderImportRequest input, ProviderCheckpoint checkpoint, CancellationToken ct)
    {
        for (var i = 0; i < 16; i++)
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var job = await Locked(id, token, ct);
            if (job is null) return;
            if (job.CancelRequested || job.Processed >= checkpoint.Records.Length)
            {
                Finish(job, job.CancelRequested ? "cancelled" : "completed");
                ImportProcessor.Touch(job); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return;
            }
            try
            {
                var game = checkpoint.Records[job.Processed].Parse(reader, input.Provider);
                await ImportStore.Save(db, job, game, input.Username, ct);
            }
            catch (ArgumentException error)
            {
                var failures = JsonSerializer.Deserialize<List<ImportFailure>>(job.FailuresJson) ?? [];
                failures.Add(new(job.Processed + 1, error.Message)); job.FailuresJson = JsonSerializer.Serialize(failures);
            }
            job.Processed++; job.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(2);
            ImportProcessor.Touch(job); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        await Mutate(id, token, j => Yield(j), ct);
    }

    private async Task<ImportJob?> Locked(Guid id, string token, CancellationToken ct)
    {
        var job = await db.ImportJobs.FromSqlInterpolated($"SELECT * FROM \"ImportJobs\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
        return job is { State: "running" } && job.LeaseToken == token ? job : null;
    }
    private async Task UpdateLocked(Guid id, string token, Action<ImportJob> action, CancellationToken ct)
    {
        var job = await Locked(id, token, ct); if (job is null) return;
        if (job.CancelRequested) Finish(job, "cancelled"); else action(job);
        ImportProcessor.Touch(job); await db.SaveChangesAsync(ct);
    }
    private async Task Mutate(Guid id, string token, Action<ImportJob> action, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await UpdateLocked(id, token, action, ct); await tx.CommitAsync(ct);
    }
    private static void Yield(ImportJob job, DateTimeOffset? next = null)
    { job.State = "queued"; job.LeaseUntil = null; job.NextAttemptAt = next; job.Attempts = 0; }
    private static void Finish(ImportJob job, string state)
    { job.State = state; job.LeaseUntil = null; job.NextAttemptAt = null; job.CheckpointJson = null; job.Phase = "finished"; }
}
