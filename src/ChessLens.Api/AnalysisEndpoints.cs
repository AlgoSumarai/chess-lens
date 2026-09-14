using System.Security.Claims;
using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Api;

public static class AnalysisEndpoints
{
    public static void MapAnalysis(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().RequireRateLimiting("api");
        api.MapPost("/games/{id:guid}/analysis", Queue);
        api.MapGet("/games/{id:guid}/analysis", async (Guid id, ClaimsPrincipal principal, LensDbContext db, CancellationToken ct) =>
        {
            var owner = principal.Owner();
            if (!await db.UserGames.AnyAsync(g => g.GameId == id && g.OwnerId == owner, ct)) return Results.NotFound();
            var run = await db.AnalysisRuns.AsNoTracking().Include(r => r.Moves).Where(r => r.OwnerId == owner && r.GameId == id)
                .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
            return Results.Json(run is null ? null : View(run));
        });
        api.MapGet("/analysis/{id:guid}", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var run = await db.AnalysisRuns.AsNoTracking().Include(r => r.Moves).SingleOrDefaultAsync(r => r.Id == id && r.OwnerId == user.Owner(), ct);
            return run is null ? Results.NotFound() : Results.Ok(View(run));
        });
        api.MapPost("/analysis/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var count = await db.AnalysisRuns.Where(r => r.Id == id && r.OwnerId == user.Owner() && (r.State == "running" || r.State == "queued"))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true).SetProperty(r => r.Version, r => r.Version + 1)
                    .SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), ct);
            return count == 0 ? Results.NotFound() : Results.Accepted($"/api/analysis/{id}");
        });
    }

    private static async Task<IResult> Queue(Guid id, AnalysisRequest input, ClaimsPrincipal principal, LensDbContext db, CancellationToken ct)
    {
        if (input.Profile is not ("quick" or "deep")) throw new ArgumentException("Choose a quick or deep review.");
        var owner = principal.Owner();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner}, 0))", ct);
        var game = await db.UserGames.AsNoTracking().Where(g => g.OwnerId == owner && g.GameId == id).Select(g => new { plies = g.Game.Moves.Count }).SingleOrDefaultAsync(ct);
        if (game is null) return Results.NotFound();
        var active = await db.AnalysisRuns.FirstOrDefaultAsync(r => r.OwnerId == owner && r.GameId == id && (r.State == "queued" || r.State == "running"), ct);
        if (active is not null) return Results.Accepted($"/api/analysis/{active.Id}", View(active));
        if (await db.AnalysisRuns.CountAsync(r => r.OwnerId == owner && (r.State == "queued" || r.State == "running"), ct) >= 3)
            return Results.Problem(statusCode: 429, title: "Analysis queue is full", detail: "Wait for a review to complete or cancel an active one.");
        if (await db.AnalysisRuns.Where(r => r.OwnerId == owner && r.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1)).SumAsync(r => r.TotalPlies, ct) + game.plies > 5000)
            return Results.Problem(statusCode: 429, title: "Daily analysis budget reached", detail: "The daily budget is 5,000 half-moves, including queued work.");
        var run = new AnalysisRun { OwnerId = owner, GameId = id, Profile = input.Profile, TotalPlies = game.plies };
        db.AnalysisRuns.Add(run);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Results.Accepted($"/api/analysis/{run.Id}", View(run));
    }

    public static object View(AnalysisRun r) => new { r.Id, r.GameId, r.State, r.Profile, r.AnalysisVersion, r.EngineVersion,
        classificationSettings = r.ClassificationSettingsJson is null ? new AnalysisOptions() : JsonSerializer.Deserialize<AnalysisOptions>(r.ClassificationSettingsJson),
        r.CompletedPlies, r.TotalPlies, r.Version, r.CancelRequested, r.Error, r.ElapsedMilliseconds, r.PeakEngineMemoryBytes,
        moves = r.Moves.OrderBy(m => m.Ply).Select(MoveView) };
    public static object MoveView(MoveAnalysis m) => new { m.Ply, m.PlayedMove, m.BestMove, m.Side,
        bestScore = new EngineScore(m.BestScoreKind, m.BestScoreValue), playedScore = new EngineScore(m.PlayedScoreKind, m.PlayedScoreValue),
        m.CentipawnLoss, m.Classification, m.MateTransition, m.Provisional, m.Depth, m.Nodes, m.Complete,
        pv = JsonSerializer.Deserialize<SearchResult>(m.BestSearchJson)?.Lines.FirstOrDefault()?.Pv ?? [] };
}

public sealed record AnalysisRequest(string Profile = "quick");
