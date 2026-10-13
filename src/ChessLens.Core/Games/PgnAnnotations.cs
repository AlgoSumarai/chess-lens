using System.Text;
using System.Text.Json;

namespace ChessLens.Core.Games;

/// <summary>Canonical shared chess data and private first-upload annotations have separate lifetimes.</summary>
public static class PgnAnnotations
{
    public static string Clocks(SourceGame parsed) => JsonSerializer.Serialize(parsed.Moves.OrderBy(m => m.Ply).Select(m => m.ClockSeconds));

    public static List<GameMove> Moves(SourceGame source, string? ownedClocks)
    {
        double?[]? clocks = ownedClocks is null ? null : JsonSerializer.Deserialize<double?[]>(ownedClocks);
        if (clocks is not null && (clocks.Length != source.Moves.Count || clocks.Any(c => c is { } value && (!double.IsFinite(value) || value < 0))))
            throw new ArgumentException("The saved owner clock data is inconsistent.");
        return source.Moves.OrderBy(m => m.Ply).Select((m, index) => new GameMove { GameId = m.GameId, Ply = m.Ply, San = m.San, Uci = m.Uci,
            FenBefore = m.FenBefore, FenAfter = m.FenAfter, Side = m.Side,
            ClockSeconds = source.Provider == "pgn" ? clocks?[index] : m.ClockSeconds }).ToList();
    }

    public static string Canonical(SourceGame source)
    {
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(source.HeadersJson) ?? [];
        if (source.InitialFen != ChessRules.InitialFen && !headers.Keys.Any(k => k.Equals("FEN", StringComparison.OrdinalIgnoreCase)))
        { headers["FEN"] = source.InitialFen; headers["SetUp"] = "1"; }
        var output = new StringBuilder();
        foreach (var (key, value) in headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (key.Equals("Annotator", StringComparison.OrdinalIgnoreCase) || key.Equals("PlyCount", StringComparison.OrdinalIgnoreCase)) continue;
            output.Append('[').Append(key).Append(" \"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).AppendLine("\"]");
        }
        output.AppendLine(); var first = true;
        foreach (var move in source.Moves.OrderBy(m => m.Ply))
        {
            if (move.Side == "white" || first)
                output.Append(move.FenBefore.Split(' ')[5]).Append(move.Side == "white" ? ". " : "... ");
            output.Append(move.San).Append(' '); first = false;
        }
        return output.Append(source.Result).ToString();
    }
}
