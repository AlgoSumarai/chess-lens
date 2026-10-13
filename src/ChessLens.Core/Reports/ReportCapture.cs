using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Insights;
using ChessLens.Core.Persistence;
using ChessLens.Core.Training;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Core.Reports;

/// <summary>Read one consistent dataset, then admit it under fresh owner/global locks. No engine/model work.</summary>
public sealed class ReportCapture(LensDbContext db)
{
    public async Task<ReportInputs> Capture(string owner, ReportPeriod period, ProgressFilter? filter, InsightOptions options, CancellationToken ct, int maximumGames = 50)
    {
        filter?.Validate(); options.Validate();
        var frozenOptions = JsonSerializer.Deserialize<InsightOptions>(JsonSerializer.Serialize(options))!;
        maximumGames = filter?.MaximumGames ?? maximumGames;
        if (maximumGames is < 1 or > 200) throw new ArgumentException("Choose 1–200 games per period.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var version = await db.Users.AsNoTracking().Where(u => u.Id == owner).Select(u => u.DataVersion).SingleAsync(ct);
        var cutoff = DateTimeOffset.UtcNow; var previousPeriod = ReportCalendar.Previous(period);
        var current = await Select(owner, period, filter, maximumGames, cutoff, ct, currentPeriod: true);
        var previous = await Select(owner, previousPeriod, filter, maximumGames, cutoff, ct);
        (current, previous) = ReportEvidenceBudget.Apply(current, previous);
        await tx.CommitAsync(ct);
        return new(period, previousPeriod, filter, cutoff, version, InsightRules.Version, frozenOptions, current, previous, maximumGames);
    }

    public async Task<ReportSnapshot?> Enqueue(string owner, ReportInputs inputs, CancellationToken ct)
    {
        if (inputs.Period.Kind == "weekly" && inputs.Current.MatchingGames == 0) return null;
        var scope = Hash(new { inputs.Period, inputs.Filter });
        // Capture time is provenance, not a new revision when all actual evidence stays identical.
        var identity = Hash(new { inputs.Period, inputs.PreviousPeriod, inputs.Filter, inputs.DataVersion, inputs.DetectorVersion,
            inputs.DetectorOptions, inputs.Current, inputs.Previous, inputs.MaximumGames, rules = ReportRules.Version });
        var json = JsonSerializer.Serialize(inputs);
        if (Encoding.UTF8.GetByteCount(json) > 2_000_000) throw new ArgumentException("Report inputs exceeded the bounded snapshot size.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await PracticeAdmission.LockOwner(db, owner, ct);
        if (await db.Users.Where(u => u.Id == owner).Select(u => u.DataVersion).SingleAsync(ct) != inputs.DataVersion)
            throw new ReportDataChanged();
        var prior = await db.ReportSnapshots.SingleOrDefaultAsync(r => r.OwnerId == owner && r.CacheIdentity == identity, ct);
        if (prior is not null)
        {
            if (prior.State is "failed" or "cancelled")
            {
                if (!await Reserve(owner, ct)) throw new ReportBudgetReached();
                prior.State = "queued"; prior.Attempts = 0; prior.Error = null; prior.LeaseUntil = null; prior.Version++; prior.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
            return prior;
        }
        if (!await Reserve(owner, ct)) throw new ReportBudgetReached();
        var revision = (await db.ReportSnapshots.Where(r => r.OwnerId == owner && r.ScopeIdentity == scope).MaxAsync(r => (int?)r.Revision, ct) ?? 0) + 1;
        var job = new ReportSnapshot { OwnerId = owner, Kind = inputs.Period.Kind, ScopeIdentity = scope, CacheIdentity = identity, InputsJson = json, Revision = revision };
        db.ReportSnapshots.Add(job); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return job;
    }

    private async Task<bool> Reserve(string owner, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('chesslens-report-admission', 0))", ct);
        if (await db.ReportSnapshots.AnyAsync(r => r.OwnerId == owner && (r.State == "queued" || r.State == "running"), ct) ||
            await db.ResourceUsages.Where(u => u.OwnerId == owner && u.Kind == "report" && u.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1)).SumAsync(u => u.Units, ct) >= 20 ||
            await db.ReportSnapshots.CountAsync(r => r.State == "queued" || r.State == "running", ct) >= 1000) return false;
        db.ResourceUsages.Add(new() { OwnerId = owner, Kind = "report" }); return true;
    }

    private async Task<ReportSelection> Select(string owner, ReportPeriod period, ProgressFilter? filter, int maximumGames, DateTimeOffset cutoff, CancellationToken ct, bool currentPeriod = false)
    {
        var completed = db.AnalysisRuns.AsNoTracking().Where(r => r.OwnerId == owner && r.State == "completed" && r.UpdatedAt <= cutoff);
        if (period.Kind == "weekly") completed = completed.Where(r => r.UpdatedAt >= period.From && r.UpdatedAt < period.To);
        var matching = Filter(db.UserGames.AsNoTracking().Where(g => g.OwnerId == owner), filter);
        if (period.Kind == "weekly") matching = matching.Where(g => completed.Any(r => r.GameId == g.GameId));
        var undated = await matching.CountAsync(g => g.Game.PlayedAt == null, ct);
        if (period.Kind == "progress") matching = matching.Where(g => g.Game.PlayedAt >= period.From && g.Game.PlayedAt < period.To);
        var count = await matching.CountAsync(ct);
        var ordered = period.Kind == "weekly"
            ? matching.OrderByDescending(g => completed.Where(r => r.GameId == g.GameId).OrderByDescending(r => r.UpdatedAt).Select(r => (DateTimeOffset?)r.UpdatedAt).FirstOrDefault())
            : matching.OrderByDescending(g => g.Game.PlayedAt);
        var projection = ordered.ThenBy(g => g.GameId).Take(maximumGames).Select(g => new ReportGameReference(g.GameId, g.PlayerColour, g.Game.Provider, g.Game.RatingPool, g.Game.Rated,
            g.Game.Opening, g.Game.TimeControl, TimeControlGroups.SqlGroup(g.Game.TimeControl, g.Game.RatingPool), g.Game.Result, g.Game.PlayedAt,
            g.PlayerColour == "white" ? g.Game.WhiteRating : g.PlayerColour == "black" ? g.Game.BlackRating : null, g.Game.Moves.Count,
            completed.Where(r => r.GameId == g.GameId).OrderByDescending(r => r.UpdatedAt).ThenByDescending(r => r.Id)
                .Select(r => new ReportAnalysisReference(r.Id, r.EngineVersion ?? "Engine version unavailable", r.Profile, r.AnalysisVersion,
                    r.SettingsJson ?? "{}", r.ClassificationSettingsJson ?? JsonSerializer.Serialize(new AnalysisOptions(), (JsonSerializerOptions?)null), r.UpdatedAt,
                    r.EngineVersion != null && r.SettingsJson != null && r.ClassificationSettingsJson != null)).FirstOrDefault()));
        var selected = await projection.ToArrayAsync(ct);
        // Training dates describe when an attempt finished, independently of the game's played-at date.
        var trainingGames = Filter(db.UserGames.AsNoTracking().Where(g => g.OwnerId == owner), filter, applyColour: false).Select(g => g.GameId);
        var puzzles = from attempt in db.PuzzleAttempts.AsNoTracking()
                      join puzzle in db.Puzzles.AsNoTracking() on attempt.PuzzleId equals puzzle.Id
                      where attempt.OwnerId == owner && puzzle.OwnerId == owner && trainingGames.Contains(puzzle.GameId) &&
                            attempt.FinishedAt >= period.From && attempt.FinishedAt < period.To && attempt.FinishedAt <= cutoff &&
                            (filter == null || filter.Colour == null || db.MoveAnalyses.Any(m => m.RunId == puzzle.RunId && m.Ply == puzzle.Ply && m.Side == filter.Colour))
                      orderby attempt.FinishedAt, attempt.Id
                      select new TrainingObservation("puzzle", attempt.Id, puzzle.GameId, attempt.State, attempt.HintsUsed > 0,
                          puzzle.ValidationVersion, puzzle.Objective ?? "unknown", attempt.FinishedAt!.Value);
        var practice = from attempt in db.PracticeAttempts.AsNoTracking()
                       join session in db.PracticeSessions.AsNoTracking() on attempt.SessionId equals session.Id
                       where session.OwnerId == owner && trainingGames.Contains(session.GameId) && attempt.FinishedAt >= period.From &&
                             attempt.FinishedAt < period.To && attempt.FinishedAt <= cutoff && (filter == null || filter.Colour == null || session.Side == filter.Colour)
                       select new { attempt.Id, session.GameId, attempt.State, Assisted = attempt.HintsUsed + attempt.UndosUsed > 0,
                           session.RulesVersion, attempt.ObjectiveJson, FinishedAt = attempt.FinishedAt!.Value };
        var trainingCount = await puzzles.CountAsync(ct) + await practice.CountAsync(ct);
        var puzzleRows = await puzzles.Take(2000).ToArrayAsync(ct);
        var practiceRows = await practice.OrderBy(p => p.FinishedAt).ThenBy(p => p.Id).Take(2000 - puzzleRows.Length).ToArrayAsync(ct);
        var training = puzzleRows.Concat(practiceRows.Select(p => new TrainingObservation("practice", p.Id, p.GameId, p.State, p.Assisted,
            p.RulesVersion, PracticeProcessor.Read<PracticeObjective>(p.ObjectiveJson).Kind, p.FinishedAt))).OrderBy(p => p.FinishedAt).ThenBy(p => p.Id).ToArray();
        // Weekly history does not invent past due dates from today's mutable spaced-review schedule.
        var due = period.Kind == "progress" && currentPeriod
            ? await db.Puzzles.AsNoTracking().Where(p => p.OwnerId == owner && p.State == "ready" && trainingGames.Contains(p.GameId) && p.ReviewDueAt <= cutoff)
                .OrderBy(p => p.ReviewDueAt).ThenBy(p => p.Id).Select(p => p.Id).Take(20).ToArrayAsync(ct)
            : [];
        return new(count, period.Kind == "progress" ? undated : 0, selected, trainingCount, trainingCount > training.Length, training, due);
    }

    private static IQueryable<UserGame> Filter(IQueryable<UserGame> source, ProgressFilter? filter, bool applyColour = true)
    {
        if (filter is null) return source;
        if (filter.Source is not null) source = source.Where(g => g.Game.Provider == filter.Source);
        if (applyColour && filter.Colour is not null) source = source.Where(g => g.PlayerColour == filter.Colour);
        if (filter.Opening is not null) source = source.Where(g => g.Game.Opening == filter.Opening);
        if (filter.UnknownOpening) source = source.Where(g => g.Game.Opening == null);
        if (filter.TimeControl is not null) source = source.Where(g => g.Game.TimeControl == filter.TimeControl);
        if (filter.TimeControlGroup is not null) source = source.Where(g => TimeControlGroups.SqlGroup(g.Game.TimeControl, g.Game.RatingPool) == filter.TimeControlGroup);
        return source;
    }
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}

public sealed class ReportBudgetReached : Exception;
public sealed class ReportDataChanged : Exception;
