using Chess;

namespace ChessLens.Core.Games;

/// <summary>All server move validation goes through the established Gera.Chess rules library.</summary>
public static class ChessRules
{
    public const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public static ChessBoard Replay(string initialFen, IEnumerable<string> history)
    {
        var board = ChessBoard.LoadFromFen(initialFen);
        foreach (var uci in history) PlayUci(board, uci);
        return board;
    }

    public static string Uci(Move move)
    {
        var type = move.Promotion?.Type;
        var promotion = type == PieceType.Queen ? "q" : type == PieceType.Rook ? "r" : type == PieceType.Bishop ? "b" : type == PieceType.Knight ? "n" : "";
        return $"{move.OriginalPosition}{move.NewPosition}{promotion}";
    }

    public static Move PlayUci(ChessBoard board, string uci)
    {
        if (string.IsNullOrEmpty(uci) || uci.Length is < 4 or > 5) throw new ArgumentException("Use a legal coordinate move, for example e2e4 or a7a8q.");
        var move = board.Moves().FirstOrDefault(m => Uci(m) == uci);
        if (move is null || !board.Move(move)) throw new ArgumentException("That move is not legal in this position.");
        return board.ExecutedMoves[^1];
    }

    public static PositionView View(ChessBoard board) => new(board.ToFen(), board.Turn == PieceColor.White ? "white" : "black",
        board.IsEndGame ? [] : board.Moves().Select(Uci).ToArray(), board.EndGame?.EndgameType.ToString());
}

public sealed record PositionView(string Fen, string SideToMove, string[] LegalMoves, string? Endgame);
