using System.Security.Claims;
using System.Text.Json;
using ChessLens.Core.Games;
using ChessLens.Core.Imports;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Api;

public static class GameEndpoints
{
    public static void MapGames(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().RequireRateLimiting("api");
        api.MapPost("/imports/pgn", Import);
        api.MapGet("/jobs", async (ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var jobs = await db.ImportJobs.AsNoTracking().Where(j => j.OwnerId == user.Owner()).OrderByDescending(j => j.CreatedAt).Take(30).ToListAsync(ct);
            return Results.Ok(jobs.Select(JobView));
        });
        api.MapGet("/jobs/{id:guid}", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var job = await db.ImportJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.OwnerId == user.Owner(), ct);
            return job is null ? Results.NotFound() : Results.Ok(JobView(job));
        });
        api.MapPost("/jobs/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var count = await db.ImportJobs.Where(j => j.Id == id && j.OwnerId == user.Owner() && (j.State == "queued" || j.State == "running"))
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.CancelRequested, true).SetProperty(j => j.Version, j => j.Version + 1), ct);
            return count == 0 ? Results.NotFound() : Results.Accepted($"/api/jobs/{id}");
        });
        api.MapGet("/games", Library);
        api.MapGet("/games/{id:guid}", Game);
        api.MapPut("/games/{id:guid}/identity", Identity);
        api.MapPost("/games/{id:guid}/position", Position);
    }

    private static async Task<IResult> Import(PgnImportRequest input, ClaimsPrincipal principal, LensDbContext db, CancellationToken ct)
    {
        if (input.MaxGames is < 1 or > PgnReader.MaxGames || System.Text.Encoding.UTF8.GetByteCount(input.Pgn) > PgnReader.MaxBytes || string.IsNullOrWhiteSpace(input.Pgn))
            throw new ArgumentException("Supply a PGN up to 5 MB and choose 1–200 games.");
        if (input.PlayerName?.Length > 200) throw new ArgumentException("Player names may have at most 200 characters.");
        var owner = principal.Owner();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner}, 0))", ct);
        if (await db.ImportJobs.CountAsync(j => j.OwnerId == owner && (j.State == "queued" || j.State == "running"), ct) >= 3)
            return Results.Problem(statusCode: 429, title: "Import limit reached", detail: "Wait for an active import to finish or cancel it.");
        if (await db.ImportJobs.CountAsync(j => j.OwnerId == owner && j.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1), ct) >= 20)
            return Results.Problem(statusCode: 429, title: "Daily import limit reached");
        var job = new ImportJob { OwnerId = owner, PayloadJson = JsonSerializer.Serialize(input) };
        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Accepted($"/api/jobs/{job.Id}", JobView(job));
    }

    private static async Task<IResult> Library(ClaimsPrincipal principal, LensDbContext db, CancellationToken ct,
        int page = 1, string? source = null, string? colour = null, string? opening = null, string? timeControl = null, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        if (page is < 1 or > 100000) throw new ArgumentException("Invalid page.");
        var query = db.UserGames.AsNoTracking().Where(g => g.OwnerId == principal.Owner());
        if (source is not null) query = query.Where(g => g.Game.Provider == source);
        if (colour is not null) query = query.Where(g => g.PlayerColour == colour);
        if (opening is not null) query = query.Where(g => g.Game.Opening == opening);
        if (timeControl is not null) query = query.Where(g => g.Game.TimeControl == timeControl);
        if (from is not null) query = query.Where(g => g.Game.PlayedAt >= from);
        if (to is not null) query = query.Where(g => g.Game.PlayedAt < to);
        var total = await query.CountAsync(ct);
        var games = await query.OrderByDescending(g => g.Game.PlayedAt ?? g.AddedAt).ThenBy(g => g.GameId).Skip((page - 1) * 20).Take(20)
            .Select(g => new { id = g.GameId, g.PlayerColour, g.Game.White, g.Game.Black, g.Game.WhiteRating, g.Game.BlackRating,
                g.Game.Result, g.Game.PlayedAt, g.Game.Provider, g.Game.TimeControl, g.Game.Opening, plies = g.Game.Moves.Count }).ToListAsync(ct);
        return Results.Ok(new { items = games, page, pageSize = 20, total });
    }

    private static async Task<IResult> Game(Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct)
    {
        var association = await db.UserGames.AsNoTracking().Include(g => g.Game).ThenInclude(g => g.Moves)
            .SingleOrDefaultAsync(g => g.GameId == id && g.OwnerId == user.Owner(), ct);
        if (association is null) return Results.NotFound();
        var game = association.Game;
        return Results.Ok(new { game.Id, association.PlayerColour, game.White, game.Black, game.WhiteRating, game.BlackRating, game.Result,
            game.PlayedAt, game.Provider, game.Url, game.Termination, game.TimeControl, game.Opening, game.Eco, game.RawPgn, game.InitialFen,
            moves = game.Moves.OrderBy(m => m.Ply) });
    }

    private static async Task<IResult> Identity(Guid id, IdentityRequest input, ClaimsPrincipal user, LensDbContext db, CancellationToken ct)
    {
        if (input.Colour is not ("white" or "black")) throw new ArgumentException("Choose White or Black.");
        var count = await db.UserGames.Where(g => g.GameId == id && g.OwnerId == user.Owner())
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.PlayerColour, input.Colour), ct);
        return count == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> Position(Guid id, PositionRequest input, ClaimsPrincipal user, LensDbContext db, CancellationToken ct)
    {
        var association = await db.UserGames.AsNoTracking().Include(g => g.Game).ThenInclude(g => g.Moves)
            .SingleOrDefaultAsync(g => g.GameId == id && g.OwnerId == user.Owner(), ct);
        if (association is null) return Results.NotFound();
        if (input.Ply < 0 || input.Ply > association.Game.Moves.Count || input.Variation.Length > 100)
            throw new ArgumentException("Choose a valid game position and at most 100 variation half-moves.");
        var history = association.Game.Moves.OrderBy(m => m.Ply).Take(input.Ply).Select(m => m.Uci).Concat(input.Variation);
        return Results.Ok(ChessRules.View(ChessRules.Replay(association.Game.InitialFen, history)));
    }

    public static object JobView(ImportJob j) => new { j.Id, j.Source, j.State, j.Imported, j.Duplicates, j.Processed, j.Total,
        failures = JsonSerializer.Deserialize<ImportFailure[]>(j.FailuresJson), j.Error, j.Version, j.CancelRequested, j.CreatedAt, j.UpdatedAt };
}

public sealed record IdentityRequest(string Colour);
public sealed record PositionRequest(int Ply, string[] Variation);
