using System.Security.Claims;
using System.Text.Json;
using ChessLens.Core.Games;
using ChessLens.Core.Persistence;
using ChessLens.Core.Training;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Api;

public static class PuzzleEndpoints
{
    public static void MapPuzzles(this WebApplication app)
    {
        var api = app.MapGroup("/api/puzzles").RequireAuthorization().RequireRateLimiting("api");
        api.MapPost("", async (CreatePuzzle request, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var owner = user.Owner(); await using var tx = await db.Database.BeginTransactionAsync(ct);
            await Lock(db, owner, ct);
            var prior = await db.Puzzles.SingleOrDefaultAsync(p => p.OwnerId == owner && p.RunId == request.RunId && p.Ply == request.Ply && p.ValidationVersion == PuzzleValidator.Version, ct);
            if (prior is not null) return Results.Accepted($"/api/puzzles/{prior.Id}", PuzzleView(prior));
            var run = await db.AnalysisRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == request.RunId && r.OwnerId == owner, ct);
            if (run is null) return Results.NotFound();
            var move = await db.MoveAnalyses.AsNoTracking().SingleOrDefaultAsync(m => m.RunId == run.Id && m.Ply == request.Ply, ct);
            var colour = await db.UserGames.Where(g => g.OwnerId == owner && g.GameId == run.GameId).Select(g => g.PlayerColour).SingleAsync(ct);
            if (run.State != "completed" || move is null || !PuzzleProcessor.Eligible(move) || move.Side != colour)
                throw new ArgumentException("Choose your player and a completed, verified mistake to validate as a puzzle.");
            if (await db.Puzzles.CountAsync(p => p.OwnerId == owner && (p.State == "queued" || p.State == "running"), ct) >= 2 ||
                await db.Puzzles.CountAsync(p => p.OwnerId == owner && p.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1), ct) >= 20)
                return Results.Problem(statusCode: 429, title: "Puzzle validation budget reached", detail: "At most two active and 20 new candidates per rolling day are allowed.");
            var puzzle = new Puzzle { OwnerId = owner, RunId = run.Id, GameId = run.GameId, Ply = request.Ply };
            db.Puzzles.Add(puzzle); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Results.Accepted($"/api/puzzles/{puzzle.Id}", PuzzleView(puzzle));
        });
        api.MapGet("", async (ClaimsPrincipal user, LensDbContext db, CancellationToken ct, int page = 1) =>
        {
            if (page is < 1 or > 10000) throw new ArgumentException("Page must be between 1 and 10000.");
            var query = db.Puzzles.AsNoTracking().Where(p => p.OwnerId == user.Owner());
            var count = await query.CountAsync(ct);
            var rows = await query.OrderBy(p => p.State == "ready" ? 0 : 1).ThenBy(p => p.ReviewDueAt).ThenByDescending(p => p.CreatedAt).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
            return Results.Ok(new { page, total = count, items = rows.Select(PuzzleView) });
        });
        api.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var puzzle = await db.Puzzles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.OwnerId == user.Owner(), ct);
            return puzzle is null ? Results.NotFound() : Results.Ok(PuzzleView(puzzle));
        });
        api.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var changed = await db.Puzzles.Where(p => p.Id == id && p.OwnerId == user.Owner() && (p.State == "queued" || p.State == "running"))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.State, "cancelled").SetProperty(p => p.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(p => p.Version, p => p.Version + 1).SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow), ct);
            return changed == 0 ? Results.NotFound() : Results.NoContent();
        });
        api.MapPost("/{id:guid}/attempts", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var owner = user.Owner(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, owner, ct);
            var puzzle = await db.Puzzles.SingleOrDefaultAsync(p => p.Id == id && p.OwnerId == owner, ct);
            if (puzzle is null) return Results.NotFound();
            if (puzzle.State != "ready") return Results.Conflict(new { detail = "This candidate is not a published puzzle." });
            var active = await db.PuzzleAttempts.SingleOrDefaultAsync(a => a.PuzzleId == id && a.OwnerId == owner && a.State == "active", ct);
            if (active is not null && active.StartedAt < DateTimeOffset.UtcNow.AddDays(-1))
            { Finish(active, puzzle, "expired"); await db.SaveChangesAsync(ct); active = null; }
            if (active is not null) return Results.Ok(AttemptView(active, puzzle));
            if (await db.PuzzleAttempts.CountAsync(a => a.OwnerId == owner && a.StartedAt > DateTimeOffset.UtcNow.AddDays(-1), ct) >= 200)
                return Results.Problem(statusCode: 429, title: "Daily attempt limit reached", detail: "Up to 200 puzzle attempts may be started per rolling day.");
            var attempt = new PuzzleAttempt { OwnerId = owner, PuzzleId = id };
            db.PuzzleAttempts.Add(attempt); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Results.Created($"/api/puzzles/attempts/{attempt.Id}", AttemptView(attempt, puzzle));
        });
        api.MapGet("/attempts/{id:guid}", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var attempt = await db.PuzzleAttempts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.OwnerId == user.Owner(), ct);
            if (attempt is null) return Results.NotFound();
            var puzzle = await db.Puzzles.AsNoTracking().SingleAsync(p => p.Id == attempt.PuzzleId && p.OwnerId == user.Owner(), ct);
            return Results.Ok(AttemptView(attempt, puzzle));
        });
        api.MapPost("/attempts/{id:guid}/moves", (Guid id, AttemptCommand command, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) => Act(id, command, "move", user, db, ct));
        api.MapPost("/attempts/{id:guid}/hint", (Guid id, AttemptCommand command, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) => Act(id, command, "hint", user, db, ct));
        api.MapPost("/attempts/{id:guid}/reveal", (Guid id, AttemptCommand command, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) => Act(id, command, "reveal", user, db, ct));
    }

    private static async Task<IResult> Act(Guid id, AttemptCommand command, string action, ClaimsPrincipal user, LensDbContext db, CancellationToken ct)
    {
        var owner = user.Owner(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, owner, ct);
        var attempt = await db.PuzzleAttempts.SingleOrDefaultAsync(a => a.Id == id && a.OwnerId == owner, ct);
        if (attempt is null) return Results.NotFound();
        var puzzle = await db.Puzzles.SingleAsync(p => p.Id == attempt.PuzzleId && p.OwnerId == owner, ct);
        if (attempt.Version != command.Version || attempt.State != "active") return Results.Conflict(new { detail = "The attempt changed. Reload its current position." });
        if (attempt.StartedAt < DateTimeOffset.UtcNow.AddDays(-1)) Finish(attempt, puzzle, "expired");
        else
        {
            var solution = Read(puzzle); var history = JsonSerializer.Deserialize<string[]>(attempt.HistoryJson)!;
            var node = PuzzleReview.Node(solution, history) ?? throw new InvalidOperationException("An active attempt has no decision remaining.");
            if (action == "hint")
            { if (attempt.NodeHintsUsed < 3) { attempt.NodeHintsUsed++; attempt.HintsUsed++; } }
            else if (action == "reveal") Finish(attempt, puzzle, "revealed");
            else
            {
                var board = ChessRules.Replay(solution.StartingPosition.InitialFen, solution.StartingPosition.History.Concat(history));
                ChessRules.PlayUci(board, command.Move ?? "");
                var choice = node.Choices.SingleOrDefault(c => c.Move == command.Move);
                var journal = JsonSerializer.Deserialize<List<PuzzleMoveEntry>>(attempt.JournalJson)!;
                journal.Add(new(command.Move!, choice is not null, attempt.HintsUsed, DateTimeOffset.UtcNow)); attempt.JournalJson = JsonSerializer.Serialize(journal);
                if (choice is null) Finish(attempt, puzzle, "failed");
                else
                {
                    string[] updatedHistory = choice.Reply is null ? [.. history, choice.Move] : [.. history, choice.Move, choice.Reply];
                    attempt.HistoryJson = JsonSerializer.Serialize(updatedHistory);
                    attempt.NodeHintsUsed = 0;
                    if (choice.Resolved) Finish(attempt, puzzle, "solved");
                }
            }
        }
        attempt.Version++; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Results.Ok(AttemptView(attempt, puzzle));
    }
    private static void Finish(PuzzleAttempt attempt, Puzzle puzzle, string outcome)
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = PuzzleReview.Schedule(outcome, attempt.HintsUsed, puzzle.ReviewStreak, now);
        attempt.State = outcome; attempt.FinishedAt = now; attempt.SolveSeconds = (int)Math.Clamp((now - attempt.StartedAt).TotalSeconds, 0, 86400);
        attempt.ReviewDueAt = schedule.Due; puzzle.ReviewStreak = schedule.Streak; puzzle.ReviewDueAt = schedule.Due;
        puzzle.Version++; puzzle.UpdatedAt = now;
    }
    private static Task Lock(LensDbContext db, string owner, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner}, 0))", ct);
    private static PuzzleSolution Read(Puzzle puzzle) => JsonSerializer.Deserialize<PuzzleSolution>(puzzle.SolutionJson!)!;
    private static object PuzzleView(Puzzle p)
    {
        var solution = p.SolutionJson is null ? null : Read(p);
        return new { p.Id, p.State, p.Version, p.CreatedAt, p.UpdatedAt, p.Error, p.ReviewDueAt, p.ValidationVersion,
            objective = solution?.Objective, materialTarget = solution?.MaterialTarget, side = solution?.Side, difficulty = "Short tactic · estimate, not a rating" };
    }
    private static object AttemptView(PuzzleAttempt attempt, Puzzle puzzle)
    {
        var solution = Read(puzzle); var history = JsonSerializer.Deserialize<string[]>(attempt.HistoryJson)!;
        var board = ChessRules.Replay(solution.StartingPosition.InitialFen, solution.StartingPosition.History.Concat(history));
        var view = ChessRules.View(board); var node = PuzzleReview.Node(solution, history);
        var next = node?.Choices[0].Move;
        var hint = attempt.NodeHintsUsed switch { 1 => solution.Objective == "mate" ? "Look for forcing checks and the king's escape squares." : "Check forcing captures and the opponent's best recapture.",
            2 => $"Look at the piece on {next?[..2]}.", >= 3 => $"Consider {next}.", _ => null };
        return new { attempt.Id, attempt.PuzzleId, attempt.State, attempt.Version, attempt.HintsUsed, attempt.StartedAt, attempt.SolveSeconds, attempt.ReviewDueAt,
            solution.Side, solution.Objective, solution.MaterialTarget, initialFen = solution.Root.Fen, view.Fen, legalMoves = attempt.State == "active" ? view.LegalMoves : [], history, hint,
            review = attempt.State == "active" ? null : new { puzzle.GameId, puzzle.RunId, puzzle.Ply, solution.Explanation, solution.Root, solution.Version,
                engine = solution.Searches[0].Result.Engine, journal = JsonSerializer.Deserialize<PuzzleMoveEntry[]>(attempt.JournalJson) } };
    }
}
public sealed record CreatePuzzle(Guid RunId, int Ply);
public sealed record AttemptCommand(long Version, string? Move = null);
