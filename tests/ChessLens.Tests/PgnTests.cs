using ChessLens.Core.Games;
using ChessLens.Core.Imports;

namespace ChessLens.Tests;

public sealed class PgnTests
{
    private readonly PgnReader reader = new();
    public const string Game = "[White \"Learner\"]\n[Black \"Opponent\"]\n[Result \"0-1\"]\n\n1. f3 e5 2. g4 Qh4# 0-1";

    [Fact]
    public void Mainline_preserves_clocks_and_skips_nested_variations()
    {
        var game = reader.Parse("[White \"A\"]\n[Black \"B\"]\n1.e4 {[%clk 0:09:58.5]} (1.d4 d5 (1... Nf6)) e5 2.Nf3 Nc6 1-0");
        Assert.Equal(new[] { "e2e4", "e7e5", "g1f3", "b8c6" }, game.Moves.Select(m => m.Uci));
        Assert.Equal(598.5, game.Moves[0].ClockSeconds);
        Assert.Null(game.Moves[1].ClockSeconds);
        Assert.Null(game.Opening);
        Assert.Null(game.WhiteRating);
        Assert.Contains("(1.d4", game.RawPgn);
    }

    [Fact]
    public void Identity_must_match_exactly_one_player()
    {
        var game = reader.Parse(Game);
        Assert.Null(PgnReader.Identify(game, null));
        Assert.Null(PgnReader.Identify(game, "Other"));
        Assert.Equal("white", PgnReader.Identify(game, "learner"));
        game.Black = game.White;
        Assert.Null(PgnReader.Identify(game, "Learner"));
    }

    [Fact]
    public void Duplicate_identity_ignores_annotations_but_preserves_distinct_metadata()
    {
        var a = reader.Parse(Game);
        var b = reader.Parse(Game.Replace("f3", "f3 {comment}"));
        var c = reader.Parse("[Date \"2026.01.01\"]\n" + Game);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.NotEqual(a.Fingerprint, c.Fingerprint);
    }

    [Theory]
    [InlineData("[Variant \"Chess960\"]\n1.e4 e5 1-0", "Unsupported variant")]
    [InlineData("1.e5 e4 1-0", "Illegal or malformed")]
    [InlineData("1.e4 e5 *", "completed")]
    [InlineData("[Result \"1-0\"]\n1.e4 e5 0-1", "disagree")]
    [InlineData("[FEN \"invalid\"]\n1.e4 e5 0-1", "Invalid starting FEN")]
    public void Bad_games_have_explanations(string pgn, string message) => Assert.Contains(message, Assert.Throws<ArgumentException>(() => reader.Parse(pgn)).Message);

    [Fact]
    public void Mixed_batch_keeps_each_game_for_individual_validation()
    {
        var batch = reader.Split(Game + "\n\n[White \"Bad\"]\n1.e5 e4 0-1\n\n" + Game, 50);
        Assert.Equal(3, batch.Count);
        Assert.Equal(4, reader.Parse(batch[0]).Moves.Count);
        Assert.Throws<ArgumentException>(() => reader.Parse(batch[1]));
        Assert.Equal(4, reader.Parse(batch[2]).Moves.Count);
    }

    [Theory]
    [InlineData("[FEN \"r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1\"]\n1.O-O O-O-O 1-0", "e1g1", "e8c8")]
    [InlineData("1.e4 a6 2.e5 d5 3.exd6 1-0", "e5d6", "e5d6")]
    [InlineData("[FEN \"7k/P7/8/8/8/8/7p/4K3 w - - 0 1\"]\n1.a8=N h1=Q+ 0-1", "a7a8n", "h2h1q")]
    public void Special_moves_round_trip_with_server_rules(string pgn, string expected, string last)
    {
        var game = reader.Parse(pgn);
        Assert.Contains(game.Moves, m => m.Uci == expected);
        Assert.Equal(last, game.Moves[^1].Uci);
        var board = ChessRules.Replay(game.InitialFen, game.Moves.Select(m => m.Uci));
        Assert.Equal(game.Moves[^1].FenAfter, board.ToFen());
    }

    [Fact]
    public void Illegal_variation_is_rejected_without_changing_source()
    {
        var game = reader.Parse(Game);
        Assert.Throws<ArgumentException>(() => ChessRules.Replay(game.InitialFen, ["e2e5"]));
        Assert.Equal("f2f3", game.Moves[0].Uci);
    }

    [Fact]
    public void Unclosed_comment_does_not_discard_later_header_delimited_game()
    {
        var batch = reader.Split("[White \"Broken\"]\n1.e4 {unclosed\n\n" + Game, 50);
        Assert.Equal(2, batch.Count);
        Assert.Throws<ArgumentException>(() => reader.Parse(batch[0]));
        Assert.Equal(4, reader.Parse(batch[1]).Moves.Count);
    }

    [Fact]
    public void Comment_after_final_result_does_not_become_an_extra_game()
    {
        var batch = reader.Split(Game + " {A final annotation}", 50);
        Assert.Single(batch);
        Assert.Contains("{A final annotation}", reader.Parse(batch[0]).RawPgn);
    }
}
