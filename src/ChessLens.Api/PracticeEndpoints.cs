using System.Security.Claims;
using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Insights;
using ChessLens.Core.Persistence;
using ChessLens.Core.Training;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Api;

public static class PracticeEndpoints
{
    public static void MapPractice(this WebApplication app)
    {
        var api = app.MapGroup("/api/practice").RequireAuthorization().RequireRateLimiting("api");
        api.MapPost("", Create);
        api.MapGet("", async (ClaimsPrincipal user, LensDbContext db, CancellationToken ct, int page = 1) =>
        {
            if (page is < 1 or > 10000) throw new ArgumentException("Choose a valid page.");
            var query = db.PracticeSessions.AsNoTracking().Where(s => s.OwnerId == user.Owner());
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(s => s.UpdatedAt).Skip((page - 1) * 20).Take(20).ToArrayAsync(ct);
            return Results.Ok(new { total, page, items = rows.Select(s => new { s.Id, s.Side, s.State, s.Version, s.CreatedAt, s.UpdatedAt, s.Error,
                objective = PracticeProcessor.Read<PracticeObjective>(s.ObjectiveJson) }) });
        });
        api.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var session = await db.PracticeSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.OwnerId == user.Owner(), ct);
            return session is null ? Results.NotFound() : Results.Ok(await View(session, db, ct));
        });
        api.MapGet("/{id:guid}/attempts/{attemptId:guid}", async (Guid id, Guid attemptId, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
        {
            var session = await db.PracticeSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.OwnerId == user.Owner(), ct);
            var attempt = session is null ? null : await db.PracticeAttempts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == attemptId && a.SessionId == id && a.State != "active", ct);
            return attempt is null ? Results.NotFound() : Results.Ok(AttemptView(session!, attempt, false));
        });
        foreach (var action in new[] { "moves", "hint", "undo", "retry", "cancel" })
        {
            var commandAction = action;
            api.MapPost($"/{{id:guid}}/{action}", (Guid id, PracticeCommand command, ClaimsPrincipal user, LensDbContext db, CancellationToken ct) =>
                Act(id, command, commandAction, user.Owner(), db, ct));
        }
    }

    private static async Task<IResult> Create(CreatePractice request, ClaimsPrincipal user, LensDbContext db, CancellationToken ct)
    {
        var owner = user.Owner(); await using var tx = await db.Database.BeginTransactionAsync(ct); await PracticeAdmission.LockOwner(db, owner, ct);
        var prior = await db.PracticeSessions.SingleOrDefaultAsync(s => s.OwnerId == owner && s.RunId == request.RunId && s.Ply == request.Ply && s.RulesVersion == PracticeRules.Version, ct);
        if (prior is not null) return Results.Ok(await View(prior, db, ct));
        var run = await db.AnalysisRuns.AsNoTracking().Include(r => r.Moves.Where(m => m.Ply == request.Ply))
            .SingleOrDefaultAsync(r => r.OwnerId == owner && r.Id == request.RunId, ct);
        if (run is null) return Results.NotFound();
        var association = await db.UserGames.AsNoTracking().Include(g => g.Game).ThenInclude(g => g.Moves)
            .SingleAsync(g => g.OwnerId == owner && g.GameId == run.GameId, ct);
        var move = run.Moves.SingleOrDefault();
        var evidence = move is null ? null : VerifiedEvidence.Read(association.Game, run, move);
        if (evidence is null || move is null || association.PlayerColour != move.Side || !PuzzleProcessor.Eligible(move))
            throw new ArgumentException("Choose your player and a completed, verified mistake to practise.");
        if (ChessRules.PracticeView(evidence.Position.InitialFen, evidence.Position.History, []).Endgame is not null)
            throw new ArgumentException("The starting practice position has already reached a terminal or claimable draw state. Choose another decision.");
        if (request.WeaknessSnapshotId is { } snapshotId)
        {
            var snapshot = await db.InsightSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == snapshotId && s.OwnerId == owner && s.State == "completed", ct);
            if (snapshot?.ResultJson is null) return Results.NotFound();
            var result = PracticeProcessor.Read<InsightResult>(snapshot.ResultJson);
            if (!result.Groups.Any(g => g.Summary.Findings.Any(f => f.Evidence.Any(e => e.RunId == run.Id && e.Ply == move.Ply))))
                throw new ArgumentException("That saved weakness does not contain this decision.");
        }
        if (!await PracticeAdmission.Reserve(db, owner, true, ct)) return Budget();
        var session = new PracticeSession { OwnerId = owner, GameId = run.GameId, RunId = run.Id, Ply = move.Ply, Side = move.Side,
            WeaknessSnapshotId = request.WeaknessSnapshotId, StartingPositionJson = JsonSerializer.Serialize(evidence.Position),
            ObjectiveJson = JsonSerializer.Serialize(PracticeRules.Objective(evidence.BestLine.Score)),
            SourceEvidenceJson = JsonSerializer.Serialize(new PracticeSourceEvidence(run.AnalysisVersion, run.ClassificationSettingsJson, move.Classification,
                move.PlayedMove, evidence.BestLine.Score, evidence.PlayedLine.Score, evidence.BestSearch, evidence.PlayedSearch)) };
        var attempt = new PracticeAttempt { SessionId = session.Id, ObjectiveJson = session.ObjectiveJson }; session.CurrentAttemptId = attempt.Id;
        db.PracticeSessions.Add(session); db.PracticeAttempts.Add(attempt); Queue(db, session, attempt, "initialize");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Results.Accepted($"/api/practice/{session.Id}", await View(session, db, ct));
    }

    private static async Task<IResult> Act(Guid id, PracticeCommand command, string action, string owner, LensDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await PracticeAdmission.LockOwner(db, owner, ct);
        var session = await db.PracticeSessions.SingleOrDefaultAsync(s => s.Id == id && s.OwnerId == owner, ct);
        if (session is null) return Results.NotFound();
        if (command.Version != session.Version || command.AttemptId != session.CurrentAttemptId)
            return Results.Conflict(new { detail = "The session changed. Reload its saved position before continuing." });
        if (session.RulesVersion != PracticeRules.Version && action is not ("undo" or "cancel"))
            return Results.Conflict(new { detail = "This session uses an earlier grading rule. End this attempt and open the exact original mistake to start practice with the current rules." });
        var attempt = await db.PracticeAttempts.SingleAsync(a => a.Id == session.CurrentAttemptId && a.SessionId == id, ct);
        var history = PracticeProcessor.Read<string[]>(attempt.HistoryJson);
        var start = PracticeProcessor.Read<EnginePosition>(session.StartingPositionJson);
        if (action is "moves" or "hint")
        {
            if (session.State != "active" || attempt.State != "active") return Results.Conflict(new { detail = "Wait for the current check, or start a new attempt." });
            var board = ChessRules.Replay(start.InitialFen, start.History.Concat(history));
            var view = ChessRules.PracticeView(start.InitialFen, start.History, history);
            if (view.SideToMove != session.Side || view.Endgame is not null) throw new ArgumentException("There is no player decision remaining.");
            if (action == "moves") ChessRules.PlayUci(board, command.Move ?? "");
            else if (attempt.HintsUsed >= 6) return Results.Conflict(new { detail = "This attempt has used its six hints. Continue playing or retry." });
            if (!await PracticeAdmission.Reserve(db, owner, false, ct)) return Budget();
            session.Version++; session.State = "waiting"; session.Error = null;
            if (action == "moves") { attempt.HistoryJson = JsonSerializer.Serialize(history.Append(command.Move!)); attempt.Hint = null; }
            else attempt.HintsUsed++;
            PracticeProcessor.Journal(attempt, action, command.Move, history.Length);
            Queue(db, session, attempt, action == "moves" ? "turn" : "hint");
        }
        else if (action == "undo")
        {
            if (attempt.State != "active" || session.State is not ("active" or "waiting") || history.Length == 0)
                return Results.Conflict(new { detail = "Only an active played decision can be undone. Retry a finished attempt instead." });
            if (attempt.UndosUsed >= 6) return Results.Conflict(new { detail = "This attempt has used its six undos. Continue or retry." });
            var length = history.Length % 2 == 0 ? history.Length - 2 : history.Length - 1;
            var grades = PracticeProcessor.Read<List<PracticeGrade>>(attempt.GradesJson);
            attempt.GradesJson = JsonSerializer.Serialize(grades.Take(length / 2)); attempt.HistoryJson = JsonSerializer.Serialize(history.Take(length));
            attempt.UndosUsed++; attempt.Hint = null; PracticeProcessor.Journal(attempt, "undo", null, length);
            session.Version++; session.State = "active"; session.Error = null; await CancelJobs(db, id, ct);
        }
        else if (action == "retry")
        {
            if (!await PracticeAdmission.Reserve(db, owner, false, ct, activate: session.State is not ("active" or "waiting" or "preparing"))) return Budget();
            await CancelJobs(db, id, ct);
            if (attempt.State == "active") { attempt.State = "retried"; attempt.FinishedAt = DateTimeOffset.UtcNow; }
            if (attempt.State == "retried") PracticeProcessor.Journal(attempt, "retry", null, history.Length);
            var next = new PracticeAttempt { SessionId = id, Number = attempt.Number + 1, ObjectiveJson = session.ObjectiveJson };
            session.CurrentAttemptId = next.Id; session.Version++; session.State = "preparing"; session.Error = null;
            db.PracticeAttempts.Add(next); Queue(db, session, next, "initialize");
        }
        else if (action == "cancel")
        {
            if (attempt.State != "active") return Results.Conflict(new { detail = "This attempt is already finished." });
            await CancelJobs(db, id, ct); session.State = "cancelled"; session.Version++; session.Error = null;
            attempt.State = "cancelled"; attempt.FinishedAt = DateTimeOffset.UtcNow; PracticeProcessor.Journal(attempt, "cancel", null, history.Length);
        }
        session.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Results.Ok(await View(session, db, ct));
    }
    private static Task CancelJobs(LensDbContext db, Guid id, CancellationToken ct) => db.PracticeJobs
        .Where(j => j.SessionId == id && (j.State == "queued" || j.State == "running"))
        .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "cancelled").SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null), ct);
    private static void Queue(LensDbContext db, PracticeSession session, PracticeAttempt attempt, string kind) => db.PracticeJobs.Add(new()
    { SessionId = session.Id, AttemptId = attempt.Id, SessionVersion = session.Version, Kind = kind });
    private static IResult Budget() => Results.Problem(statusCode: 429, title: "Practice budget reached",
        detail: "Up to two active sessions, forty new sessions and two hundred engine checks per rolling day are allowed. The global pending queue is also bounded. Resume existing practice or try later.");

    public static async Task<object> View(PracticeSession session, LensDbContext db, CancellationToken ct)
    {
        var attempt = await db.PracticeAttempts.AsNoTracking().SingleAsync(a => a.Id == session.CurrentAttemptId && a.SessionId == session.Id, ct);
        var attempts = await db.PracticeAttempts.AsNoTracking().Where(a => a.SessionId == session.Id)
            .OrderBy(a => a.Number).Select(a => new { a.Id, a.Number, a.State, a.HintsUsed, a.UndosUsed, a.StartedAt, a.FinishedAt }).ToArrayAsync(ct);
        return new { session.Id, session.State, session.Version, session.Side, session.CreatedAt, session.UpdatedAt, session.Error, session.RulesVersion,
            objective = PracticeProcessor.Read<PracticeObjective>(session.ObjectiveJson), opponent = "Practical Stockfish · skill 6, not a human rating", attempts,
            attempt = AttemptView(session, attempt, session.State == "active") };
    }
    private static object AttemptView(PracticeSession session, PracticeAttempt attempt, bool canMove)
    {
        var start = PracticeProcessor.Read<EnginePosition>(session.StartingPositionJson); var history = PracticeProcessor.Read<string[]>(attempt.HistoryJson);
        var board = ChessRules.Replay(start.InitialFen, start.History); var initialFen = board.ToFen();
        var notation = new List<string>(); foreach (var move in history) notation.Add(ChessRules.PlayUci(board, move).San ?? move);
        var position = ChessRules.PracticeView(start.InitialFen, start.History, history); var finished = attempt.State != "active";
        return new { attempt.Id, attempt.Number, attempt.State, attempt.HintsUsed, attempt.UndosUsed, attempt.StartedAt, attempt.FinishedAt,
            objective = PracticeProcessor.Read<PracticeObjective>(attempt.ObjectiveJson),
            assisted = attempt.HintsUsed + attempt.UndosUsed > 0, initialFen, position.Fen, position.Endgame, history, notation, attempt.Hint,
            decisionsPlayed = (history.Length + 1) / 2, legalMoves = canMove && !finished ? position.LegalMoves : [],
            review = finished ? new { session.GameId, session.RunId, session.Ply, session.WeaknessSnapshotId, session.RulesVersion,
                source = PracticeProcessor.Read<PracticeSourceEvidence>(session.SourceEvidenceJson),
                initialCheck = attempt.InitialCheckJson is null ? null : PracticeProcessor.Read<PracticePair>(attempt.InitialCheckJson),
                finalCheck = attempt.FinalCheckJson is null ? null : PracticeProcessor.Read<PracticePair>(attempt.FinalCheckJson), attempt.Conclusion,
                grades = PracticeProcessor.Read<PracticeGrade[]>(attempt.GradesJson),
                allGrades = PracticeProcessor.Read<PracticeGrade[]>(attempt.AllGradesJson),
                opponentSearches = PracticeProcessor.Read<PracticeSearch[]>(attempt.OpponentSearchesJson),
                journal = PracticeProcessor.Read<PracticeJournal[]>(attempt.JournalJson) } : null };
    }
}

public sealed record CreatePractice(Guid RunId, int Ply, Guid? WeaknessSnapshotId = null);
public sealed record PracticeCommand(long Version, Guid AttemptId, string? Move = null);
