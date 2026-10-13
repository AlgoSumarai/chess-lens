using System.Text.Json;
using ChessLens.Core.Games;

namespace ChessLens.Core.Analysis;

public sealed record VerifiedMoveEvidence(EnginePosition Position, GameMove SourceMove, MoveAnalysis Analysis, SearchResult BestSearch,
    SearchResult PlayedSearch, SearchLine BestLine, SearchLine PlayedLine);

/// <summary>Checks stored evidence consistency and legal replay; this is not a proof of perfect chess.</summary>
public static class VerifiedEvidence
{
    public static VerifiedMoveEvidence? Read(SourceGame game, AnalysisRun run, MoveAnalysis move)
    {
        if (run.State != "completed" || run.GameId != game.Id || move.RunId != run.Id || !move.Complete || move.Provisional) return null;
        var ordered = game.Moves.OrderBy(m => m.Ply).ToArray();
        var source = ordered.SingleOrDefault(m => m.Ply == move.Ply);
        if (source is null || source.Uci != move.PlayedMove || source.Side != move.Side || source.FenBefore != move.FenBefore) return null;
        var position = new EnginePosition(game.InitialFen, ordered.Where(m => m.Ply < move.Ply).Select(m => m.Uci).ToArray());
        try
        {
            if (ChessRules.Replay(position.InitialFen, position.History).ToFen() != move.FenBefore) return null;
            var best = JsonSerializer.Deserialize<SearchResult>(move.BestSearchJson);
            var played = JsonSerializer.Deserialize<SearchResult>(move.PlayedSearchJson);
            var b = best?.Lines.SingleOrDefault(l => l.Rank == 1); var p = played?.Lines.SingleOrDefault(l => l.Rank == 1);
            if (best?.Complete != true || played?.Complete != true || b is null || p is null || b.Bound || p.Bound ||
                best.Engine != played.Engine || best.SettingsJson != played.SettingsJson || best.BestMove != move.BestMove || played.BestMove != move.PlayedMove ||
                b.Pv.FirstOrDefault() != move.BestMove || p.Pv.FirstOrDefault() != move.PlayedMove ||
                run.EngineVersion is not null && run.EngineVersion != best.Engine || run.SettingsJson is not null && run.SettingsJson != best.SettingsJson ||
                b.Score != new EngineScore(move.BestScoreKind, move.BestScoreValue) || p.Score != new EngineScore(move.PlayedScoreKind, move.PlayedScoreValue)) return null;
            ChessRules.Replay(position.InitialFen, position.History.Concat(b.Pv));
            ChessRules.Replay(position.InitialFen, position.History.Concat(p.Pv));
            var settings = JsonSerializer.Deserialize<AnalysisOptions>(run.ClassificationSettingsJson ?? "{}")!;
            var comparison = settings.Compare(b.Score, p.Score, false);
            if (comparison.Label != move.Classification || comparison.CentipawnLoss != move.CentipawnLoss || comparison.MateTransition != move.MateTransition || comparison.NeedsVerification) return null;
            return new(position, source, move, best, played, b, p);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException) { return null; }
    }
}
