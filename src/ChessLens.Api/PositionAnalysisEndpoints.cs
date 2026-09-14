using System.Security.Claims;
using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Api;

public static class PositionAnalysisEndpoints
{
    public static void MapPositionAnalysis(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().RequireRateLimiting("api");
        api.MapPost("/games/{id:guid}/position-analysis", Queue);
        api.MapGet("/position-analysis/{id:guid}", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var job = await db.PositionAnalysisJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.OwnerId == user.Owner(), ct);
            return job is null ? Results.NotFound() : Results.Ok(View(job));
        });
    }
    private static async Task<IResult> Queue(Guid id, PositionAnalysisRequest input, ClaimsPrincipal user, LensDbContext db, CancellationToken ct)
    {
        if (input.ChannelId == Guid.Empty || input.Sequence is < 1 or > 1_000_000 || input.Variation.Length > 100)
            throw new ArgumentException("Choose a valid review channel, sequence and at most 100 variation half-moves.");
        var owner = user.Owner();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner}, 0))", ct);
        var association = await db.UserGames.AsNoTracking().Include(g => g.Game).ThenInclude(g => g.Moves)
            .SingleOrDefaultAsync(g => g.OwnerId == owner && g.GameId == id, ct);
        var game = association?.Game;
        if (game is null) return Results.NotFound();
        var channel = await db.PositionReviewChannels.SingleOrDefaultAsync(c => c.Id == input.ChannelId, ct);
        if (channel is not null && (channel.OwnerId != owner || channel.GameId != id)) return Results.NotFound();
        if (channel is not null && input.Sequence <= channel.Sequence)
        {
            var previous = input.Sequence == channel.Sequence
                ? await db.PositionAnalysisJobs.SingleOrDefaultAsync(j => j.ChannelId == input.ChannelId && j.Sequence == input.Sequence, ct) : null;
            return previous is not null ? Results.Accepted($"/api/position-analysis/{previous.Id}", View(previous))
                : Results.Problem(statusCode: 409, title: "Position request superseded", detail: "A newer navigation request is already recorded.");
        }
        if (channel is null)
        {
            if (await db.PositionReviewChannels.CountAsync(c => c.OwnerId == owner && c.UpdatedAt > DateTimeOffset.UtcNow.AddDays(-1), ct) >= 200)
                return Results.Problem(statusCode: 429, title: "Daily review channel limit reached");
            channel = new() { Id = input.ChannelId, OwnerId = owner, GameId = id };
            db.PositionReviewChannels.Add(channel);
        }
        PositionAnalysisJob? job = null;
        if (!input.CancelOnly)
        {
            if (input.Ply < 0 || input.Ply > game.Moves.Count) throw new ArgumentException("Choose a position in this completed game.");
            if (await db.PositionAnalysisJobs.CountAsync(j => j.OwnerId == owner && j.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1), ct) >= 200)
                return Results.Problem(statusCode: 429, title: "Daily position analysis budget reached", detail: "The limit is 200 position reviews per day, including cancelled work.");
            if (await db.PositionAnalysisJobs.CountAsync(j => j.OwnerId == owner && j.ChannelId != input.ChannelId && (j.State == "running" || j.State == "queued"), ct) >= 2)
                return Results.Problem(statusCode: 429, title: "Position review queue is full", detail: "Stop an active position review before starting another.");
            var history = game.Moves.OrderBy(m => m.Ply).Take(input.Ply).Select(m => m.Uci).Concat(input.Variation).ToArray();
            var board = ChessRules.Replay(game.InitialFen, history);
            if (ChessRules.View(board).LegalMoves.Length == 0) throw new ArgumentException("This position has ended; there is no continuation to analyse.");
            job = new() { OwnerId = owner, GameId = id, ChannelId = channel.Id, Sequence = input.Sequence, Ply = input.Ply,
                InitialFen = game.InitialFen, HistoryJson = JsonSerializer.Serialize(history), Fen = board.ToFen() };
        }
        channel.Sequence = input.Sequence; channel.UpdatedAt = DateTimeOffset.UtcNow;
        await db.PositionAnalysisJobs.Where(j => j.ChannelId == channel.Id && (j.State == "running" || j.State == "queued"))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "cancelled").SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
        if (job is not null) db.PositionAnalysisJobs.Add(job);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return job is null ? Results.NoContent() : Results.Accepted($"/api/position-analysis/{job.Id}", View(job));
    }
    public static object View(PositionAnalysisJob job) => new { job.Id, job.GameId, job.ChannelId, job.Sequence, job.Ply, job.Fen,
        job.State, job.Version, job.Error, provisional = true,
        result = job.ResultJson is null ? null : JsonSerializer.Deserialize<SearchResult>(job.ResultJson) };
}

public sealed record PositionAnalysisRequest(Guid ChannelId, int Sequence, int Ply, string[] Variation, bool CancelOnly = false);
