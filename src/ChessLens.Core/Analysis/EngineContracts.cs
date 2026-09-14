using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChessLens.Core.Analysis;

/// <summary>Value is in the position's side-to-move perspective. Mate distance is never converted to centipawns.</summary>
public sealed record EngineScore(string Kind, int Value)
{
    public EngineScore Opposite() => this with { Value = -Value };
    public EngineScore ForWhite(string sideToMove) => sideToMove == "white" ? this : Opposite();
}

public sealed record SearchBudget(string Profile, long Nodes, int MaxMilliseconds, int MultiPv = 1)
{
    public static SearchBudget Quick => new("quick-v1", 30_000, 1000);
    public static SearchBudget Deep => new("deep-v1", 250_000, 5000);
    public void Validate()
    {
        if (Nodes is < 1000 or > 5_000_000 || MaxMilliseconds is < 50 or > 15_000 || MultiPv is < 1 or > 8)
            throw new ArgumentException("Engine budget is outside the configured safety bounds.");
    }
}

public sealed record SearchLine(int Rank, int Depth, long Nodes, int ElapsedMilliseconds, EngineScore Score, string[] Pv, bool Bound);
public sealed record SearchResult(string Engine, string SettingsJson, string BestMove, IReadOnlyList<SearchLine> Lines,
    int ElapsedMilliseconds, long PeakMemoryBytes, bool Complete, bool CacheHit = false);
public sealed record EnginePosition(string InitialFen, string[] History)
{
    public string CacheIdentity(string engine, string settings, SearchBudget budget, string[]? searchMoves = null)
    {
        var data = JsonSerializer.Serialize(new { version = 1, InitialFen, History, engine, settings, budget, searchMoves });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }
}

public interface IChessEngine
{
    Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct);
}

public sealed record MoveClassification(string Label, int? CentipawnLoss, string? MateTransition, bool NeedsVerification);

public sealed class AnalysisOptions
{
    public int InaccuracyCentipawns { get; set; } = 50;
    public int MistakeCentipawns { get; set; } = 100;
    public int BlunderCentipawns { get; set; } = 200;
    public void Validate()
    {
        if (InaccuracyCentipawns < 1 || MistakeCentipawns <= InaccuracyCentipawns ||
            BlunderCentipawns <= MistakeCentipawns || BlunderCentipawns > 10_000)
            throw new ArgumentException("Classification thresholds must increase from inaccuracy to mistake to blunder, within 1–10,000 centipawns.");
    }
    public MoveClassification Compare(EngineScore best, EngineScore played, bool provisional)
    {
        Validate();
        return MoveClassifier.Compare(best, played, provisional, InaccuracyCentipawns, MistakeCentipawns, BlunderCentipawns);
    }
}

public static class MoveClassifier
{
    public static MoveClassification Compare(EngineScore best, EngineScore played, bool provisional, int inaccuracy = 50, int mistake = 100, int blunder = 200)
    {
        if (best.Kind == "cp" && played.Kind == "cp")
        {
            var loss = Math.Max(0, best.Value - played.Value);
            return new(loss >= blunder ? "blunder" : loss >= mistake ? "mistake" : loss >= inaccuracy ? "inaccuracy" : "sound",
                loss, null, provisional || played.Value > best.Value + 30);
        }
        if (played.Kind == "mate" && played.Value < 0 && (best.Kind != "mate" || best.Value > 0))
            return new("blunder", null, "allowed-forced-mate", provisional);
        if (best.Kind == "mate" && best.Value > 0 && (played.Kind != "mate" || played.Value <= 0))
            return new("mistake", null, "missed-forced-mate", provisional);
        if (best.Kind == "mate" && played.Kind == "mate" && Math.Sign(best.Value) == Math.Sign(played.Value))
            return new("sound", null, best.Value > 0 ? "mate-maintained" : "already-facing-mate", provisional);
        return new("unresolved", null, "unstable-mate-transition", true);
    }
}
