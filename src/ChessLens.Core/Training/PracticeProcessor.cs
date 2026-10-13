using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChessLens.Core.Training;

public sealed class PracticeProcessor(LensDbContext db, IChessEngine engine, ILogger<PracticeProcessor>? logger = null)
{
    public async Task<bool> RunNext(CancellationToken ct)
    {
        PracticeJob job; var token = Guid.NewGuid().ToString("N");
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // Include exhausted leases in selection so their session receives an honest ungraded outcome.
            var rows = await db.PracticeJobs.FromSqlRaw("""
                SELECT * FROM "PracticeJobs" WHERE "State" = 'queued' OR ("State" = 'running' AND "LeaseUntil" < now())
                ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (rows.Count == 0) { await tx.CommitAsync(ct); return false; }
            job = rows[0]; job.State = "running"; job.LeaseToken = token; job.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(2);
            job.Attempts++; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        try
        {
            if (job.Attempts > 3) throw new UnstablePracticeEvidence("Three interrupted executions exhausted this practice job.");
            var session = await db.PracticeSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == job.SessionId, ct);
            var attempt = await db.PracticeAttempts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == job.AttemptId, ct);
            if (session is null || attempt is null || !Current(session, attempt, job)) { await Supersede(job, token, ct); return true; }
            if (session.RulesVersion != PracticeRules.Version)
            { await Failed(job, token, ct, obsolete: true); return true; }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(90));
            var start = Read<EnginePosition>(session.StartingPositionJson);
            var history = Read<string[]>(attempt.HistoryJson);
            var objective = Read<PracticeObjective>(attempt.ObjectiveJson);
            var rules = new PracticeRules(engine, job.Attempts);
            PracticePair? initial = null; PracticePair? hintEvidence = null; PracticeTurn? turn = null; string? hint = null;
            using var watchStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var watchdog = Watch(job, token, deadline, watchStop.Token);
            try
            {
            if (job.Kind == "initialize")
            {
                initial = await rules.Pair(start, null, deadline.Token);
                if (PracticeRules.Objective(PracticeRules.Score(initial.First)).Kind != PracticeRules.Objective(PracticeRules.Score(initial.Final)).Kind)
                    throw new UnstablePracticeEvidence("The learning objective changed between grading budgets.");
                // Freeze the current objective only after fresh full-strength confirmation.
                objective = PracticeRules.Objective(PracticeRules.Score(initial.Final));
            }
            else if (job.Kind == "hint")
            {
                var position = start with { History = [.. start.History, .. history] };
                var checkedLine = await rules.Pair(position, null, deadline.Token);
                hintEvidence = checkedLine;
                var board = ChessRules.Replay(position.InitialFen, position.History);
                hint = $"Consider {ChessRules.PlayUci(board, checkedLine.Final.Result.BestMove).San ?? checkedLine.Final.Result.BestMove}. Check the opponent's reply before committing.";
            }
            else if (job.Kind == "turn")
                turn = await rules.Turn(start, history, objective, Read<List<PracticeGrade>>(attempt.GradesJson).Count + 1, deadline.Token);
            else throw new ArgumentException("Unknown practice job.");
            }
            finally { await watchStop.CancelAsync(); await watchdog; }

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await PracticeAdmission.LockOwner(db, session.OwnerId, ct);
            db.ChangeTracker.Clear();
            var liveJob = await db.PracticeJobs.SingleOrDefaultAsync(j => j.Id == job.Id && j.State == "running" && j.LeaseToken == token, ct);
            var live = await db.PracticeSessions.SingleOrDefaultAsync(s => s.Id == session.Id, ct);
            var liveAttempt = await db.PracticeAttempts.SingleOrDefaultAsync(a => a.Id == attempt.Id, ct);
            if (liveJob is null || live is null || liveAttempt is null || !Current(live, liveAttempt, job))
            { if (liveJob is not null) liveJob.State = "superseded"; }
            else
            {
                if (initial is not null)
                { liveAttempt.InitialCheckJson = JsonSerializer.Serialize(initial); live.ObjectiveJson = JsonSerializer.Serialize(objective); liveAttempt.ObjectiveJson = live.ObjectiveJson; }
                if (hint is not null)
                {
                    liveAttempt.Hint = hint;
                    var hints = Read<List<PracticePair>>(liveAttempt.HintEvidenceJson); hints.Add(hintEvidence!);
                    liveAttempt.HintEvidenceJson = JsonSerializer.Serialize(hints);
                }
                if (turn is not null)
                {
                    var grades = Read<List<PracticeGrade>>(liveAttempt.GradesJson); grades.Add(turn.Grade);
                    var allGrades = Read<List<PracticeGrade>>(liveAttempt.AllGradesJson); allGrades.Add(turn.Grade);
                    liveAttempt.AllGradesJson = JsonSerializer.Serialize(allGrades);
                    liveAttempt.GradesJson = JsonSerializer.Serialize(grades); liveAttempt.HistoryJson = JsonSerializer.Serialize(turn.History);
                    if (turn.Opponent is not null)
                    {
                        Journal(liveAttempt, "opponent", turn.Opponent.Result.BestMove, turn.History.Length);
                        var opponents = Read<List<PracticeSearch>>(liveAttempt.OpponentSearchesJson); opponents.Add(turn.Opponent);
                        liveAttempt.OpponentSearchesJson = JsonSerializer.Serialize(opponents);
                    }
                    if (turn.Finished)
                    {
                        liveAttempt.State = turn.Outcome!; liveAttempt.FinishedAt = DateTimeOffset.UtcNow; live.State = "completed";
                        liveAttempt.FinalCheckJson = turn.FinalCheck is null ? null : JsonSerializer.Serialize(turn.FinalCheck);
                        liveAttempt.Conclusion = turn.Conclusion;
                    }
                }
                if (live.State != "completed") live.State = "active";
                live.Error = null; live.Version++; live.UpdatedAt = DateTimeOffset.UtcNow;
                liveJob.State = "completed"; liveJob.LeaseUntil = null; liveJob.Error = null;
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger?.LogWarning(error, "Practice job {JobId} session {SessionId} execution {Attempt} interrupted", job.Id, job.SessionId, job.Attempts);
            await Failed(job, token, ct);
        }
        return true;
    }

    private async Task Failed(PracticeJob job, string token, CancellationToken ct, bool obsolete = false)
    {
        db.ChangeTracker.Clear();
        var owner = await db.PracticeSessions.Where(s => s.Id == job.SessionId).Select(s => s.OwnerId).SingleOrDefaultAsync(ct);
        if (owner is null) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct); await PracticeAdmission.LockOwner(db, owner, ct);
        var liveJob = await db.PracticeJobs.SingleOrDefaultAsync(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running", ct);
        var session = await db.PracticeSessions.SingleOrDefaultAsync(s => s.Id == job.SessionId, ct);
        var attempt = await db.PracticeAttempts.SingleOrDefaultAsync(a => a.Id == job.AttemptId, ct);
        if (liveJob is null || session is null || attempt is null) return;
        if (!Current(session, attempt, job)) liveJob.State = "superseded";
        else if (job.Attempts < 3 && !obsolete)
        { liveJob.State = "queued"; liveJob.Error = "Grading was incomplete or unstable. Retrying within the three-execution limit."; session.Error = liveJob.Error; session.UpdatedAt = DateTimeOffset.UtcNow; }
        else
        {
            liveJob.State = "failed"; liveJob.Error = obsolete ? "This session uses an earlier grading rule. Open its original mistake to start practice with the current rules; this attempt is ungraded." : "Practice could not obtain stable engine evidence. This attempt is ungraded; retry to start again.";
            session.State = "failed"; session.Error = liveJob.Error; session.Version++; session.UpdatedAt = DateTimeOffset.UtcNow;
            attempt.State = "ungraded"; attempt.FinishedAt = DateTimeOffset.UtcNow;
        }
        liveJob.LeaseUntil = null; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task Watch(PracticeJob job, string token, CancellationTokenSource deadline, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
                if (!await db.PracticeJobs.AsNoTracking().AnyAsync(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running" &&
                    db.PracticeSessions.Any(s => s.Id == j.SessionId && s.Version == j.SessionVersion && s.CurrentAttemptId == j.AttemptId), ct))
                { await deadline.CancelAsync(); return; }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error)
        { logger?.LogWarning(error, "Practice job {JobId} lost its durable state check", job.Id); await deadline.CancelAsync(); }
    }
    private Task Supersede(PracticeJob job, string token, CancellationToken ct) => db.PracticeJobs
        .Where(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running")
        .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "superseded").SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null), ct);
    private static bool Current(PracticeSession session, PracticeAttempt attempt, PracticeJob job) =>
        session.Version == job.SessionVersion && session.CurrentAttemptId == attempt.Id && attempt.State == "active" &&
        session.State is "preparing" or "waiting";
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Saved practice data is missing.");
    public static void Journal(PracticeAttempt attempt, string action, string? move, int length)
    {
        var journal = Read<List<PracticeJournal>>(attempt.JournalJson); journal.Add(new(action, move, length, DateTimeOffset.UtcNow));
        attempt.JournalJson = JsonSerializer.Serialize(journal);
    }
}

public static class PracticeAdmission
{
    public static Task LockOwner(LensDbContext db, string owner, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner}, 0))", ct);
    public static async Task<bool> Reserve(LensDbContext db, string owner, bool newSession, CancellationToken ct, bool activate = false)
    {
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('chesslens-practice-admission', 0))", ct);
        var day = DateTimeOffset.UtcNow.AddDays(-1);
        if ((newSession || activate) && await db.PracticeSessions.CountAsync(s => s.OwnerId == owner && (s.State == "active" || s.State == "preparing" || s.State == "waiting"), ct) >= 2) return false;
        if (newSession && await db.ResourceUsages.Where(u => u.OwnerId == owner && u.Kind == "practice-session" && u.CreatedAt > day).SumAsync(u => u.Units, ct) >= 40) return false;
        if (await db.ResourceUsages.Where(u => u.OwnerId == owner && u.Kind == "practice-job" && u.CreatedAt > day).SumAsync(u => u.Units, ct) >= 200 ||
            await db.PracticeJobs.CountAsync(j => j.State == "queued" || j.State == "running", ct) >= 1000) return false;
        if (newSession) db.ResourceUsages.Add(new() { OwnerId = owner, Kind = "practice-session" });
        db.ResourceUsages.Add(new() { OwnerId = owner, Kind = "practice-job" }); return true;
    }
}
