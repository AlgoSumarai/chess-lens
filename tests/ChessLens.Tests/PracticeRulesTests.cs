using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Training;

namespace ChessLens.Tests;

public sealed class PracticeRulesTests
{
    [Theory]
    [InlineData("preserve-advantage", 600, 500, true)]
    [InlineData("preserve-advantage", 600, 499, false)]
    [InlineData("preserve-advantage", 150, 99, false)]
    [InlineData("decision-safety", -600, -650, true)]
    [InlineData("decision-safety", -600, -701, false)]
    public void Objectives_use_inclusive_boundaries_and_do_not_require_winning_a_whole_game(string kind, int best, int played, bool accepted) =>
        Assert.Equal(accepted, PracticeRules.Accepts(new(kind, "Fixture", "Labelled objective"), new("cp", best), new("cp", played)));

    [Fact]
    public void Mate_objectives_stay_separate_from_centipawn_losses_and_already_lost_positions_are_labelled()
    {
        var mate = PracticeRules.Objective(new("mate", 2)); var safety = PracticeRules.Objective(new("mate", -3));
        Assert.Equal("retain-mate", mate.Kind); Assert.Equal("decision-safety", safety.Kind);
        Assert.True(PracticeRules.Accepts(mate, new("mate", 2), new("mate", 3)));
        Assert.False(PracticeRules.Accepts(mate, new("mate", 2), new("cp", 2000)));
        Assert.False(PracticeRules.Accepts(safety, new("cp", 0), new("mate", -1)));
        Assert.True(PracticeRules.Accepts(safety, new("mate", -2), new("mate", -1)));
        Assert.True(PracticeRules.RelativeLossAgrees(new("cp", 300), new("cp", 250), new("cp", 400), new("cp", 275)));
        Assert.False(PracticeRules.RelativeLossAgrees(new("cp", 300), new("cp", 250), new("cp", 400), new("cp", 274)));
        Assert.False(PracticeRules.RelativeLossAgrees(new("mate", 1), new("mate", 1), new("cp", 10000), new("cp", 10000)));
    }

    [Fact]
    public async Task Practical_opponent_and_full_strength_grading_are_separate_and_keep_the_complete_source_history()
    {
        string[] prior = ["e2e4", "e7e5"];
        var engine = new FixtureEngine(); var rules = new PracticeRules(engine);
        var turn = await rules.Turn(new(ChessRules.InitialFen, prior), ["g1f3"], PracticeRules.Objective(new("cp", 0)), 1, CancellationToken.None);
        Assert.True(turn.Grade.Accepted); Assert.False(turn.Finished); Assert.Equal(2, turn.History.Length);
        Assert.Equal(4, engine.Calls.Count(c => c.Skill == 20)); Assert.Single(engine.Calls, c => c.Skill == 6);
        Assert.All(engine.Calls.Where(c => c.Skill == 20), c => Assert.Equal(prior, c.Position.History));
        Assert.Equal(prior.Concat(["g1f3"]), engine.Calls.Single(c => c.Skill == 6).Position.History);
        Assert.Equal(750_000, turn.Grade.Played.Final.Budget.Nodes);
        Assert.Contains(turn.History[^1], ChessRules.View(ChessRules.Replay(ChessRules.InitialFen, prior.Concat(["g1f3"]))).LegalMoves);
    }

    [Fact]
    public async Task Growing_winning_estimates_and_a_stronger_restricted_alternative_are_accepted_when_relative_loss_and_objective_agree()
    {
        var turn = await new PracticeRules(new FixtureEngine(mode: "growing-stronger", score: 600)).Turn(new(ChessRules.InitialFen, []), ["e2e4"], PracticeRules.Objective(new("cp", 600)), 1, CancellationToken.None);
        Assert.True(turn.Grade.Accepted); Assert.Equal(600, PracticeRules.Score(turn.Grade.Best.First).Value); Assert.Equal(800, PracticeRules.Score(turn.Grade.Best.Final).Value);
        Assert.Equal(700, PracticeRules.Score(turn.Grade.Played.First).Value); Assert.Equal(900, PracticeRules.Score(turn.Grade.Played.Final).Value);
    }

    [Fact]
    public async Task Full_source_history_detects_repetition_after_a_legal_player_move()
    {
        string[] prior = ["g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1"];
        var rules = new PracticeRules(new FixtureEngine(preferred: "f6g8"));
        var turn = await rules.Turn(new(ChessRules.InitialFen, prior), ["f6g8"], PracticeRules.Objective(new("cp", 0)), 1, CancellationToken.None);
        Assert.True(turn.Finished); Assert.Equal("succeeded", turn.Outcome); Assert.NotNull(turn.Terminal); Assert.Null(turn.Opponent);
        Assert.False(ChessRules.Replay(ChessRules.Replay(ChessRules.InitialFen, prior).ToFen(), ["f6g8"]).IsEndGame);
    }

    [Fact]
    public void Recorded_source_games_can_continue_after_an_unclaimed_draw_and_pawn_moves_reset_the_counter()
    {
        string[] source = ["g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8", "e2e4", "e7e5"];
        Assert.Equal("Repetition", ChessRules.PracticeView(ChessRules.InitialFen, source.Take(8), []).Endgame);
        Assert.Null(ChessRules.PracticeView(ChessRules.InitialFen, source, []).Endgame);
        Assert.Null(ChessRules.PracticeView("7k/7p/8/8/8/8/P7/R6K w - - 100 1", [], ["a2a3"]).Endgame);
        Assert.Equal("InsufficientMaterial", ChessRules.PracticeView("7k/8/8/8/8/8/8/2B4K w - - 0 1", [], []).Endgame);
    }

    [Fact]
    public void Two_knights_are_not_dead_material_and_a_legal_cooperative_mate_has_priority_over_draw_rules()
    {
        Assert.Null(ChessRules.PracticeView("7k/8/8/8/8/8/8/2NN3K w - - 0 1", [], []).Endgame);
        Assert.Equal("Checkmate", ChessRules.PracticeView("5K1k/5N2/5N2/8/8/8/8/8 b - - 100 1", [], []).Endgame);
        Assert.Null(ChessRules.PracticeView("7k/8/8/8/8/8/8/2BN3K w - - 0 1", [], []).Endgame);
        Assert.Equal("InsufficientMaterial", ChessRules.PracticeView("7k/8/8/8/8/8/8/2B1B2K w - - 0 1", [], []).Endgame);
        Assert.Null(ChessRules.PracticeView("7k/8/8/8/8/8/8/2BB3K w - - 0 1", [], []).Endgame);
    }

    [Theory]
    [InlineData("8/8/8/8/8/8/3k4/R6K w - - 99 1", "a1a8")]
    [InlineData("7k/8/8/8/8/8/3n4/2B4K w - - 0 1", "c1d2")]
    public async Task Fifty_move_and_insufficient_material_draws_finish_without_an_illegal_opponent_search(string fen, string move)
    {
        var engine = new FixtureEngine(preferred: move);
        var turn = await new PracticeRules(engine).Turn(new(fen, []), [move], PracticeRules.Objective(new("cp", 0)), 1, CancellationToken.None);
        Assert.True(turn.Finished); Assert.Equal("succeeded", turn.Outcome); Assert.NotNull(turn.Terminal); Assert.Equal(4, engine.Calls.Count);
    }

    [Fact]
    public async Task Promotions_are_full_uci_moves_and_illegal_or_wrong_colour_moves_never_reach_the_engine()
    {
        const string fen = "7k/P7/6K1/8/8/8/8/8 w - - 0 1";
        var engine = new FixtureEngine(preferred: "a7a8r"); var rules = new PracticeRules(engine);
        var turn = await rules.Turn(new(fen, []), ["a7a8r"], PracticeRules.Objective(new("cp", 0)), 1, CancellationToken.None);
        Assert.Equal("a7a8r", turn.Grade.Move); Assert.DoesNotContain("P", ChessRules.Replay(fen, turn.History).ToFen().Split(' ')[0]);
        await Assert.ThrowsAsync<ArgumentException>(() => rules.Turn(new(fen, []), ["a7a8"], PracticeRules.Objective(new("cp", 0)), 1, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => rules.Turn(new(fen, []), ["h8h7"], PracticeRules.Objective(new("cp", 0)), 1, CancellationToken.None));
    }

    [Fact]
    public async Task Unstable_bounded_or_illegal_engine_evidence_is_ungraded_rather_than_a_wrong_answer()
    {
        foreach (var mode in new[] { "unstable", "bound", "illegal", "incomplete", "configuration" })
            await Assert.ThrowsAnyAsync<Exception>(() => new PracticeRules(new FixtureEngine(mode: mode)).Turn(new(ChessRules.InitialFen, []), ["e2e4"], PracticeRules.Objective(new("cp", 0)), 1, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Stockfish")]
    public async Task Real_stockfish_accepts_distinct_mating_alternatives_and_rejects_a_stalemate_without_converting_mate_to_cp()
    {
        var path = Environment.GetEnvironmentVariable("Stockfish__Path") ?? throw new InvalidOperationException("Stockfish__Path is required.");
        await using var engine = await UciEngine.Start(path, 1, 32, CancellationToken.None);
        var start = new EnginePosition("7k/5Q2/6K1/8/8/8/8/8 w - - 0 1", []); var rules = new PracticeRules(engine);
        foreach (var move in new[] { "f7g7", "f7h7" })
        {
            var turn = await rules.Turn(start, [move], PracticeRules.Objective(new("mate", 1)), 1, CancellationToken.None);
            Assert.True(turn.Grade.Accepted); Assert.Equal("succeeded", turn.Outcome); Assert.Equal("Checkmate", turn.Terminal);
            Assert.All(new[] { turn.Grade.Played.First, turn.Grade.Played.Final }, s => Assert.Equal("mate", PracticeRules.Score(s).Kind));
        }
        var failed = await rules.Turn(start, ["f7e6"], PracticeRules.Objective(new("mate", 1)), 1, CancellationToken.None);
        Assert.False(failed.Grade.Accepted); Assert.Equal("needs-practice", failed.Outcome); Assert.Equal("cp", PracticeRules.Score(failed.Grade.Played.Final).Kind);
    }

    [Fact]
    [Trait("Category", "Stockfish")]
    public async Task Real_practical_opponent_returns_a_legal_reply_after_independent_full_strength_grading()
    {
        var path = Environment.GetEnvironmentVariable("Stockfish__Path") ?? throw new InvalidOperationException("Stockfish__Path is required.");
        await using var engine = await UciEngine.Start(path, 1, 32, CancellationToken.None);
        var turn = await new PracticeRules(engine).Turn(new(ChessRules.InitialFen, []), ["e2e4"], PracticeRules.Objective(new("cp", 0)), 1, CancellationToken.None);
        Assert.True(turn.Grade.Accepted); Assert.NotNull(turn.Opponent); Assert.Equal(2, turn.History.Length);
        Assert.Contains(turn.History[^1], ChessRules.View(ChessRules.Replay(ChessRules.InitialFen, ["e2e4"])).LegalMoves);
        Assert.Contains("\"skill\":6", turn.Opponent.Result.SettingsJson);
        Assert.Contains("\"skill\":20", turn.Grade.Played.Final.Result.SettingsJson);
    }

    [Fact]
    [Trait("Category", "Stockfish")]
    public async Task Observed_practical_branch_is_graded_by_stable_relative_loss_and_a_checked_final_objective()
    {
        var path = Environment.GetEnvironmentVariable("Stockfish__Path") ?? throw new InvalidOperationException("Stockfish__Path is required.");
        await using var engine = await UciEngine.Start(path, 1, 64, CancellationToken.None);
        var start = new EnginePosition("4k3/pp6/5n2/8/8/8/PPPP4/4K2R w - - 0 1", ["e1f1", "f6g4"]);
        string[] history = ["f1e2", "a7a5", "h1h4", "g4e5", "h4e4"];
        PracticeTurn? turn = null;
        for (var execution = 1; execution <= 3 && turn is null; execution++)
        {
            try { turn = await new PracticeRules(engine, execution).Turn(start, history, PracticeRules.Objective(new("cp", 600)), 3, CancellationToken.None); }
            catch (UnstablePracticeEvidence) when (execution < 3) { }
        }
        Assert.NotNull(turn); Assert.True(turn.Grade.Accepted); Assert.True(turn.Finished); Assert.Equal("succeeded", turn.Outcome);
        Assert.NotNull(turn.FinalCheck); Assert.True(PracticeRules.Retained(PracticeRules.Objective(new("cp", 600)), PracticeRules.Score(turn.FinalCheck.Final)));
    }

    internal sealed class FixtureEngine(string? preferred = null, string mode = "valid", int score = 0, Func<Task>? callback = null) : IChessEngine
    {
        public List<(EnginePosition Position, SearchBudget Budget, string[]? Restricted, int Skill)> Calls { get; } = [];
        public async Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct)
        {
            Calls.Add((position, budget, searchMoves, skill)); if (Calls.Count == 1 && callback is not null) await callback();
            var legal = ChessRules.View(ChessRules.Replay(position.InitialFen, position.History)).LegalMoves;
            var move = searchMoves?.FirstOrDefault() ?? (preferred is not null && legal.Contains(preferred) ? preferred : legal[0]);
            var value = mode == "unstable" && budget.Nodes == 750_000 && searchMoves is not null ? score - 76 : score;
            if (mode == "growing-stronger") value += (budget.Nodes == 750_000 ? 200 : 0) + (searchMoves is not null ? 100 : 0);
            return new("Labelled practice engine double", mode == "configuration" ? $"{{\"call\":{Calls.Count}}}" : $"{{\"skill\":{skill}}}", move,
                [new(1, 15, budget.Nodes, 1, new("cp", value), [mode == "illegal" ? "a1a8" : move], mode == "bound")], 1, 1000, mode != "incomplete");
        }
    }
}
