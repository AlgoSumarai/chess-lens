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

    /// <summary>Source games may continue after claimable draws. Practice claims them at the current position.</summary>
    public static PositionView PracticeView(string initialFen, IEnumerable<string> sourceHistory, IEnumerable<string> practiceHistory)
    {
        var board = Replay(initialFen, []);
        var counts = new Dictionary<string, int> { [RepetitionIdentity(board)] = 1 };
        foreach (var move in sourceHistory.Concat(practiceHistory))
        {
            PlayUci(board, move);
            var key = RepetitionIdentity(board); counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        var view = View(board); if (view.Endgame is not null) return view; // Checkmate takes precedence over draw counters.
        // Gera.Chess 1.2.0 incorrectly declares K+NN versus K insufficient, although a legal mate is
        // possible. Keep its legal/checkmate/stalemate path, and use conservative standard dead-material
        // cases here rather than equating 'cannot force mate' with 'no legal mate can occur'.
        var ending = DeadMaterial(view.Fen) ? "InsufficientMaterial" : null;
        if (ending is null && int.Parse(view.Fen.Split(' ')[4], System.Globalization.CultureInfo.InvariantCulture) >= 100) ending = "FiftyMoveRule";
        if (ending is null && counts[RepetitionIdentity(board)] >= 3) ending = "Repetition";
        return ending is null ? view : view with { LegalMoves = [], Endgame = ending };
    }

    private static bool DeadMaterial(string fen)
    {
        var minors = new List<(char Piece, int SquareColour)>();
        var ranks = fen.Split(' ')[0].Split('/');
        for (var rank = 0; rank < ranks.Length; rank++)
        {
            var file = 0;
            foreach (var token in ranks[rank])
            {
                if (char.IsDigit(token)) { file += token - '0'; continue; }
                var piece = char.ToLowerInvariant(token);
                if (piece is 'p' or 'r' or 'q') return false;
                if (piece != 'k') minors.Add((piece, (rank + file) % 2));
                file++;
            }
        }
        return minors.Count == 0 || minors.Count == 1 && minors[0].Piece is 'b' or 'n' ||
            minors.All(m => m.Piece == 'b') && minors.Select(m => m.SquareColour).Distinct().Count() == 1;
    }

    private static string RepetitionIdentity(ChessBoard board)
    {
        var fields = board.ToFen().Split(' ');
        // An en-passant target changes repetition rights only when an en-passant capture is legal.
        if (fields[3] != "-" && !board.Moves().Any(m => m.IsEnPassant)) fields[3] = "-";
        return string.Join(' ', fields.Take(4));
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
