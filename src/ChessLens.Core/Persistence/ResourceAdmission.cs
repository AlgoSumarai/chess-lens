using ChessLens.Core.Training;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Core.Persistence;

public enum WorkBudget { Import, Analysis, Position, PositionChannel, Insight, Puzzle, PuzzleAttempt, Coaching, PracticeSession, PracticeJob, Report }

public sealed class ResourceBudgetReached(string message) : Exception(message);

/// <summary>Atomic retained daily accounting and a shared pending-work ceiling across all producers.</summary>
public static class ResourceAdmission
{
    public const int MaximumPendingJobs = 1000;
    public static async Task Reserve(LensDbContext db, string owner, WorkBudget budget, CancellationToken ct, int units = 1, int pendingJobs = 1)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Resource admission requires a transaction.");
        if (units < 0 || pendingJobs is < 0 or > MaximumPendingJobs) throw new ArgumentException("Invalid work reservation.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner}, 0))", ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('chesslens-resource-admission', 0))", ct);
        var (kind, limit, description) = Policy(budget);
        var used = await db.ResourceUsages.Where(u => u.OwnerId == owner && u.Kind == kind && u.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1)).SumAsync(u => (long)u.Units, ct);
        used += db.ChangeTracker.Entries<ResourceUsage>().Where(e => e.State == EntityState.Added && e.Entity.OwnerId == owner && e.Entity.Kind == kind).Sum(e => (long)e.Entity.Units);
        if (used + units > limit) throw new ResourceBudgetReached($"The rolling-day {description} budget is {limit:N0}. Deleting imported data does not reset this budget.");
        if (pendingJobs > 0 && await Pending(db, ct) + pendingJobs > MaximumPendingJobs)
            throw new ResourceBudgetReached("The service is processing its current work. Try again after jobs complete.");
        if (units > 0) db.ResourceUsages.Add(new() { OwnerId = owner, Kind = kind, Units = units });
    }

    public static Task<int> Pending(LensDbContext db, CancellationToken ct) => db.Database.SqlQuery<int>($"""
        SELECT (
          (SELECT count(*) FROM "ImportJobs" WHERE "State" IN ('queued','running')) +
          (SELECT count(*) FROM "AnalysisRuns" WHERE "State" IN ('waiting','queued','running')) +
          (SELECT count(*) FROM "ImportAnalysisBatches" WHERE "State" IN ('queued','running')) +
          (SELECT count(*) FROM "PositionAnalysisJobs" WHERE "State" IN ('queued','running')) +
          (SELECT count(*) FROM "InsightSnapshots" WHERE "State" IN ('queued','running')) +
          (SELECT count(*) FROM "Puzzles" WHERE "State" IN ('queued','running')) +
          (SELECT count(*) FROM "CoachingJobs" WHERE "State" IN ('queued','running')) +
          (SELECT count(*) FROM "PracticeJobs" WHERE "State" IN ('queued','running')) +
          (SELECT count(*) FROM "ReportSnapshots" WHERE "State" IN ('queued','running'))
        )::integer AS "Value"
        """).SingleAsync(ct);

    private static (string Kind, int Limit, string Description) Policy(WorkBudget budget) => budget switch
    {
        WorkBudget.Import => ("import", 20, "import batch"),
        WorkBudget.Analysis => ("analysis-plies", 5000, "game-review half-move"),
        WorkBudget.Position => ("position-review", 200, "position review"),
        WorkBudget.PositionChannel => ("position-channel", 200, "new review channel"),
        WorkBudget.Insight => ("insight", 20, "weakness snapshot"),
        WorkBudget.Puzzle => ("puzzle-validation", 20, "puzzle validation"),
        WorkBudget.PuzzleAttempt => ("puzzle-attempt", 200, "puzzle attempt"),
        WorkBudget.Coaching => ("coaching", 300, "saved explanation"),
        WorkBudget.PracticeSession => ("practice-session", 40, "new mistake session"),
        WorkBudget.PracticeJob => ("practice-job", 200, "mistake-practice job"),
        WorkBudget.Report => ("report", 20, "report snapshot"),
        _ => throw new ArgumentOutOfRangeException(nameof(budget))
    };
}
