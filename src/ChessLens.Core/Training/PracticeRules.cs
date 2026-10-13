using ChessLens.Core.Analysis;
using ChessLens.Core.Games;

namespace ChessLens.Core.Training;

/// <summary>Bounded objective grading. A practical opponent's score is never a grade.</summary>
public sealed class PracticeRules(IChessEngine engine, int verificationAttempt = 1)
{
    public const string Version = "chesslens-practice-v2";
    public static SearchBudget Check => new("practice-grade-v1", 250_000, 5000);
    public static SearchBudget Verify => new("practice-verify-v1", 750_000, 8000);
    public static SearchBudget Opponent => new("practice-opponent-v1", 30_000, 1000);
    public const int OpponentSkill = 6;

    public static PracticeObjective Objective(EngineScore score) => score.Kind == "mate" && score.Value > 0
        ? new("retain-mate", "Keep the forced mate", "Keep a verified forced mate through three of your decisions, or deliver checkmate sooner.")
        : score.Kind == "cp" && score.Value >= 300
            ? new("preserve-advantage", "Preserve your advantage", "Keep an advantage and avoid another significant evaluation loss through three of your decisions.")
            : new("decision-safety", "Improve decision safety", "Avoid another significant evaluation loss through three decisions, even if the starting position is already difficult.");

    public async Task<PracticePair> Pair(EnginePosition position, string[]? restricted, CancellationToken ct)
    {
        if (verificationAttempt is < 1 or > 3) throw new ArgumentException("Practice has at most three verification executions.");
        var multiplier = 1L << (verificationAttempt - 1);
        var first = await Search(position, Check with { Nodes = Check.Nodes * multiplier, Profile = Check.Profile + (verificationAttempt > 1 ? $"-retry{verificationAttempt}" : "") }, restricted, 20, ct);
        var final = await Search(position, Verify with { Nodes = Verify.Nodes * multiplier, Profile = Verify.Profile + (verificationAttempt > 1 ? $"-retry{verificationAttempt}" : "") }, restricted, 20, ct);
        if (first.Result.Engine != final.Result.Engine || first.Result.SettingsJson != final.Result.SettingsJson ||
            Score(first).Kind != Score(final).Kind || Score(first).Kind == "mate" && Score(first).Value != Score(final).Value)
            throw new UnstablePracticeEvidence($"The two grading budgets did not agree: {Score(first)} / {Score(final)}, nodes {first.Budget.Nodes}/{final.Budget.Nodes}.");
        return new(first, final);
    }

    public async Task<PracticeTurn> Turn(EnginePosition start, string[] history, PracticeObjective objective, int decision, CancellationToken ct)
    {
        if (history.Length == 0 || decision is < 1 or > 3) throw new ArgumentException("Practice has no pending decision.");
        var side = ChessRules.View(ChessRules.Replay(start.InitialFen, start.History)).SideToMove;
        var move = history[^1];
        var before = start with { History = [.. start.History, .. history[..^1]] };
        var board = ChessRules.Replay(before.InitialFen, before.History);
        var beforeView = ChessRules.PracticeView(start.InitialFen, start.History, history[..^1]);
        if (beforeView.SideToMove != side || beforeView.Endgame is not null) throw new ArgumentException("The pending move is not a player decision.");
        ChessRules.PlayUci(board, move);
        var best = await Pair(before, null, ct); var played = await Pair(before, [move], ct);
        if (best.Final.Result.Engine != played.Final.Result.Engine || best.Final.Result.SettingsJson != played.Final.Result.SettingsJson)
            throw new UnstablePracticeEvidence("Grading settings changed between best and played searches.");
        if (Contradicts(Score(best.First), Score(played.First)) || Contradicts(Score(best.Final), Score(played.Final)))
            throw new UnstablePracticeEvidence("The restricted move contradicts the unrestricted best score.");
        if (Score(best.First).Kind == "cp" && Score(played.First).Kind == "cp" &&
            !RelativeLossAgrees(Score(best.First), Score(played.First), Score(best.Final), Score(played.Final)))
            throw new UnstablePracticeEvidence("The decision's relative loss changed by more than 75 centipawns between budgets.");
        var firstAccepted = Accepts(objective, Score(best.First), Score(played.First));
        var finalAccepted = Accepts(objective, Score(best.Final), Score(played.Final));
        if (firstAccepted != finalAccepted) throw new UnstablePracticeEvidence("The acceptance boundary changed between grading budgets.");
        var grade = new PracticeGrade(decision, move, finalAccepted,
            finalAccepted ? "The chosen legal move met the objective at both grading budgets." : "The chosen move did not meet the objective at both grading budgets.", best, played);
        var terminal = ChessRules.PracticeView(start.InitialFen, start.History, history).Endgame;
        if (!finalAccepted || terminal is not null) return new(history, grade, true, finalAccepted ? "succeeded" : "needs-practice", terminal, null,
            Conclusion: finalAccepted ? "The checked decision met the objective and the position finished." : "The chosen move failed the learning objective at both full-strength budgets.");
        var after = start with { History = [.. start.History, .. history] };
        var opponent = await Search(after, Opponent, null, OpponentSkill, ct);
        ChessRules.PlayUci(board, opponent.Result.BestMove);
        var updated = history.Append(opponent.Result.BestMove).ToArray(); terminal = ChessRules.PracticeView(start.InitialFen, start.History, updated).Endgame;
        // A practical reply can only improve the player's chances relative to the full-strength root check.
        // Any terminal reply is still adjudicated by the rules library, including history-based draws.
        if (terminal is not null)
        {
            var checkmate = terminal == "Checkmate";
            var acceptedTerminal = objective.Kind == "decision-safety" && (checkmate
                ? Score(best.First).Kind == "mate" && Score(best.First).Value < 0 && Score(best.Final).Kind == "mate" && Score(best.Final).Value < 0
                : Accepts(objective, Score(best.First), new("cp", 0)) && Accepts(objective, Score(best.Final), new("cp", 0)));
            return new(updated, grade, true, acceptedTerminal ? "succeeded" : "needs-practice", terminal, opponent,
                Conclusion: acceptedTerminal ? "The position finished after the opponent reply, with the decision-safety objective met." : "Your decision passed its checks, but the terminal continuation did not retain the stated advantage or mating objective.");
        }
        if (decision < objective.Decisions) return new(updated, grade, false, null, null, opponent);
        var final = await Pair(start with { History = [.. start.History, .. updated] }, null, ct);
        var achieved = Retained(objective, Score(final.First)) && Retained(objective, Score(final.Final));
        if (Retained(objective, Score(final.First)) != Retained(objective, Score(final.Final)))
            throw new UnstablePracticeEvidence("The final objective changed between budgets.");
        return new(updated, grade, true, achieved ? "succeeded" : "needs-practice", null, opponent, final,
            achieved ? "All three player decisions met their checks and the final position retained the objective at both full-strength budgets." : "The decisions passed their checks, but fresh final-position evidence did not retain the objective. Compare the continuation.");
    }

    public static bool Accepts(PracticeObjective objective, EngineScore best, EngineScore played)
    {
        if (objective.Kind == "retain-mate") return best.Kind == "mate" && best.Value > 0 && played.Kind == "mate" && played.Value > 0;
        if (played.Kind == "mate" && played.Value > 0) return true;
        if (best.Kind == "mate" && best.Value > 0) return false;
        if (played.Kind == "mate" && played.Value < 0)
            return objective.Kind == "decision-safety" && best.Kind == "mate" && best.Value < 0;
        if (best.Kind != "cp" || played.Kind != "cp") return false;
        return (long)best.Value - played.Value <= objective.MaximumLoss &&
            (objective.Kind != "preserve-advantage" || played.Value >= objective.MinimumAdvantage);
    }

    public static bool RelativeLossAgrees(EngineScore firstBest, EngineScore firstPlayed, EngineScore finalBest, EngineScore finalPlayed) =>
        new[] { firstBest, firstPlayed, finalBest, finalPlayed }.All(s => s.Kind == "cp") &&
        Math.Abs(Loss(firstBest, firstPlayed) - Loss(finalBest, finalPlayed)) <= 75;
    public static EngineScore Score(PracticeSearch search) => search.Result.Lines.Single(l => l.Rank == 1).Score;
    private static long Loss(EngineScore best, EngineScore played) => Math.Max(0, (long)best.Value - played.Value);
    public static bool Retained(PracticeObjective objective, EngineScore score) => objective.Kind switch
    {
        "retain-mate" => score.Kind == "mate" && score.Value > 0,
        "preserve-advantage" => score.Kind == "mate" && score.Value > 0 || score.Kind == "cp" && score.Value >= objective.MinimumAdvantage,
        "decision-safety" => true, // All player decisions already passed relative-loss grading, including already losing starts.
        _ => false
    };
    // A restricted search can concentrate its budget and discover a stronger candidate than the
    // unrestricted centipawn estimate. Treat that as zero loss; do not penalize a sound alternative.
    // A claimed unavoidable losing mate contradicted by a surviving candidate still needs verification.
    private static bool Contradicts(EngineScore best, EngineScore played) =>
        best.Kind == "mate" && best.Value < 0 && (played.Kind != "mate" || played.Value >= 0);

    private async Task<PracticeSearch> Search(EnginePosition position, SearchBudget budget, string[]? restricted, int skill, CancellationToken ct)
    {
        var result = await engine.Search(position, budget, restricted, skill, ct);
        var line = result.Lines.SingleOrDefault(l => l.Rank == 1);
        var legal = ChessRules.View(ChessRules.Replay(position.InitialFen, position.History)).LegalMoves;
        if (!result.Complete || line is null || result.Lines.Count != 1 || line.Bound || line.Depth < 1 || line.Pv.Length == 0 ||
            !legal.Contains(result.BestMove) || restricted is not null && !restricted.Contains(result.BestMove) ||
            skill == 20 && line.Pv[0] != result.BestMove || line.Score.Kind is not ("cp" or "mate"))
            throw new UnstablePracticeEvidence("The engine did not return exact, complete, legal grading evidence.");
        ChessRules.Replay(position.InitialFen, position.History.Concat(line.Pv));
        return new(position, budget, restricted, result);
    }
}

public sealed class UnstablePracticeEvidence(string message) : InvalidOperationException(message);
