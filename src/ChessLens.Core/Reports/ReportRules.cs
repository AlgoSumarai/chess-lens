using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Insights;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Core.Reports;

public static class ReportRules
{
    public const string Version = "chesslens-reports-v2";
    private static readonly string[] Categories = ["hanging-material", "missed-tactics", "opening-mistakes", "time-trouble", "conversion-problems", "endgame-errors"];

    public static async Task<ReportResult> Generate(LensDbContext db, string owner, ReportInputs inputs, CancellationToken ct)
    {
        inputs.DetectorOptions.Validate();
        var references = inputs.Current.Games.Concat(inputs.Previous.Games).ToArray();
        var ids = references.Select(g => g.GameId).Distinct().ToArray();
        var runIds = references.Where(g => g.Analysis is not null).Select(g => g.Analysis!.RunId).Distinct().ToArray();
        // Source PGN/comments are unnecessary and may be megabytes. Load only the legal move/evidence inputs.
        var games = await db.Games.AsNoTracking().Where(g => ids.Contains(g.Id)).Select(g => new SourceGame
        {
            Id = g.Id, Fingerprint = g.Fingerprint, White = "", Black = "", RawPgn = "", HeadersJson = "{}", Result = g.Result,
            InitialFen = g.InitialFen, Provider = g.Provider, Moves = g.Moves.ToList()
        }).ToDictionaryAsync(g => g.Id, ct);
        var runs = await db.AnalysisRuns.AsNoTracking().Where(r => r.OwnerId == owner && runIds.Contains(r.Id) && r.State == "completed")
            .Include(r => r.Moves).ToDictionaryAsync(r => r.Id, ct);
        if (games.Count != ids.Length || runs.Count != runIds.Length) throw new ArgumentException("The saved report evidence is no longer available.");
        foreach (var reference in references.Where(g => g.Analysis is not null))
        {
            var expected = reference.Analysis!; var run = runs[expected.RunId];
            if (run.GameId != reference.GameId || run.Profile != expected.Profile || run.AnalysisVersion != expected.AnalysisVersion ||
                (run.EngineVersion ?? "Engine version unavailable") != expected.Engine || (run.SettingsJson ?? "{}") != expected.SettingsJson ||
                (run.ClassificationSettingsJson ?? JsonSerializer.Serialize(new AnalysisOptions())) != expected.ClassificationSettingsJson || run.UpdatedAt != expected.CompletedAt)
                throw new ArgumentException("The saved analysis metadata changed; this report cannot reinterpret its original evidence.");
        }
        return Build(inputs, games, runs, ct);
    }

    public static ReportResult Build(ReportInputs inputs, IReadOnlyDictionary<Guid, SourceGame> games, IReadOnlyDictionary<Guid, AnalysisRun> runs, CancellationToken ct = default)
    {
        var current = Period(inputs.Current, inputs, games, runs, ct);
        var previous = Period(inputs.Previous, inputs, games, runs, ct).ToDictionary(g => g.Identity);
        var groups = current.Select(group =>
        {
            previous.TryGetValue(group.Identity, out var prior);
            var comparison = Compare(group, prior);
            var rates = group.WeaknessRates.Select(rate =>
            {
                var old = prior?.WeaknessRates.Single(r => r.Category == rate.Category);
                var supported = comparison.Status == "supported" && old is not null && rate.EligibleGames >= 5 && old.EligibleGames >= 5 &&
                    rate.EligibleDecisions >= 10 && old.EligibleDecisions >= 10 && (rate.Category != "time-trouble" ||
                    group.Summary.ClockAvailable && prior!.Summary.ClockAvailable && Math.Abs(group.Summary.ClockCoverage - prior.Summary.ClockCoverage) <= .1);
                return rate with { Comparison = supported
                    ? Difference(rate.Occurrences, rate.EligibleDecisions, old!.Occurrences, old.EligibleDecisions, "Comparable category-specific decision samples; an observed association, not a causal training effect.")
                    : new("insufficient", comparison.Status != "supported" ? comparison.Explanation : "This category needs five eligible games, ten eligible decisions in both periods, and comparable timing coverage when relevant.") };
            }).ToArray();
            return group with { Comparison = comparison, WeaknessRates = rates };
        }).OrderByDescending(g => g.Coverage.ReviewedGames).ThenBy(g => g.Identity).ToArray();
        var training = inputs.Current.Training.GroupBy(t => new { t.Kind, t.Version, t.Objective }).Select(group => new TrainingSummary(
            group.Key.Kind, group.Key.Version, group.Key.Objective, group.Count(), group.Count(t => Success(t.Outcome) && !t.Assisted),
            group.Count(t => Success(t.Outcome) && t.Assisted), group.Count(t => t.Outcome is "failed" or "needs-practice"),
            group.Count(t => t.Outcome is "cancelled" or "retried" or "revealed" or "expired"), group.Count(t => t.Outcome == "ungraded"))).ToArray();
        var findings = groups.SelectMany(g => g.Summary.Findings).OrderByDescending(f => f.Status == "recurring-pattern").ThenByDescending(f => f.AffectedGames).ThenByDescending(f => f.Occurrences).Take(3).ToArray();
        var plan = findings.Length == 0
            ? new[] { "Review a verified decision in an analysed game before choosing a practice focus. Current evidence does not support a recurring weakness." }
            : findings.Select(f => (f.Status == "recurring-pattern" ? "Supported practice focus: " : "Early signal to revisit: ") + f.Practice).ToArray();
        if (inputs.Current.DuePuzzles.Length > 0) plan = [.. plan, $"Revisit one of {inputs.Current.DuePuzzles.Length} due personal puzzles, recording whether help was used."];
        return new(Version, inputs.Period, inputs.PreviousPeriod, inputs.CapturedAt, inputs.Current.MatchingGames,
            OverallCoverage(inputs.Current, groups), Results(inputs.Current.Games), groups, training, inputs.Current.TrainingCount,
            inputs.Current.TrainingCapped, plan, inputs.Current.DuePuzzles, inputs.Current.UndatedGamesExcluded);
    }

    private static ReportGroup[] Period(ReportSelection selection, ReportInputs inputs, IReadOnlyDictionary<Guid, SourceGame> games,
        IReadOnlyDictionary<Guid, AnalysisRun> runs, CancellationToken ct)
    {
        return selection.Games.Where(g => g.Analysis is not null && g.Colour is "white" or "black").GroupBy(g => Identity(g, inputs)).Select(group =>
        {
            ct.ThrowIfCancellationRequested(); var rows = group.ToArray(); var first = rows[0];
            var evidence = rows.Select(reference =>
            {
                var stored = games[reference.GameId];
                var source = new SourceGame { Id = stored.Id, Fingerprint = stored.Fingerprint, White = "", Black = "", RawPgn = "", HeadersJson = "{}",
                    InitialFen = stored.InitialFen, Moves = PgnAnnotations.Moves(stored, reference.ClockSecondsJson), Result = reference.Result, Provider = reference.Source, PlayedAt = reference.PlayedAt,
                    Opening = reference.Opening, TimeControl = reference.TimeControl };
                return new InsightGame(source, reference.Colour!, runs[reference.Analysis!.RunId]);
            }).ToArray();
            var summary = InsightRules.Build(evidence, inputs.DetectorOptions, ct, inputs.DetectorVersion);
            var playerDecisions = evidence.Sum(g => g.Game.Moves.Count(m => m.Side == g.Colour));
            var stable = 0; var rejected = 0;
            foreach (var entry in evidence)
                foreach (var move in entry.Run.Moves.Where(m => m.Side == entry.Colour && m.Complete && !m.Provisional))
                { ct.ThrowIfCancellationRequested(); if (VerifiedEvidence.Read(entry.Game, entry.Run, move) is not null) stable++; else rejected++; }
            var coverage = new ReportCoverage(rows.Length, rows.Length, 0, 0, rows.Count(r => r.PlayedAt is null), playerDecisions,
                summary.EligibleMoves, stable, summary.ProvisionalMoves, rejected, selection.MatchingGames > selection.Games.Length,
                selection.Games.Count(r => ContextMatches(first, r)), selection.Games.Count(r => ContextMatches(first, r) && r.Analysis is not null && r.Colour is "white" or "black"));
            var rates = Categories.Select(category => Rate(category, evidence, summary, inputs.DetectorOptions)).ToArray();
            // Reports show representative positions; full source run/ply evidence remains immutable in the referenced reviews.
            summary = summary with { Findings = summary.Findings.Select(f => f with { Evidence = f.Evidence.Take(3).ToArray() }).ToArray(), Evidence = summary.Evidence.Take(10).ToArray() };
            return new ReportGroup(group.Key, first.Source, first.RatingPool ?? "unknown", first.Colour!, first.Opening ?? "Opening unknown",
                first.TimeControlGroup, first.Rated, rows.Select(r => r.TimeControl ?? "unknown").Distinct().Order().ToArray(), first.Analysis!, rows.Select(r => r.GameId).ToArray(),
                Results(rows), coverage, summary, new("insufficient", "No preceding compatible sample was found."), rates,
                rows.Where(r => r.Rating is not null && r.PlayedAt is not null).OrderBy(r => r.PlayedAt).ThenBy(r => r.GameId)
                    .Select(r => new RatingObservation(r.GameId, r.PlayedAt!.Value, r.Rating!.Value, r.TimeControl, r.Rated)).ToArray());
        }).ToArray();
    }

    private static WeaknessRate Rate(string category, InsightGame[] games, InsightSummary summary, InsightOptions options)
    {
        var numerator = summary.Findings.Where(f => f.Category == category).Sum(f => f.Occurrences);
        var objective = summary.CategoryCoverage?.SingleOrDefault(c => c.Category == category);
        if (objective is not null) return new(category, numerator, objective.EligibleDecisions, objective.EligibleGames, new("insufficient", "Awaiting a compatible preceding category sample."));
        var eligible = games.SelectMany(g => g.Run.Moves.Where(m => m.Side == g.Colour && m.Complete).Select(m => new { Game = g, Analysis = m, Source = g.Game.Moves.SingleOrDefault(s => s.Ply == m.Ply) }))
            .Where(e => e.Source is not null && e.Source.Side == e.Game.Colour && e.Source.FenBefore == e.Analysis.FenBefore && e.Source.Uci == e.Analysis.PlayedMove)
            .Where(e => category != "opening-mistakes" || e.Game.Game.InitialFen == ChessRules.InitialFen && e.Analysis.Ply <= options.OpeningFullMoves * 2)
            .Where(e => category != "time-trouble" || summary.ClockAvailable && InsightRules.Clock(e.Game.Game.TimeControl, e.Game.Game.Moves.OrderBy(m => m.Ply).ToArray(), e.Source!) is not null).ToArray();
        return new(category, numerator, eligible.Length, eligible.Select(e => e.Game.Game.Id).Distinct().Count(), new("insufficient", "Awaiting a compatible preceding category sample."));
    }

    public static PeriodComparison Compare(ReportGroup current, ReportGroup? previous)
    {
        if (previous is null || previous.Identity != current.Identity) return new("insufficient", "No preceding sample has the same provider, pool, colour, opening, time-control group and analysis/detector settings.");
        if (!current.Analysis.SettingsKnown || !previous.Analysis.SettingsKnown) return new("insufficient", "The original analysis settings are incomplete; comparison is unavailable.");
        if (current.GameIds.Intersect(previous.GameIds).Any()) return new("insufficient", "The periods review some of the same source games. Use played-at progress to compare independent game samples.");
        if (current.Coverage.ReviewedGames < 5 || previous.Coverage.ReviewedGames < 5) return new("insufficient", "At least five reviewed games are needed in both comparable periods.");
        if (current.Coverage.SelectionCapped || previous.Coverage.SelectionCapped) return new("insufficient", "A period's evidence selection was capped. Compare a smaller selection before interpreting a change.");
        var nowReviewed = Fraction(current.Coverage.ContextReviewedGames, current.Coverage.ContextGames);
        var oldReviewed = Fraction(previous.Coverage.ContextReviewedGames, previous.Coverage.ContextGames);
        if (nowReviewed < .8 || oldReviewed < .8 || Math.Abs(nowReviewed - oldReviewed) > .10000001)
            return new("insufficient", "Selected-player identity and completed reviews must cover at least 80% of same-context games in both periods, with coverage differing by at most ten percentage points.");
        var nowCoverage = Fraction(current.Coverage.StableDecisions, current.Coverage.PlayerDecisions);
        var oldCoverage = Fraction(previous.Coverage.StableDecisions, previous.Coverage.PlayerDecisions);
        if (nowCoverage < .8 || oldCoverage < .8 || Math.Abs(nowCoverage - oldCoverage) > .10000001)
            return new("insufficient", "Stable evidence must cover at least 80% of decisions in both periods, with coverage differing by at most ten percentage points.");
        return Difference(current.Summary.VerifiedErrorMoves, current.Summary.EligibleMoves, previous.Summary.VerifiedErrorMoves, previous.Summary.EligibleMoves,
            "Descriptive difference between compatible decision samples; training completion alone does not establish improved game performance.");
    }
    private static PeriodComparison Difference(int now, int nowEligible, int old, int oldEligible, string explanation) =>
        new("supported", explanation, now, nowEligible, old, oldEligible, Fraction(now, nowEligible) - Fraction(old, oldEligible));
    private static double Fraction(int numerator, int denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
    private static bool Success(string outcome) => outcome is "solved" or "succeeded";
    private static bool ContextMatches(ReportGameReference first, ReportGameReference candidate) => first.Source == candidate.Source &&
        first.RatingPool == candidate.RatingPool && (first.Colour == candidate.Colour || candidate.Colour is null) && first.Opening == candidate.Opening &&
        first.TimeControlGroup == candidate.TimeControlGroup && first.Rated == candidate.Rated;
    private static string Identity(ReportGameReference game, ReportInputs inputs) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        game.Source, game.RatingPool, game.Colour, game.Opening, game.TimeControlGroup, game.Rated, game.Analysis!.Engine, game.Analysis.Profile,
        game.Analysis.AnalysisVersion, game.Analysis.SettingsJson, game.Analysis.ClassificationSettingsJson, game.Analysis.SettingsKnown,
        inputs.DetectorVersion, inputs.DetectorOptions, rules = Version
    }))));
    private static ReportCoverage OverallCoverage(ReportSelection selection, ReportGroup[] groups) => new(selection.Games.Length,
        selection.Games.Count(g => g.Analysis is not null), selection.Games.Count(g => g.Colour is not ("white" or "black")), selection.Games.Count(g => g.Analysis is null),
        selection.Games.Count(g => g.PlayedAt is null), groups.Sum(g => g.Coverage.PlayerDecisions), groups.Sum(g => g.Coverage.CompleteDecisions), groups.Sum(g => g.Coverage.StableDecisions),
        groups.Sum(g => g.Coverage.ProvisionalDecisions), groups.Sum(g => g.Coverage.RejectedDecisions), selection.MatchingGames > selection.Games.Length,
        selection.Games.Length, selection.Games.Count(g => g.Analysis is not null && g.Colour is "white" or "black"));
    private static ResultCounts Results(IEnumerable<ReportGameReference> games)
    {
        var wins = 0; var draws = 0; var losses = 0; var unknown = 0;
        foreach (var game in games)
        {
            if (game.Colour is not ("white" or "black")) { unknown++; continue; }
            if (game.Result == "1/2-1/2") draws++;
            else if (game.Result == (game.Colour == "white" ? "1-0" : "0-1")) wins++;
            else if (game.Result is "1-0" or "0-1") losses++;
            else unknown++;
        }
        return new(wins, draws, losses, unknown);
    }
}
