using Microsoft.Extensions.Logging;
using ChessLens.Core.Operations;
using System.Text.Json;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessLens.Core.Coaching;

public sealed class CoachingProcessor(LensDbContext db, ICoachingModel model, IOptions<CoachingOptions> configuration, ILogger<CoachingProcessor>? logger = null)
{
    public async Task<bool> RunNext(CancellationToken ct)
    {
        var options = configuration.Value; options.Validate();
        var token = Guid.NewGuid().ToString("N"); CoachingJob job;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.CoachingJobs.Where(j => j.State == "running" && j.LeaseUntil < DateTimeOffset.UtcNow && j.Attempts >= 3)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "failed").SetProperty(j => j.Error, "Coaching stopped after three interrupted attempts. The saved engine review remains available.")
                    .SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            var rows = await db.CoachingJobs.FromSqlRaw("""
                SELECT * FROM "CoachingJobs" WHERE ("State"='queued' OR ("State"='running' AND "LeaseUntil" < now()))
                AND "Attempts" < 3 ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (rows.Count == 0) { await tx.CommitAsync(ct); return false; }
            job = rows[0]; job.State = "running"; job.LeaseToken = token; job.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(1);
            job.Attempts++; job.Version++; job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        using var execution = new JobExecution(logger, "coaching", job.Id);
        db.ChangeTracker.Clear();
        try
        {
            var evidence = JsonSerializer.Deserialize<CoachingEvidence>(job.EvidenceJson, CoachingRules.Json)!;
            var explanation = CoachingRules.Factual(evidence, job.ModelRequested ? "provider-unavailable" : null);
            if (job.ModelRequested && !job.ModelAttempted && model.Configured && options.Configured && job.Model == model.Name)
            {
                // Reservation commits before the external request. Interrupted requests are never sent again.
                if (await Reserve(job, token, options, ct))
                {
                    await using var providerTx = await db.Database.BeginTransactionAsync(ct);
                    var gate = "chesslens-coaching-provider";
                    var acquired = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({gate}, 0)) AS \"Value\"").SingleAsync(ct);
                    if (acquired)
                    {
                        try
                        {
                            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                            var reply = await model.Explain(new(evidence, options.MaximumOutputTokens), deadline.Token);
                            var accepted = reply is null ? null : CoachingRules.Validate(reply.Json, evidence, reply.Model);
                            explanation = reply is null ? CoachingRules.Factual(evidence, "provider-failure") : accepted is null
                                ? CoachingRules.Factual(evidence, "unsupported-model-output") with { Model = reply.Model, ModelProvider = reply.Provider, ModelResponseId = reply.ResponseId }
                                : accepted with { ModelProvider = reply.Provider, ModelResponseId = reply.ResponseId };
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { explanation = CoachingRules.Factual(evidence, "provider-timeout"); }
                        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException)
                        { explanation = CoachingRules.Factual(evidence, "provider-failure"); }
                    }
                    else explanation = CoachingRules.Factual(evidence, "provider-busy");
                    await providerTx.CommitAsync(ct);
                }
                else explanation = CoachingRules.Factual(evidence, "model-budget-reached");
            }
            else if (job.ModelAttempted) explanation = CoachingRules.Factual(evidence, "interrupted-model-request");
            var result = JsonSerializer.Serialize(explanation, CoachingRules.Json);
            await db.CoachingJobs.Where(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "completed").SetProperty(j => j.ResultJson, result)
                    .SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null).SetProperty(j => j.Version, j => j.Version + 1)
                    .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            execution.Interrupted(error);
            var permanent = error is JsonException or ArgumentException || job.Attempts >= 3;
            await db.CoachingJobs.Where(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, permanent ? "failed" : "queued")
                    .SetProperty(j => j.Error, "The explanation could not be recovered. The saved engine review remains available.")
                    .SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null).SetProperty(j => j.Version, j => j.Version + 1)
                    .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            if (!permanent) throw;
        }
        return true;
    }

    private async Task<bool> Reserve(CoachingJob job, string token, CoachingOptions options, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({job.OwnerId}, 0))", ct);
        var global = "chesslens-coaching-budget";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({global}, 0))", ct);
        var current = await db.CoachingJobs.FromSqlInterpolated($"SELECT * FROM \"CoachingJobs\" WHERE \"Id\"={job.Id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (current is null || current.State != "running" || current.LeaseToken != token || current.ModelAttempted) return false;
        var cost = checked((long)CoachingRules.MaximumInputBytes * options.InputMicroDollarsPerToken + (long)options.MaximumOutputTokens * options.OutputMicroDollarsPerToken);
        var since = DateTimeOffset.UtcNow.AddDays(-1);
        var globalUsage = db.CoachingUsages.Where(u => u.CreatedAt > since);
        var ownerUsage = globalUsage.Where(u => u.OwnerId == job.OwnerId);
        if (await globalUsage.CountAsync(ct) >= options.DailyGlobalRequests || await ownerUsage.CountAsync(ct) >= options.DailyOwnerRequests ||
            await globalUsage.SumAsync(u => u.ReservedMicroDollars, ct) + cost > options.DailyGlobalMicroDollars ||
            await ownerUsage.SumAsync(u => u.ReservedMicroDollars, ct) + cost > options.DailyOwnerMicroDollars) return false;
        current.ModelAttempted = true; current.Version++; current.UpdatedAt = DateTimeOffset.UtcNow;
        db.CoachingUsages.Add(new CoachingUsage { OwnerId = job.OwnerId, JobId = job.Id, ReservedMicroDollars = cost,
            MaximumInputBytes = CoachingRules.MaximumInputBytes, MaximumOutputTokens = options.MaximumOutputTokens });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear(); return true;
    }
}
