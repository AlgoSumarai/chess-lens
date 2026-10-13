using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChessLens.Core.Coaching;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessLens.Api;

public static class CoachingEndpoints
{
    public static void MapCoaching(this WebApplication app)
    {
        var api = app.MapGroup("/api/coaching").RequireAuthorization().RequireRateLimiting("api");
        api.MapPost("", Queue);
        api.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var job = await db.CoachingJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.OwnerId == user.Owner(), ct);
            return job is null ? Results.NotFound() : Results.Ok(View(job));
        });
        api.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var count = await db.CoachingJobs.Where(j => j.Id == id && j.OwnerId == user.Owner() && (j.State == "queued" || j.State == "running"))
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "cancelled").SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            return count == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static async Task<IResult> Queue(CoachingRequest input, ClaimsPrincipal principal, LensDbContext db, IOptions<CoachingOptions> configuration, CancellationToken ct)
    {
        if (input.Ply is < 1 or > 1000) throw new ArgumentException("Choose a saved half-move between 1 and 1,000.");
        configuration.Value.Validate(); var owner = principal.Owner();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner}, 0))", ct);
        var run = await db.AnalysisRuns.AsNoTracking().Include(r => r.Moves.Where(m => m.Ply == input.Ply))
            .SingleOrDefaultAsync(r => r.Id == input.RunId && r.OwnerId == owner, ct);
        if (run is null) return Results.NotFound();
        var association = await db.UserGames.AsNoTracking().Include(g => g.Game).ThenInclude(g => g.Moves)
            .SingleOrDefaultAsync(g => g.OwnerId == owner && g.GameId == run.GameId, ct);
        if (association is null) return Results.NotFound();
        var move = run.Moves.SingleOrDefault() ?? throw new ArgumentException("That move has no saved analysis.");
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == owner, ct);
        var evidence = CoachingRules.Build(association.Game, run, move, user.CoachingLevel);
        var requested = user.UseModelCoaching && configuration.Value.Enabled;
        var model = requested ? configuration.Value.Model : null;
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { evidence.Id, requested, model, version = CoachingRules.Version }))));
        var existing = await db.CoachingJobs.SingleOrDefaultAsync(j => j.OwnerId == owner && j.CacheIdentity == key, ct);
        if (existing is not null)
        {
            if (existing.State is "failed" or "cancelled")
            {
                // Retrying displays factual evidence without repeating any already attempted paid request.
                existing.State = "completed"; existing.Error = null; existing.LeaseUntil = null; existing.Version++; existing.UpdatedAt = DateTimeOffset.UtcNow;
                existing.ResultJson = JsonSerializer.Serialize(CoachingRules.Factual(evidence, "previous-request-interrupted"), CoachingRules.Json);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
            return Results.Accepted($"/api/coaching/{existing.Id}", View(existing));
        }
        if (requested && await db.CoachingJobs.CountAsync(j => j.OwnerId == owner && (j.State == "queued" || j.State == "running"), ct) >= 3)
            return Results.Problem(statusCode: 429, title: "Coaching limit reached", detail: "Saved explanations remain available. Wait for an active explanation or review the engine's recorded line.");
        var immediate = !requested || !configuration.Value.Configured;
        await ResourceAdmission.Reserve(db, owner, WorkBudget.Coaching, ct, pendingJobs: immediate ? 0 : 1);
        var job = new CoachingJob { OwnerId = owner, GameId = run.GameId, RunId = run.Id, Ply = move.Ply, CacheIdentity = key,
            EvidenceJson = JsonSerializer.Serialize(evidence, CoachingRules.Json), ModelRequested = requested, Model = model,
            State = immediate ? "completed" : "queued", ResultJson = immediate ? JsonSerializer.Serialize(CoachingRules.Factual(evidence, requested ? "provider-unavailable" : null), CoachingRules.Json) : null };
        db.CoachingJobs.Add(job); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Results.Accepted($"/api/coaching/{job.Id}", View(job));
    }
    public static object View(CoachingJob j) => new { j.Id, j.GameId, j.RunId, j.Ply, j.State, j.Version, j.Error,
        j.PromptVersion, j.Model, j.CreatedAt, result = j.ResultJson is null ? null : JsonSerializer.Deserialize<CoachingExplanation>(j.ResultJson, CoachingRules.Json) };
}
public sealed record CoachingRequest(Guid RunId, int Ply);
