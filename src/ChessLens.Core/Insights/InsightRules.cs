using System.Globalization;
using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;

namespace ChessLens.Core.Insights;

public sealed class InsightOptions
{
    public int MinimumGames { get; set; } = 5;
    public int RecurringGames { get; set; } = 3;
    public int OpeningFullMoves { get; set; } = 10;
    public int MinimumLoss { get; set; } = 100;
    public int MinimumMaterial { get; set; } = 100;
    public int MinimumClockMoves { get; set; } = 20;
    public double MinimumClockCoverage { get; set; } = .5;
    public void Validate()
    {
        if (MinimumGames is < 2 or > 100 || RecurringGames is < 2 or > 100 || OpeningFullMoves is < 1 or > 30 ||
            MinimumLoss is < 50 or > 1000 || MinimumMaterial is < 100 or > 900 || MinimumClockMoves is < 2 or > 1000 ||
            MinimumClockCoverage is <= 0 or > 1) throw new ArgumentException("Insight thresholds are outside the supported bounds.");
    }
}

public sealed record InsightGame(SourceGame Game, string Colour, AnalysisRun Run);
public sealed record MistakeEvidence(Guid GameId, Guid RunId, int Ply, string Side, string Fen, string PlayedMove, string BestMove,
    string Classification, int? CentipawnLoss, string? MateTransition, string[] Tags, string[] BestLine, string[] PlayedLine,
    int? BestMaterialChange, int? PlayedMaterialChange, double? RemainingSeconds, double? SpentSeconds, string Opening,
    string Source, string? TimeControl, DateTimeOffset? PlayedAt, string Result, string Explanation);
public sealed record WeaknessFinding(string Category, string Group, string Claim, string Status, string Confidence,
    int EligibleGames, int EligibleMoves, int Occurrences, int AffectedGames, double OccurrenceRate, int LossesAmongAffectedGames,
    int CentipawnLossSum, int MateTransitions, string Severity, string Practice, MistakeEvidence[] Evidence);
public sealed record InsightSummary(string DetectorVersion, int AnalysedGames, int EligibleMoves, int VerifiedErrorMoves,
    int ProvisionalMoves, int RejectedEvidenceMoves, int ClockMoves, double ClockCoverage, bool ClockAvailable, WeaknessFinding[] Findings, MistakeEvidence[] Evidence);

/// <summary>Conservative, deterministic associations derived from immutable engine evidence.</summary>
public static class InsightRules
{
    public const string Version = "chesslens-insights-v1";
    public static InsightSummary Build(IReadOnlyList<InsightGame> games, InsightOptions options, CancellationToken ct = default)
    {
        options.Validate();
        var evidence = new List<MistakeEvidence>();
        var eligible = new List<(InsightGame Game, GameMove Move, bool Clock)>();
        var provisional = 0;
        var rejected = 0;
        foreach (var entry in games.GroupBy(g => g.Game.Id).Select(group => group.OrderByDescending(g => g.Run.CreatedAt).ThenBy(g => g.Run.Id).First()))
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Colour is not ("white" or "black") || entry.Run.State != "completed" || entry.Run.GameId != entry.Game.Id) continue;
            var moves = entry.Game.Moves.OrderBy(m => m.Ply).ToArray();
            foreach (var analysis in entry.Run.Moves.Where(m => m.Side == entry.Colour && m.Complete))
            {
                ct.ThrowIfCancellationRequested();
                var move = moves.SingleOrDefault(m => m.Ply == analysis.Ply);
                if (move is null || move.Side != entry.Colour || move.FenBefore != analysis.FenBefore || move.Uci != analysis.PlayedMove) continue;
                var clock = Clock(entry.Game.TimeControl, moves, move);
                eligible.Add((entry, move, clock is not null));
                if (analysis.Provisional) { provisional++; continue; }
                if (analysis.CentipawnLoss < options.MinimumLoss || analysis.CentipawnLoss is null &&
                    analysis.MateTransition is not ("missed-forced-mate" or "allowed-forced-mate")) continue;
                if (analysis.Classification is not ("inaccuracy" or "mistake" or "blunder")) continue;
                var position = new EnginePosition(entry.Game.InitialFen, moves.TakeWhile(m => m.Ply < move.Ply).Select(m => m.Uci).ToArray());
                var best = ReadLine(analysis.BestSearchJson, position, analysis.BestMove);
                var played = ReadLine(analysis.PlayedSearchJson, position, move.Uci);
                if (best is null || played is null || best.Score != new EngineScore(analysis.BestScoreKind, analysis.BestScoreValue) ||
                    played.Score != new EngineScore(analysis.PlayedScoreKind, analysis.PlayedScoreValue)) { rejected++; continue; }
                var bestChange = StableMaterialChange(position, best.Pv, entry.Colour);
                var playedChange = StableMaterialChange(position, played.Pv, entry.Colour);
                var tags = new List<string>();
                var balance = Material(move.FenBefore, entry.Colour);
                if (playedChange <= -options.MinimumMaterial && bestChange is not null && bestChange > playedChange &&
                    analysis.BestScoreKind == "cp" && analysis.PlayedScoreKind == "cp" &&
                    analysis.CentipawnLoss >= -playedChange * .75 && analysis.PlayedScoreValue <= balance + playedChange * .5)
                    tags.Add("hanging-material");
                if (analysis.MateTransition == "missed-forced-mate" || bestChange >= options.MinimumMaterial && playedChange is not null &&
                    bestChange - playedChange >= options.MinimumMaterial && analysis.CentipawnLoss >= options.MinimumLoss)
                    tags.Add("missed-tactics");
                // Custom setups are not labelled opening play merely because their FEN move number is small.
                if (entry.Game.InitialFen == ChessRules.InitialFen && move.Ply <= options.OpeningFullMoves * 2) tags.Add("opening-mistakes");
                if (clock is { } timing && timing.Remaining <= Math.Clamp(timing.Initial * .1, 10, 60)) tags.Add("time-trouble");
                var explanation = analysis.MateTransition == "missed-forced-mate" ? "The verified engine result found a mating continuation that the played move did not preserve."
                    : analysis.MateTransition == "allowed-forced-mate" ? "The played move allowed a forced mate that the engine's alternative avoided."
                    : $"The played move lost {analysis.CentipawnLoss} centipawns compared with the checked alternative. Review the legal continuation before drawing a lesson.";
                evidence.Add(new(entry.Game.Id, entry.Run.Id, move.Ply, entry.Colour, move.FenBefore, move.Uci, analysis.BestMove,
                    analysis.Classification, analysis.CentipawnLoss, analysis.MateTransition, tags.ToArray(), best.Pv, played.Pv,
                    bestChange, playedChange, clock?.Remaining, clock?.Spent, entry.Game.Opening ?? "Opening unknown",
                    entry.Game.Provider, entry.Game.TimeControl, entry.Game.PlayedAt, entry.Game.Result, explanation));
            }
        }
        var clockMoves = eligible.Count(e => e.Clock);
        var coverage = eligible.Count == 0 ? 0 : (double)clockMoves / eligible.Count;
        var clockAvailable = clockMoves >= options.MinimumClockMoves && coverage >= options.MinimumClockCoverage;
        var findings = new List<WeaknessFinding>();
        foreach (var category in new[] { "hanging-material", "missed-tactics", "opening-mistakes", "time-trouble" })
        {
            if (category == "time-trouble" && !clockAvailable) continue;
            var groups = category == "opening-mistakes"
                ? evidence.Where(e => e.Tags.Contains(category)).GroupBy(e => e.Opening + " · " + e.Side)
                : evidence.Where(e => e.Tags.Contains(category)).GroupBy(_ => "All selected games");
            foreach (var group in groups)
            {
                var sample = eligible.Where(e => category != "time-trouble" || e.Clock).Where(e => category != "opening-mistakes" ||
                    e.Game.Game.InitialFen == ChessRules.InitialFen && e.Move.Ply <= options.OpeningFullMoves * 2 &&
                    (e.Game.Game.Opening ?? "Opening unknown") + " · " + e.Game.Colour == group.Key).ToArray();
                var rows = group.OrderByDescending(e => e.MateTransition is "missed-forced-mate" or "allowed-forced-mate")
                    .ThenByDescending(e => e.CentipawnLoss).ThenBy(e => e.GameId).ThenBy(e => e.Ply).ToArray();
                var affected = rows.Select(e => e.GameId).Distinct().Count();
                var eligibleGames = sample.Select(e => e.Game.Game.Id).Distinct().Count();
                var recurring = eligibleGames >= options.MinimumGames && affected >= options.RecurringGames;
                var label = category switch { "hanging-material" => "Material losses", "missed-tactics" => "Missed tactical opportunities", "opening-mistakes" => "Early-game evaluation losses", _ => "Mistakes ending with little time" };
                var practice = category switch
                {
                    "hanging-material" => "Before committing, check the opponent's forcing replies and whether your material stays safe through the exchanges.",
                    "missed-tactics" => "Calculate checks and captures, then test the opponent's strongest reply before choosing a move.",
                    "opening-mistakes" => "Revisit these early decisions in this opening and colour. Explain the threat before memorising a move.",
                    _ => "Review how much time these decisions consumed. Practise making a short threat check while reserving time for critical positions."
                };
                findings.Add(new(category, group.Key, $"{label} appeared in {affected} of {eligibleGames} eligible games.",
                    recurring ? "recurring-pattern" : "early-signal", recurring ? "moderate" : "low", eligibleGames, sample.Length,
                    rows.Length, affected, sample.Length == 0 ? 0 : (double)rows.Length / sample.Length,
                    rows.Where(e => e.Result == (e.Side == "white" ? "0-1" : "1-0")).Select(e => e.GameId).Distinct().Count(),
                    rows.Sum(e => e.CentipawnLoss ?? 0), rows.Count(e => e.MateTransition is "missed-forced-mate" or "allowed-forced-mate"),
                    rows.Any(e => e.Classification == "blunder") ? "contains-blunders" : "meaningful-losses", practice, rows));
            }
        }
        return new(Version, eligible.Select(e => e.Game.Game.Id).Distinct().Count(), eligible.Count, evidence.Count, provisional,
            rejected, clockMoves, coverage, clockAvailable, findings.OrderByDescending(f => f.Status == "recurring-pattern")
                .ThenByDescending(f => f.AffectedGames).ThenByDescending(f => f.MateTransitions).ThenByDescending(f => f.CentipawnLossSum).ToArray(), evidence.ToArray());
    }

    private static SearchLine? ReadLine(string json, EnginePosition position, string firstMove)
    {
        try
        {
            var result = JsonSerializer.Deserialize<SearchResult>(json);
            var line = result?.Lines.SingleOrDefault(l => l.Rank == 1);
            if (result?.Complete != true || line is null || line.Bound || line.Pv.FirstOrDefault() != firstMove) return null;
            ChessRules.Replay(position.InitialFen, position.History.Concat(line.Pv));
            return line;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    // Require material payoff to persist at two consecutive decision points for
    // the original player, not an intermediate capture before a recapture.
    private static int? StableMaterialChange(EnginePosition position, string[] pv, string side)
    {
        if (pv.Length < 4) return null;
        var board = ChessRules.Replay(position.InitialFen, position.History);
        var baseline = Material(board.ToFen(), side);
        var changes = new List<int>();
        for (var i = 0; i < pv.Length; i++)
        {
            ChessRules.PlayUci(board, pv[i]);
            if (i % 2 == 1) changes.Add(Material(board.ToFen(), side) - baseline);
        }
        if (changes.Count < 2 || Math.Sign(changes[^1]) != Math.Sign(changes[^2])) return null;
        return Math.Abs(changes[^1]) < Math.Abs(changes[^2]) ? changes[^1] : changes[^2];
    }
    private static int Material(string fen, string side)
    {
        var white = 0;
        foreach (var piece in fen.Split(' ')[0])
        {
            var value = char.ToLowerInvariant(piece) switch { 'p' => 100, 'n' => 300, 'b' => 300, 'r' => 500, 'q' => 900, _ => 0 };
            white += char.IsUpper(piece) ? value : -value;
        }
        return side == "white" ? white : -white;
    }
    public static (double Initial, double Remaining, double Spent)? Clock(string? control, GameMove[] moves, GameMove move)
    {
        var parts = control?.Split('+');
        if (parts is null || parts.Length is < 1 or > 2 || !double.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var initial) || initial <= 0 || !double.IsFinite(initial)) return null;
        var increment = 0d;
        if (parts.Length == 2 && (!double.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out increment) || increment < 0 || !double.IsFinite(increment))) return null;
        var earlier = moves.LastOrDefault(m => m.Ply < move.Ply && m.Side == move.Side);
        var before = earlier is null ? moves.FirstOrDefault()?.FenBefore == ChessRules.InitialFen ? initial : (double?)null : earlier.ClockSeconds;
        if (move.ClockSeconds is not { } after || before is null || !double.IsFinite(before.Value) || !double.IsFinite(after) || after < 0) return null;
        var spent = before.Value + increment - after;
        if (spent < -1) return null;
        return (initial, Math.Max(0, after - increment), Math.Max(0, spent));
    }
}
