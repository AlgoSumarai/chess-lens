using ChessLens.Core.Persistence;
using ChessLens.Core.Training;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ChessLens.Core.Insights;

namespace ChessLens.Core.Reports;

/// <summary>Durable bounded backfill: one owner and at most one new report admitted per tick.</summary>
public sealed class ReportScheduler(LensDbContext db, IOptions<InsightOptions> configuration, ILogger<ReportScheduler>? logger = null)
{
    public async Task<bool> RunNext(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // Include quiet owners eventually without retaining an in-memory cursor across worker restarts.
        var missing = await db.Users.AsNoTracking().Where(u => !db.ReportSchedules.Any(s => s.OwnerId == u.Id)).OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => new { u.Id, u.Timezone }).Take(25).ToArrayAsync(ct);
        foreach (var user in missing)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct); await PracticeAdmission.LockOwner(db, user.Id, ct);
            if (!await db.ReportSchedules.AnyAsync(s => s.OwnerId == user.Id, ct)) db.ReportSchedules.Add(new() { OwnerId = user.Id, Timezone = user.Timezone });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear();
        }
        ReportSchedule schedule; var token = Guid.NewGuid().ToString("N");
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var rows = await db.ReportSchedules.FromSqlRaw("""
                SELECT * FROM "ReportSchedules" WHERE "NextCheckAt" <= now() AND ("LeaseUntil" IS NULL OR "LeaseUntil" < now())
                ORDER BY "NextCheckAt", "OwnerId" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (rows.Count == 0) { await tx.CommitAsync(ct); return false; }
            schedule = rows[0]; schedule.LeaseToken = token; schedule.LeaseUntil = now.AddMinutes(2);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var timezone = await db.Users.AsNoTracking().Where(u => u.Id == schedule.OwnerId).Select(u => u.Timezone).SingleAsync(deadline.Token);
            var monday = ReportCalendar.Monday(now, timezone);
            var completed = ReportCalendar.Week(monday.AddDays(-7), timezone, now);
            var restart = schedule.Timezone != timezone || schedule.CycleWeekFrom != completed.To;
            var before = restart || schedule.ScanBefore is null ? completed.To : schedule.ScanBefore.Value;
            var activity = await db.AnalysisRuns.AsNoTracking().Where(r => r.OwnerId == schedule.OwnerId && r.State == "completed" && r.UpdatedAt < before)
                .OrderByDescending(r => r.UpdatedAt).Select(r => r.UpdatedAt).Take(100).ToArrayAsync(deadline.Token);
            var weeks = activity.Select(t => ReportCalendar.Monday(t, timezone)).Distinct().OrderByDescending(d => d).ToArray();
            var capture = new ReportCapture(db);
            foreach (var week in weeks)
            {
                var inputs = await capture.Capture(schedule.OwnerId, ReportCalendar.Week(week, timezone, now), null, configuration.Value, deadline.Token);
                var job = await capture.Enqueue(schedule.OwnerId, inputs, deadline.Token);
                if (job?.State is "queued" or "running")
                {
                    // Retry this page after its report finishes; do not skip older unprocessed weeks.
                    await Finish(schedule, token, timezone, completed.To, before, now.AddMinutes(1), ct); return true;
                }
            }
            await Finish(schedule, token, timezone, completed.To, activity.Length == 0 ? completed.To : activity[^1],
                activity.Length == 0 ? now.AddHours(6) : now.AddSeconds(30), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (error is not (ReportBudgetReached or ResourceBudgetReached or ReportDataChanged)) logger?.LogWarning(error, "Weekly report schedule for owner {OwnerId} interrupted", schedule.OwnerId);
            await db.ReportSchedules.Where(s => s.OwnerId == schedule.OwnerId && s.LeaseToken == token)
                .ExecuteUpdateAsync(s => s.SetProperty(s => s.LeaseUntil, (DateTimeOffset?)null).SetProperty(s => s.NextCheckAt, now.AddMinutes(1)), ct);
        }
        return true;
    }
    private Task Finish(ReportSchedule schedule, string token, string timezone, DateTimeOffset cycle, DateTimeOffset before, DateTimeOffset next, CancellationToken ct) =>
        db.ReportSchedules.Where(s => s.OwnerId == schedule.OwnerId && s.LeaseToken == token)
            .ExecuteUpdateAsync(s => s.SetProperty(s => s.Timezone, timezone).SetProperty(s => s.CycleWeekFrom, cycle)
                .SetProperty(s => s.ScanBefore, before).SetProperty(s => s.NextCheckAt, next).SetProperty(s => s.LeaseUntil, (DateTimeOffset?)null), ct);
}
