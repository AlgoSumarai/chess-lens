using System.Security.Claims;
using System.Text.Json;
using ChessLens.Core.Insights;
using ChessLens.Core.Persistence;
using ChessLens.Core.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessLens.Api;

public static class ReportEndpoints
{
    public static void MapReports(this WebApplication app)
    {
        var reports = app.MapGroup("/api/reports").RequireAuthorization().RequireRateLimiting("api");
        reports.MapPost("/weekly", async (WeeklyReportRequest request, ClaimsPrincipal user, LensDbContext db, IOptions<InsightOptions> options, CancellationToken ct) =>
        {
            var owner = user.Owner(); var timezone = await db.Users.Where(u => u.Id == owner).Select(u => u.Timezone).SingleAsync(ct);
            var now = DateTimeOffset.UtcNow; var monday = request.WeekFrom ?? ReportCalendar.Monday(now, timezone).AddDays(-7);
            var period = ReportCalendar.Week(monday, timezone, now);
            return await Queue(owner, period, null, request.MaximumGames, db, options.Value, ct);
        });
        app.MapPost("/api/progress", async (ProgressFilter filter, ClaimsPrincipal user, LensDbContext db, IOptions<InsightOptions> options, CancellationToken ct) =>
        {
            filter.Validate(); return await Queue(user.Owner(), ReportCalendar.Progress(filter.FromDate, filter.ThroughDate), filter, filter.MaximumGames, db, options.Value, ct);
        }).RequireAuthorization().RequireRateLimiting("api");
        reports.MapGet("", async (ClaimsPrincipal user, LensDbContext db, CancellationToken ct, int page = 1, string? kind = null) =>
        {
            if (page is < 1 or > 10000 || kind is not (null or "weekly" or "progress")) throw new ArgumentException("Choose a valid report page/type.");
            var query = db.ReportSnapshots.AsNoTracking().Where(r => r.OwnerId == user.Owner() && (kind == null || r.Kind == kind));
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip((page - 1) * 20).Take(20).ToArrayAsync(ct);
            return Results.Ok(new { total, page, items = rows.Select(r => View(r, includeResult: false)) });
        });
        reports.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var report = await db.ReportSnapshots.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.OwnerId == user.Owner(), ct);
            return report is null ? Results.NotFound() : Results.Ok(View(report));
        });
        reports.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var changed = await db.ReportSnapshots.Where(r => r.Id == id && r.OwnerId == user.Owner() && (r.State == "queued" || r.State == "running"))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, "cancelled").SetProperty(r => r.Version, r => r.Version + 1)
                    .SetProperty(r => r.LeaseUntil, (DateTimeOffset?)null).SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), ct);
            return changed == 0 ? Results.NotFound() : Results.NoContent();
        });
        reports.MapPost("/{id:guid}/retry", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var report = await db.ReportSnapshots.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.OwnerId == user.Owner(), ct);
            if (report is null) return Results.NotFound();
            try
            {
                var inputs = JsonSerializer.Deserialize<ReportInputs>(report.InputsJson)!;
                var queued = await new ReportCapture(db).Enqueue(user.Owner(), inputs, ct);
                return Results.Accepted($"/api/reports/{queued!.Id}", View(queued));
            }
            catch (ReportBudgetReached) { return Budget(); }
            catch (ReportDataChanged) { return DataChanged(); }
        });
        reports.MapGet("/calendar", async (ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var timezone = await db.Users.Where(u => u.Id == user.Owner()).Select(u => u.Timezone).SingleAsync(ct);
            var now = DateTimeOffset.UtcNow; var monday = ReportCalendar.Monday(now, timezone);
            return Results.Ok(new { timezone, currentWeekFrom = monday, latestCompleted = ReportCalendar.Week(monday.AddDays(-7), timezone, now) });
        });
    }
    private static async Task<IResult> Queue(string owner, ReportPeriod period, ProgressFilter? filter, int maximumGames, LensDbContext db, InsightOptions options, CancellationToken ct)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
            var capture = new ReportCapture(db); var inputs = await capture.Capture(owner, period, filter, options, deadline.Token, maximumGames);
            var job = await capture.Enqueue(owner, inputs, deadline.Token);
            return job is null ? Results.Ok(new { state = "empty", period, detail = "No new completed review activity was found in this completed local week." })
                : Results.Accepted($"/api/reports/{job.Id}", View(job));
        }
        catch (ReportBudgetReached) { return Budget(); }
        catch (ReportDataChanged) { return DataChanged(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Results.Problem(statusCode: 503, title: "The selection took too long", detail: "Try fewer games or a smaller date range." ); }
    }
    private static IResult Budget() => Results.Problem(statusCode: 429, title: "Report budget reached", detail: "One report may be active at a time, with twenty new or retried snapshots per rolling day. The global pending queue is also bounded.");
    private static IResult DataChanged() => Results.Problem(statusCode: 409, title: "Your data changed", detail: "The workspace changed during capture. Reload and choose a fresh selection.");
    public static object View(ReportSnapshot report, bool includeResult = true)
    {
        var inputs = JsonSerializer.Deserialize<ReportInputs>(report.InputsJson)!;
        return new { report.Id, report.Kind, report.Revision, report.State, report.Version, report.CreatedAt, report.UpdatedAt, report.RulesVersion,
            report.Error, inputs.Period, inputs.PreviousPeriod, inputs.Filter, inputs.MaximumGames, inputs.CapturedAt, inputs.DetectorVersion, inputs.DetectorOptions,
            result = includeResult && report.ResultJson is not null ? JsonSerializer.Deserialize<ReportResult>(report.ResultJson) : null };
    }
}

public sealed record WeeklyReportRequest(DateOnly? WeekFrom = null, int MaximumGames = 50);
