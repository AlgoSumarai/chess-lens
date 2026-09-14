using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using System.Text.Json;

namespace ChessLens.Core.Training;

public sealed record PuzzleSearchEvidence(EnginePosition Position, SearchBudget Budget, string[]? RestrictedMoves, SearchResult Result);
public sealed record PuzzleChoice(string Move, string? Reply, PuzzleNode? Next, bool Resolved);
public sealed record PuzzleNode(string Fen, PuzzleChoice[] Choices);
public sealed record PuzzleSolution(string Version, string Side, string Objective, int MaterialTarget, EnginePosition StartingPosition, PuzzleNode Root,
    PuzzleSearchEvidence[] Searches, string Explanation);
public sealed record PuzzleValidation(PuzzleSolution? Solution, string? Rejection, string? Diagnostic = null);

/// <summary>Publishes only bounded, stable tactical trees. Rejection is a normal outcome.</summary>
public sealed class PuzzleValidator(IChessEngine engine)
{
    public const string Version = "chesslens-puzzles-v1";
    private readonly List<PuzzleSearchEvidence> evidence = [];
    private static readonly SearchBudget Initial = new("puzzle-check-v1", 250_000, 5000, 8);
    private static readonly SearchBudget Verify = new("puzzle-verify-v1", 750_000, 8000, 8);
    private string? engineName;
    private string? settings;
    private string side = "white";
    private string objective = "mate";
    private int baseline;
    private int target;
    private int nodes;

    public async Task<PuzzleValidation> Validate(EnginePosition position, CancellationToken ct)
    {
        evidence.Clear(); engineName = settings = null; nodes = 0;
        var board = ChessRules.Replay(position.InitialFen, position.History);
        var view = ChessRules.View(board); side = view.SideToMove;
        if (view.LegalMoves.Length == 0) return new(null, "The starting position is already finished.");
        baseline = Material(view.Fen, side);
        try
        {
            var rootPair = await Pair(position, null, ct);
            var best = rootPair.Final.Lines.Single(l => l.Rank == 1);
            if (best.Score.Kind == "mate" && best.Score.Value is > 0 and <= 3)
            { objective = "mate"; target = 0; }
            else
            {
                objective = "material";
                target = SustainedGain(position, best.Pv);
                if (target < 100 || best.Score.Kind != "cp" || best.Score.Value < Math.Max(0, baseline + target / 2))
                    throw Reject("No short mating sequence or stable material gain with sufficient evaluation support was found.");
            }
            var root = await Decision(position, 0, rootPair, ct);
            return new(new(Version, side, objective, target, position, root, evidence.ToArray(), Explain(position, root)), null);
        }
        catch (UnsuitablePuzzle error) { return new(null, error.Message, error.Diagnostic); }
    }

    private async Task<PuzzleNode> Decision(EnginePosition position, int turn, (SearchResult First, SearchResult Final) pair, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (++nodes > 12 || turn >= 3) throw Reject("The solution branches beyond the bounded training horizon.");
        var firstBest = pair.First.Lines.Single(l => l.Rank == 1).Score;
        var finalBest = pair.Final.Lines.Single(l => l.Rank == 1).Score;
        if (!Stable(firstBest, finalBest)) throw Reject("The best evaluation did not remain stable at the deeper budget.");
        var first = Acceptable(pair.First, firstBest);
        var final = Acceptable(pair.Final, finalBest);
        if (!first.Keys.Order().SequenceEqual(final.Keys.Order()) || first.Count == 0 || first.Count > 4)
            throw Reject("The acceptable alternatives are unstable or too numerous for a focused puzzle.");
        foreach (var move in first.Keys)
            if (!Stable(first[move].Score, final[move].Score)) throw Reject("An alternative's evaluation changed during validation.");
        // Search every move omitted from MultiPV as one restricted set. A sound
        // unlisted alternative makes the candidate unsuitable, never a wrong answer.
        var legal = ChessRules.View(ChessRules.Replay(position.InitialFen, position.History)).LegalMoves;
        var omitted = legal.Except(pair.Final.Lines.Select(l => l.Pv[0])).ToArray();
        if (omitted.Length > 0)
        {
            var excluded = await Pair(position, omitted, ct, 1);
            if (Near(firstBest, excluded.First.Lines[0].Score) || Near(finalBest, excluded.Final.Lines[0].Score))
                throw Reject("An additional sound alternative falls outside the bounded solution set.");
            if (!Stable(excluded.First.Lines[0].Score, excluded.Final.Lines[0].Score))
                throw Reject("The evaluation of an unlisted alternative is unstable.");
        }
        // Require a clear gap, not a fragile 51-centipawn cutoff.
        if (pair.First.Lines.Any(l => !first.ContainsKey(l.Pv[0]) && Near(firstBest, l.Score)) ||
            pair.Final.Lines.Any(l => !final.ContainsKey(l.Pv[0]) && Near(finalBest, l.Score)))
            throw Reject("The boundary between acceptable and inferior moves is unclear.");

        var choices = new List<PuzzleChoice>();
        foreach (var move in final.Keys.Order())
        {
            var after = Append(position, move);
            var afterBoard = ChessRules.Replay(after.InitialFen, after.History);
            if (afterBoard.IsEndGame)
            {
                if (objective != "mate" || !IsMate(afterBoard)) throw Reject("A branch ends without achieving the objective.");
                choices.Add(new(move, null, null, true)); continue;
            }
            var defence = await Pair(after, null, ct, 1);
            var d1 = defence.First.Lines.Single(l => l.Rank == 1);
            var d2 = defence.Final.Lines.Single(l => l.Rank == 1);
            if (!Stable(d1.Score, d2.Score)) throw Reject("The defensive evaluation is unstable.");
            // Several equivalent defences may exist. Play the final strongest
            // defence; the engine's minimax score supports this choice.
            if (objective == "mate" && (d1.Score.Kind != "mate" || d1.Score.Value >= 0 || d2.Score.Value >= 0))
                throw Reject("A defensive search no longer confirms the forced mate.");
            var reply = d2.Pv[0]; var next = Append(after, reply);
            var nextBoard = ChessRules.Replay(next.InitialFen, next.History);
            if (nextBoard.IsEndGame) throw Reject("The checked defence ends the branch without the objective.");
            var nextPair = await Pair(next, null, ct);
            if (objective == "material" && Material(nextBoard.ToFen(), side) - baseline >= target &&
                SustainedGain(next, nextPair.First.Lines[0].Pv, baseline) >= target &&
                SustainedGain(next, nextPair.Final.Lines[0].Pv, baseline) >= target &&
                Stable(nextPair.First.Lines[0].Score, nextPair.Final.Lines[0].Score) &&
                nextPair.Final.Lines[0].Score.Kind == "cp" && nextPair.Final.Lines[0].Score.Value >= Math.Max(0, baseline + target / 2))
                choices.Add(new(move, reply, null, true));
            else choices.Add(new(move, reply, await Decision(next, turn + 1, nextPair, ct), false));
        }
        return new(ChessRules.Replay(position.InitialFen, position.History).ToFen(), choices.ToArray());
    }

    private Dictionary<string, SearchLine> Acceptable(SearchResult result, EngineScore best) => result.Lines
        .Where(l => Equivalent(best, l.Score)).ToDictionary(l => l.Pv[0]);
    private bool Equivalent(EngineScore best, EngineScore other) => objective == "mate"
        ? best.Kind == "mate" && best.Value > 0 && other.Kind == "mate" && other.Value is > 0 and <= 3
        : best.Kind == "cp" && other.Kind == "cp" && (long)best.Value - other.Value <= 50;
    private bool Near(EngineScore best, EngineScore other) => Equivalent(best, other) ||
        best.Kind == "mate" && best.Value > 0 && other.Kind == "mate" && other.Value > 0 ||
        best.Kind == "cp" && other.Kind == "cp" && (long)best.Value - other.Value < 100;
    private static bool Stable(EngineScore a, EngineScore b) => a.Kind == b.Kind &&
        (a.Kind == "cp" ? Math.Abs((long)a.Value - b.Value) <= 75 : a.Value == b.Value);

    private string Explain(EnginePosition position, PuzzleNode root)
    {
        var board = ChessRules.Replay(position.InitialFen, position.History); var notation = new List<string>();
        PuzzleNode? node = root;
        while (node is not null)
        {
            var choice = node.Choices[0]; notation.Add(ChessRules.PlayUci(board, choice.Move).San ?? choice.Move);
            if (choice.Reply is not null) notation.Add(ChessRules.PlayUci(board, choice.Reply).San ?? choice.Reply);
            node = choice.Next;
        }
        var line = string.Join(' ', notation);
        return objective == "mate"
            ? $"The checked continuation is {line}. It ends in checkmate: the defending king is in check and has no legal reply. Reusable lesson: calculate forcing checks and the king's escape squares before choosing a quiet move."
            : $"The checked continuation is {line}. It improves your material balance by {(Material(board.ToFen(), side) - baseline) / 100.0:0.#} points after the defensive reply, and the checked continuation keeps at least {target / 100.0:0.#}. Reusable lesson: calculate the opponent's best recapture before deciding whether a capture wins material. Piece values describe material, not the whole position.";
    }

    private async Task<(SearchResult First, SearchResult Final)> Pair(EnginePosition position, string[]? restricted, CancellationToken ct, int multiPv = 8)
        => (await Search(position, Initial with { MultiPv = multiPv }, restricted, ct), await Search(position, Verify with { MultiPv = multiPv }, restricted, ct));
    private async Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? restricted, CancellationToken ct)
    {
        if (evidence.Count >= 48) throw Reject("The puzzle reached its 48-search validation budget.");
        var result = await engine.Search(position, budget, restricted, 20, ct);
        var legal = ChessRules.View(ChessRules.Replay(position.InitialFen, position.History)).LegalMoves;
        var count = Math.Min(budget.MultiPv, restricted?.Length ?? legal.Length);
        if (!result.Complete || result.Lines.Count != count || result.Lines.Any(l => l.Bound || l.Depth < 1 || l.Pv.Length == 0) ||
            !result.Lines.Select(l => l.Rank).Order().SequenceEqual(Enumerable.Range(1, count)) || result.Lines[0].Pv[0] != result.BestMove ||
            result.Lines.Select(l => l.Pv[0]).Distinct().Count() != count)
            throw new UnsuitablePuzzle("The engine did not return complete exact candidate evidence.",
                $"profile={budget.Profile}; history={position.History.Length}; restricted={restricted?.Length}; complete={result.Complete}; count={result.Lines.Count}/{count}; " +
                $"firstMatches={result.Lines.FirstOrDefault()?.Pv.FirstOrDefault() == result.BestMove}; unique={result.Lines.Select(l => l.Pv.FirstOrDefault()).Distinct().Count()}; " +
                string.Join(';', result.Lines.Select(l => $"rank={l.Rank},depth={l.Depth},nodes={l.Nodes},bound={l.Bound},pv={l.Pv.Length}")));
        foreach (var line in result.Lines)
        {
            ChessRules.Replay(position.InitialFen, position.History.Concat(line.Pv));
            if (restricted is not null && !restricted.Contains(line.Pv[0])) throw Reject("The engine ignored a restricted alternative check.");
        }
        var configuration = JsonSerializer.Serialize(JsonDocument.Parse(result.SettingsJson).RootElement.EnumerateObject()
            .Where(p => p.Name != "multiPv").ToDictionary(p => p.Name, p => p.Value));
        engineName ??= result.Engine; settings ??= configuration;
        if (engineName != result.Engine || settings != configuration) throw Reject("Engine settings changed during puzzle validation.");
        evidence.Add(new(position, budget, restricted, result)); return result;
    }
    private int SustainedGain(EnginePosition position, string[] pv, int? originalBalance = null)
    {
        var board = ChessRules.Replay(position.InitialFen, position.History);
        var start = originalBalance ?? Material(board.ToFen(), side); var gains = new List<int>();
        // The objective must become visible within the playable six-ply horizon.
        foreach (var move in pv.Take(6))
        {
            ChessRules.PlayUci(board, move);
            if (ChessRules.View(board).SideToMove == side) gains.Add(Material(board.ToFen(), side) - start);
        }
        return gains.Count < 2 ? 0 : Math.Min(gains[^1], gains[^2]);
    }
    public static int Material(string fen, string side)
    {
        var balance = fen.Split(' ')[0].Sum(c => (char.IsUpper(c) ? 1 : -1) * (char.ToLowerInvariant(c) switch
            { 'p' => 100, 'n' or 'b' => 300, 'r' => 500, 'q' => 900, _ => 0 }));
        return side == "white" ? balance : -balance;
    }
    private static bool IsMate(Chess.ChessBoard board) => board.EndGame?.EndgameType.ToString() == "Checkmate";
    private static EnginePosition Append(EnginePosition p, string move) => p with { History = [.. p.History, move] };
    private static UnsuitablePuzzle Reject(string message) => new(message);
    private sealed class UnsuitablePuzzle(string message, string? diagnostic = null) : Exception(message)
    { public string? Diagnostic { get; } = diagnostic; }
}
