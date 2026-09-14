using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chess;
using ChessLens.Core.Games;

namespace ChessLens.Core.Imports;

public sealed partial class PgnReader
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxGames = 200;
    public const int MaxPlies = 1000;

    // Tokenisation preserves source text and comments; Gera.Chess resolves and validates SAN.
    public IReadOnlyList<string> Split(string pgn, int maximum)
    {
        if (maximum is < 1 or > MaxGames) throw new ArgumentException($"Choose between 1 and {MaxGames} games.");
        if (Encoding.UTF8.GetByteCount(pgn) > MaxBytes) throw new ArgumentException("PGN uploads are limited to 5 MB.");
        var games = new List<string>();
        var start = 0;
        var hasMoves = false;
        var ended = false;
        var lastEnd = 0;
        try
        {
            foreach (var token in Tokens(pgn))
            {
                lastEnd = token.End;
                if (token.Kind == 'h' && hasMoves || token.Kind == 'm' && ended)
                {
                    Add(token.Start);
                    start = token.Start;
                    hasMoves = false;
                    ended = false;
                }
                if (token.Kind == 'm') hasMoves = true;
                if (token.Kind == 'm' && IsResult(token.Text))
                {
                    ended = true;
                }
                if (games.Count == maximum) return games;
            }
        }
        catch (ArgumentException)
        {
            // A broken lexical structure must not discard later header-delimited games.
            // Resynchronise only after the last successfully consumed token.
            var next = HeaderBoundaryRegex().Match(pgn, Math.Max(start + 1, lastEnd));
            if (next.Success)
            {
                Add(next.Index);
                if (games.Count < maximum) games.AddRange(Split(pgn[next.Index..], maximum - games.Count));
                return games;
            }
        }
        if (hasMoves || games.Count == 0 || pgn[start..].Contains('[')) Add(pgn.Length);
        return games;

        void Add(int end)
        {
            var text = pgn[start..end].Trim();
            if (text.Length == 0) return;
            if (games.Count < maximum) games.Add(text);
        }
    }

    public SourceGame Parse(string raw)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var moveTokens = new List<Token>();
        foreach (var token in Tokens(raw))
        {
            if (token.Kind == 'h')
            {
                var match = HeaderRegex().Match(token.Text);
                if (!match.Success) throw new ArgumentException("Malformed PGN tag.");
                if (!headers.TryAdd(match.Groups[1].Value, Unescape(match.Groups[2].Value)))
                    throw new ArgumentException("Duplicate PGN tag.");
                if (match.Groups[2].Length > 2048) throw new ArgumentException("A PGN metadata value exceeds 2,048 characters.");
            }
            else moveTokens.Add(token);
        }
        string? Tag(string name) => headers.GetValueOrDefault(name);
        if (Tag("Variant") is { } variant && !new[] { "Standard", "Chess", "From Position" }.Contains(variant, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported variant: {variant}. Only standard chess is supported.");
        if (Tag("SetUp") == "1" && Tag("FEN") is null) throw new ArgumentException("SetUp requires a starting FEN.");
        var fen = Tag("FEN") ?? ChessRules.InitialFen;
        ChessBoard board;
        try { board = ChessBoard.LoadFromFen(fen); }
        catch (Exception e) when (e is not OutOfMemoryException) { throw new ArgumentException("Invalid starting FEN."); }
        var moves = new List<GameMove>();
        string? result = null;
        foreach (var token in moveTokens)
        {
            if (token.Kind == 'c')
            {
                var clock = ClockRegex().Match(token.Text);
                if (moves.Count > 0 && clock.Success)
                {
                    var minutes = int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture);
                    var seconds = double.Parse(clock.Groups[3].Value, CultureInfo.InvariantCulture);
                    if (minutes < 60 && seconds < 60)
                        moves[^1].ClockSeconds = int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 + minutes * 60 + seconds;
                }
                continue;
            }
            var san = MoveNumberRegex().Replace(token.Text, "").TrimEnd('!', '?');
            if (san.Length == 0 || san[0] == '$' || san is "..." or "e.p.") continue;
            if (san.Length > 32) throw new ArgumentException("A move token exceeds 32 characters.");
            if (IsResult(san)) { result = san; continue; }
            if (result is not null) throw new ArgumentException("Moves found after the game result.");
            if (moves.Count >= MaxPlies) throw new ArgumentException($"A game may have at most {MaxPlies} half-moves.");
            var before = board.ToFen();
            var side = board.Turn == PieceColor.White ? "white" : "black";
            try
            {
                if (!board.Move(san.Replace("0-0-0", "O-O-O").Replace("0-0", "O-O"))) throw new ArgumentException();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                throw new ArgumentException($"Illegal or malformed move at half-move {moves.Count + 1}: {san}.");
            }
            var executed = board.ExecutedMoves[^1];
            moves.Add(new GameMove { Ply = moves.Count + 1, San = executed.San ?? san, Uci = ChessRules.Uci(executed),
                FenBefore = before, FenAfter = board.ToFen(), Side = side });
        }
        result ??= Tag("Result");
        if (result is null or "*" || !IsResult(result)) throw new ArgumentException("Only completed games with a result are imported.");
        if (Tag("Result") is { } declared && declared != result) throw new ArgumentException("The PGN header and movetext results disagree.");
        if (moves.Count == 0) throw new ArgumentException("This game has no legal moves to review.");
        var identityHeaders = headers.Where(h => !new[] { "Annotator", "PlyCount" }.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
            .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase).Select(h => new[] { h.Key.ToLowerInvariant(), h.Value });
        var identity = JsonSerializer.Serialize(new { version = 1, headers = identityHeaders, fen, result, moves = moves.Select(m => m.Uci) });
        var game = new SourceGame
        {
            Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))),
            White = Tag("White") ?? "Unknown", Black = Tag("Black") ?? "Unknown", Result = result,
            WhiteRating = Rating(Tag("WhiteElo")), BlackRating = Rating(Tag("BlackElo")),
            TimeControl = Missing(Tag("TimeControl")), Opening = Missing(Tag("Opening")), Eco = Missing(Tag("ECO")),
            Termination = Missing(Tag("Termination")), RawPgn = raw, HeadersJson = JsonSerializer.Serialize(headers),
            InitialFen = fen, PlayedAt = ParseDate(Tag("UTCDate") ?? Tag("Date"), Tag("UTCTime")), Moves = moves
        };
        foreach (var move in moves) move.GameId = game.Id;
        return game;
    }

    public static string? Identify(SourceGame game, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var white = string.Equals(game.White, name.Trim(), StringComparison.OrdinalIgnoreCase);
        var black = string.Equals(game.Black, name.Trim(), StringComparison.OrdinalIgnoreCase);
        return white == black ? null : white ? "white" : "black";
    }

    private static bool IsResult(string s) => s is "1-0" or "0-1" or "1/2-1/2" or "*";
    private static string? Missing(string? s) => s is null or "?" or "-" ? null : s;
    private static int? Rating(string? s) => int.TryParse(s, out var n) && n is > 0 and < 5000 ? n : null;
    private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");
    private static DateTimeOffset? ParseDate(string? date, string? time) =>
        DateTimeOffset.TryParseExact($"{date} {time ?? "00:00:00"}", "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private sealed record Token(char Kind, string Text, int Start, int End);
    private static IEnumerable<Token> Tokens(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i]) || text[i] == '\uFEFF') { i++; continue; }
            var start = i;
            var ch = text[i++];
            if (ch == ';' || ch == '%' && (start == 0 || text[start - 1] == '\n'))
            {
                while (i < text.Length && text[i] != '\n') i++;
                if (depth == 0) yield return new('c', text[start..i], start, i);
            }
            else if (ch == '{')
            {
                while (i < text.Length && text[i] != '}') i++;
                if (i == text.Length) throw new ArgumentException("Unclosed PGN comment.");
                i++;
                if (depth == 0) yield return new('c', text[start..i], start, i);
            }
            else if (ch == '(') { if (++depth > 32) throw new ArgumentException("PGN variations are nested too deeply."); }
            else if (ch == ')') { if (--depth < 0) throw new ArgumentException("Unmatched PGN variation bracket."); }
            else if (ch == '[')
            {
                var quoted = false;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && quoted) { i += 2; continue; }
                    if (text[i] == '"') quoted = !quoted;
                    if (text[i++] == ']' && !quoted) break;
                }
                if (i > text.Length || text[i - 1] != ']') throw new ArgumentException("Unclosed PGN tag.");
                if (depth == 0) yield return new('h', text[start..i], start, i);
            }
            else
            {
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && !"{}();[".Contains(text[i])) i++;
                if (depth == 0) yield return new('m', text[start..i], start, i);
            }
        }
        if (depth != 0) throw new ArgumentException("Unclosed PGN variation.");
    }

    [GeneratedRegex("^\\[\\s*([A-Za-z0-9_]+)\\s+\"((?:\\\\.|[^\"\\\\])*)\"\\s*\\]$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();
    [GeneratedRegex(@"^\d+\.{1,3}")]
    private static partial Regex MoveNumberRegex();
    [GeneratedRegex(@"\[%clk\s+(\d+):(\d{2}):(\d{2}(?:\.\d+)?)\]")]
    private static partial Regex ClockRegex();
    [GeneratedRegex(@"(?m)^\[(?:Event|White|Site)\s+""", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderBoundaryRegex();
}
