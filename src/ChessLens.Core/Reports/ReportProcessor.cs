using ChessLens.Core.Operations;
using System.Text;
using System.Text.Json;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChessLens.Core.Reports;

public sealed class ReportProcessor(LensDbContext db, ILogger<ReportProcessor>? logger = null)
{
    public async Task<bool> RunNext(CancellationToken ct)
    {
        ReportSnapshot job; var token = Guid.NewGuid().ToString("N");
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.ReportSnapshots.Where(r => r.State == "running" && r.LeaseUntil < DateTimeOffset.UtcNow && r.Attempts >= 3)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, "failed").SetProperty(r => r.Error, "Report generation stopped after three interrupted executions.")
                    .SetProperty(r => r.LeaseUntil, (DateTimeOffset?)null).SetProperty(r => r.Version, r => r.Version + 1).SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), ct);
            var rows = await db.ReportSnapshots.FromSqlRaw("""
                SELECT * FROM "ReportSnapshots" WHERE ("State" = 'queued' OR ("State" = 'running' AND "LeaseUntil" < now()))
                AND "Attempts" < 3 ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (rows.Count == 0) { await tx.CommitAsync(ct); return false; }
            job = rows[0]; job.State = "running"; job.Attempts++; job.LeaseToken = token; job.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(2);
            job.Version++; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        using var execution = new JobExecution(logger, "report", job.Id);
        db.ChangeTracker.Clear();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(60));
            if (job.RulesVersion != ReportRules.Version) throw new ArgumentException("This report uses an unsupported earlier rule. Keep its completed history and request a new revision.");
            var inputs = JsonSerializer.Deserialize<ReportInputs>(job.InputsJson) ?? throw new ArgumentException("Report inputs are unavailable.");
            var result = await ReportRules.Generate(db, job.OwnerId, inputs, deadline.Token);
            var json = JsonSerializer.Serialize(result);
            if (Encoding.UTF8.GetByteCount(json) > 2_000_000) throw new ArgumentException("The report exceeded its output budget. Choose fewer games per period.");
            await db.ReportSnapshots.Where(r => r.Id == job.Id && r.State == "running" && r.LeaseToken == token)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, "completed").SetProperty(r => r.ResultJson, json)
                    .SetProperty(r => r.Error, (string?)null).SetProperty(r => r.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(r => r.Version, r => r.Version + 1).SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var permanent = error is ArgumentException or OperationCanceledException || job.Attempts >= 3;
            execution.Interrupted(error);
            await db.ReportSnapshots.Where(r => r.Id == job.Id && r.State == "running" && r.LeaseToken == token)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, permanent ? "failed" : "queued")
                    .SetProperty(r => r.Error, error is ArgumentException ? error.Message : "Report generation was interrupted or reached its processing budget. Try fewer games if this repeats.")
                    .SetProperty(r => r.LeaseUntil, (DateTimeOffset?)null).SetProperty(r => r.Version, r => r.Version + 1)
                    .SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), ct);
        }
        return true;
    }
}
