using System.Security.Claims;
using System.Text.Json;
using ChessLens.Core.Insights;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessLens.Api;

public static class InsightEndpoints
{
    public static void MapInsights(this WebApplication app)
    {
        var api = app.MapGroup("/api/insights").RequireAuthorization().RequireRateLimiting("api");
        api.MapPost("", async (InsightFilter filter, ClaimsPrincipal user, LensDbContext db, IOptions<InsightOptions> configuration, CancellationToken ct) =>
        {
            filter.Validate(); configuration.Value.Validate();
            filter = filter with { From = filter.From.ToUniversalTime(), To = filter.To.ToUniversalTime() };
            var owner = user.Owner(); var json = JsonSerializer.Serialize(filter);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner}, 0))", ct);
            var active = await db.InsightSnapshots.SingleOrDefaultAsync(j => j.OwnerId == owner && (j.State == "queued" || j.State == "running"), ct);
            if (active is not null) return active.FilterJson == json ? Results.Accepted($"/api/insights/{active.Id}", View(active))
                : Results.Problem(statusCode: 429, title: "An insight review is already active", detail: "Wait for it to complete or cancel it before changing the selection.");
            if (await db.InsightSnapshots.CountAsync(j => j.OwnerId == owner && j.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1), ct) >= 20)
                return Results.Problem(statusCode: 429, title: "Daily insight budget reached", detail: "The limit is 20 snapshot requests per rolling day.");
            var snapshot = new InsightSnapshot { OwnerId = owner, FilterJson = json, OptionsJson = JsonSerializer.Serialize(configuration.Value) };
            db.InsightSnapshots.Add(snapshot); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Results.Accepted($"/api/insights/{snapshot.Id}", View(snapshot));
        });
        api.MapGet("", async (ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var job = await db.InsightSnapshots.AsNoTracking().Where(j => j.OwnerId == user.Owner()).OrderByDescending(j => j.CreatedAt).FirstOrDefaultAsync(ct);
            return Results.Json(job is null ? null : View(job));
        });
        api.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var job = await db.InsightSnapshots.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.OwnerId == user.Owner(), ct);
            return job is null ? Results.NotFound() : Results.Ok(View(job));
        });
        api.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var affected = await db.InsightSnapshots.Where(j => j.Id == id && j.OwnerId == user.Owner() && (j.State == "queued" || j.State == "running"))
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "cancelled").SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            return affected == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
    private static object View(InsightSnapshot job) => new { job.Id, job.State, job.Version, job.CreatedAt, job.UpdatedAt, job.DetectorVersion, job.Error,
        filter = JsonSerializer.Deserialize<InsightFilter>(job.FilterJson), result = job.ResultJson is null ? null : JsonSerializer.Deserialize<InsightResult>(job.ResultJson) };
}
