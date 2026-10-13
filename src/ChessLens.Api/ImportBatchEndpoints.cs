using System.Security.Claims;
using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Api;

public static class ImportBatchEndpoints
{
    public static void MapImportBatches(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().RequireRateLimiting("api");
        api.MapPost("/jobs/{id:guid}/analysis", Queue);
        api.MapGet("/jobs/{id:guid}/analysis", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            if (!await db.ImportJobs.AnyAsync(j => j.Id == id && j.OwnerId == user.Owner(), ct)) return Results.NotFound();
            var batch = await db.ImportAnalysisBatches.AsNoTracking().Where(b => b.ImportJobId == id && b.OwnerId == user.Owner())
                .OrderByDescending(b => b.CreatedAt).FirstOrDefaultAsync(ct);
            return batch is null ? Results.Text("null", "application/json") : Results.Ok(await View(db, batch, ct));
        });
        api.MapGet("/analysis-batches/{id:guid}", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var batch = await db.ImportAnalysisBatches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id && b.OwnerId == user.Owner(), ct);
            return batch is null ? Results.NotFound() : Results.Ok(await View(db, batch, ct));
        });
        api.MapPost("/analysis-batches/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var count = await db.ImportAnalysisBatches.Where(b => b.Id == id && b.OwnerId == user.Owner() && (b.State == "queued" || b.State == "running"))
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.CancelRequested, true).SetProperty(b => b.Version, b => b.Version + 1)
                    .SetProperty(b => b.UpdatedAt, DateTimeOffset.UtcNow), ct);
            return count == 0 ? Results.NotFound() : Results.Accepted($"/api/analysis-batches/{id}");
        });
    }

    private static async Task<IResult> Queue(Guid id, ImportBatchRequest input, ClaimsPrincipal principal, LensDbContext db, CancellationToken ct)
    {
        if (input.Profile is not ("quick" or "deep") || input.MaximumGames is < 1 or > 20)
            throw new ArgumentException("Choose quick or deep analysis and 1–20 games from this batch.");
        var owner = principal.Owner();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner}, 0))", ct);
        var job = await db.ImportJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.OwnerId == owner, ct);
        if (job is null) return Results.NotFound();
        if (job.State is "queued" or "running") return Results.Conflict(new { detail = "Wait for the import to finish before selecting its games for analysis." });
        var existing = await db.ImportAnalysisBatches.AsNoTracking().SingleOrDefaultAsync(b => b.OwnerId == owner && b.ImportJobId == id && b.Profile == input.Profile, ct);
        if (existing is not null) return Results.Accepted($"/api/analysis-batches/{existing.Id}", await View(db, existing, ct));
        if (await db.ImportAnalysisBatches.AnyAsync(b => b.OwnerId == owner && (b.State == "queued" || b.State == "running"), ct))
            return Results.Problem(statusCode: 429, title: "A batch is already being reviewed", detail: "Wait for it to finish or cancel it before starting another batch.");
        var games = await db.ImportJobGames.Where(g => g.JobId == id && g.OwnerId == owner).OrderBy(g => g.Ordinal).Take(input.MaximumGames)
            .Select(g => new { g.GameId, plies = db.Moves.Count(m => m.GameId == g.GameId) }).ToListAsync(ct);
        if (games.Count == 0) throw new ArgumentException("No valid games were saved from this import.");
        var selected = games.Select(g => g.GameId).ToArray();
        var reviews = await db.AnalysisRuns.Where(r => r.OwnerId == owner && selected.Contains(r.GameId) &&
            r.Profile == input.Profile && r.AnalysisVersion == "chesslens-analysis-v3" && (r.State == "waiting" || r.State == "queued" || r.State == "running" || r.State == "completed"))
            .OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var fresh = games.Where(g => !reviews.Any(r => r.GameId == g.GameId)).ToArray();
        await ResourceAdmission.Reserve(db, owner, WorkBudget.Analysis, ct, units: fresh.Sum(g => g.plies), pendingJobs: fresh.Length + 1);
        var batch = new ImportAnalysisBatch { OwnerId = owner, ImportJobId = id, Profile = input.Profile, RunIdsJson = "[]", ReusedReviews = games.Count - fresh.Length };
        var ids = new List<Guid>();
        foreach (var game in games)
        {
            var review = reviews.FirstOrDefault(r => r.GameId == game.GameId);
            if (review is null)
            {
                review = new AnalysisRun { OwnerId = owner, GameId = game.GameId, BatchId = batch.Id, Profile = input.Profile, TotalPlies = game.plies, State = "waiting" };
                db.AnalysisRuns.Add(review);
            }
            ids.Add(review.Id);
        }
        batch.RunIdsJson = JsonSerializer.Serialize(ids); db.ImportAnalysisBatches.Add(batch);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Results.Accepted($"/api/analysis-batches/{batch.Id}", await View(db, batch, ct));
    }

    public static async Task<object> View(LensDbContext db, ImportAnalysisBatch batch, CancellationToken ct)
    {
        var ids = JsonSerializer.Deserialize<Guid[]>(batch.RunIdsJson)!;
        var runs = await db.AnalysisRuns.AsNoTracking().Where(r => r.OwnerId == batch.OwnerId && ids.Contains(r.Id))
            .Select(r => new { r.Id, r.GameId, r.State, r.Profile, r.CompletedPlies, r.TotalPlies, r.Version, r.Error }).ToListAsync(ct);
        return new { batch.Id, batch.ImportJobId, batch.Profile, batch.State, batch.Version, batch.CancelRequested, batch.ReusedReviews,
            totalGames = ids.Length, completedGames = runs.Count(r => r.State == "completed"), failedGames = runs.Count(r => r.State == "failed"),
            completedPlies = runs.Sum(r => r.CompletedPlies), totalPlies = runs.Sum(r => r.TotalPlies), runs };
    }
}
public sealed record ImportBatchRequest(string Profile = "quick", int MaximumGames = 10);
