using ChessLens.Core.Analysis;
using ChessLens.Core.Games;

namespace ChessLens.Core.Insights;

public sealed record InsightCategoryCoverage(string Category, int CandidateDecisions, int StableEvidenceDecisions, int EligibleDecisions,
    int EligibleGames, int MissingOrProvisionalDecisions, int RejectedEvidenceDecisions, string Denominator,
    int CentipawnOpportunities = 0, int MateOpportunities = 0);
internal sealed record ObjectiveDecision(InsightGame Game, GameMove Move, VerifiedMoveEvidence Evidence, bool Endgame, string? ConversionObjective);

internal sealed class ObjectiveInsights
{
    public List<ObjectiveDecision> Decisions { get; } = [];
    public Dictionary<(Guid GameId, int Ply), ObjectiveDecision> ByDecision { get; } = [];
    public HashSet<(Guid GameId, int Ply)> ConversionDecisions { get; } = [];
    public HashSet<(Guid GameId, int Ply)> EndgameDecisions { get; } = [];
    public List<(InsightGame Game, GameMove Move)> EndgameCandidates { get; } = [];
    public int CandidateDecisions { get; private set; }
    public int Rejected { get; private set; }
    public int EndgameRejected { get; private set; }

    public static ObjectiveInsights Build(IReadOnlyList<InsightGame> games, InsightOptions options, CancellationToken ct)
    {
        var result = new ObjectiveInsights();
        foreach (var game in games.GroupBy(g => g.Game.Id).Select(group => group.OrderByDescending(g => g.Run.CreatedAt).ThenBy(g => g.Run.Id).First()))
        {
            if (game.Colour is not ("white" or "black") || game.Run.State != "completed" || game.Run.GameId != game.Game.Id) continue;
            var cpStreak = 0; var mateStreak = 0;
            VerifiedMoveEvidence? previous = null;
            foreach (var source in game.Game.Moves.OrderBy(m => m.Ply).Where(m => m.Side == game.Colour))
            {
                ct.ThrowIfCancellationRequested(); result.CandidateDecisions++;
                var endgame = IsEndgame(source.FenBefore, options);
                if (endgame) result.EndgameCandidates.Add((game, source));
                var move = game.Run.Moves.SingleOrDefault(m => m.Ply == source.Ply);
                var evidence = move is null || !move.Complete || move.Provisional ? null : VerifiedEvidence.Read(game.Game, game.Run, move);
                if (evidence is null)
                {
                    if (move is { Complete: true, Provisional: false }) { result.Rejected++; if (endgame) result.EndgameRejected++; }
                    cpStreak = 0; mateStreak = 0; previous = null; continue;
                }
                var cpAdvantage = evidence.BestLine.Score.Kind == "cp" && evidence.BestLine.Score.Value >= options.ConversionAdvantageCentipawns;
                var winningMate = evidence.BestLine.Score.Kind == "mate" && evidence.BestLine.Score.Value > 0;
                cpStreak = cpAdvantage ? previous is not null && previous.SourceMove.Ply == source.Ply - 2 &&
                    previous.PlayedLine.Score.Kind == "cp" && previous.PlayedLine.Score.Value >= options.ConversionAdvantageCentipawns ? cpStreak + 1 : 1 : 0;
                mateStreak = winningMate ? previous is not null && previous.SourceMove.Ply == source.Ply - 2 &&
                    previous.PlayedLine.Score.Kind == "mate" && previous.PlayedLine.Score.Value > 0 ? mateStreak + 1 : 1 : 0;
                var objective = cpStreak >= options.ConversionPersistenceDecisions ? "centipawn-advantage" :
                    mateStreak >= options.ConversionPersistenceDecisions ? "forced-mate-continuation" : null;
                var decision = new ObjectiveDecision(game, source, evidence, endgame, objective);
                result.Decisions.Add(decision); result.ByDecision.Add((game.Game.Id, source.Ply), decision);
                if (endgame) result.EndgameDecisions.Add((game.Game.Id, source.Ply));
                if (objective is not null) result.ConversionDecisions.Add((game.Game.Id, source.Ply));
                previous = evidence;
            }
        }
        return result;
    }

    public static bool IsEndgame(string fen, InsightOptions options)
    {
        var pieces = fen.Split(' ')[0];
        return (options.EndgameAllowQueens || !pieces.Any(c => c is 'q' or 'Q')) &&
            pieces.Count(c => char.ToLowerInvariant(c) is 'n' or 'b' or 'r' or 'q') <= options.EndgameMaximumNonPawnPieces;
    }
    public static bool ConversionFailed(ObjectiveDecision decision, InsightOptions options) => decision.ConversionObjective switch
    {
        "centipawn-advantage" => decision.Evidence.PlayedLine.Score.Kind == "cp" && decision.Evidence.PlayedLine.Score.Value <= options.ConversionLostAdvantageCentipawns ||
            decision.Evidence.Analysis.MateTransition == "allowed-forced-mate",
        "forced-mate-continuation" => decision.Evidence.Analysis.MateTransition == "missed-forced-mate", _ => false
    };
    public InsightCategoryCoverage[] Coverage()
    {
        var end = Decisions.Where(d => d.Endgame).ToArray(); var conversion = Decisions.Where(d => d.ConversionObjective is not null).ToArray();
        return [new("conversion-problems", CandidateDecisions, Decisions.Count, conversion.Length,
            conversion.Select(d => d.Game.Game.Id).Distinct().Count(), CandidateDecisions - Decisions.Count - Rejected, Rejected,
            "decisions with a persisted checked advantage or winning mate", conversion.Count(d => d.ConversionObjective == "centipawn-advantage"),
            conversion.Count(d => d.ConversionObjective == "forced-mate-continuation")),
            new("endgame-errors", EndgameCandidates.Count, end.Length, end.Length, end.Select(d => d.Game.Game.Id).Distinct().Count(),
                EndgameCandidates.Count - end.Length - EndgameRejected, EndgameRejected, "material-eligible endgame decisions with stable legal evidence")];
    }
}
