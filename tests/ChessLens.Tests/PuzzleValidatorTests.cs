using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Training;

namespace ChessLens.Tests;

public sealed class PuzzleValidatorTests
{
    [Fact]
    public async Task Equivalent_legal_mates_are_all_accepted_and_answers_end_at_checkmate()
    {
        var position = new EnginePosition("7k/5Q2/6K1/8/8/8/8/8 w - - 0 1", []);
        var result = await new PuzzleValidator(new MateFixtureEngine()).Validate(position, CancellationToken.None);
        Assert.True(result.Solution is not null, result.Rejection + " " + result.Diagnostic);
        Assert.Equal("mate", result.Solution.Objective);
        Assert.True(result.Solution.Root.Choices.Length >= 2);
        foreach (var choice in result.Solution.Root.Choices)
        {
            Assert.True(choice.Resolved); Assert.Null(choice.Next);
            Assert.True(ChessRules.Replay(position.InitialFen, [choice.Move]).IsEndGame);
        }
        Assert.Contains(result.Solution.Searches, s => s.RestrictedMoves is { Length: > 0 });
    }

    [Fact]
    public async Task An_unstable_alternative_set_does_not_publish_a_puzzle()
    {
        var result = await new PuzzleValidator(new MateFixtureEngine(unstable: true))
            .Validate(new("7k/5Q2/6K1/8/8/8/8/8 w - - 0 1", []), CancellationToken.None);
        Assert.Null(result.Solution); Assert.Contains("unstable", result.Rejection);
    }

    [Fact]
    [Trait("Category", "Stockfish")]
    public async Task Real_stockfish_validates_a_short_black_mating_objective()
    {
        var path = Environment.GetEnvironmentVariable("Stockfish__Path") ?? throw new InvalidOperationException("Stockfish__Path is required.");
        await using var engine = await UciEngine.Start(path, 1, 32, CancellationToken.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var result = await new PuzzleValidator(engine).Validate(new(ChessRules.InitialFen, ["f2f3", "e7e5", "g2g4"]), deadline.Token);
        Assert.True(result.Rejection is null, result.Rejection + " " + result.Diagnostic); Assert.NotNull(result.Solution);
        Assert.Equal("black", result.Solution.Side); Assert.Equal("mate", result.Solution.Objective);
        Assert.Contains(result.Solution.Root.Choices, c => c.Move == "d8h4" && c.Resolved);
        Assert.All(result.Solution.Searches, s => Assert.Contains("Stockfish 19", s.Result.Engine));
    }

    // Explicit engine double: legal mate-in-one moves are assigned mate scores;
    [Fact]
    [Trait("Category", "Stockfish")]
    public async Task Real_stockfish_checks_a_material_payoff_and_plays_a_legal_defence()
    {
        var path = Environment.GetEnvironmentVariable("Stockfish__Path") ?? throw new InvalidOperationException("Stockfish__Path is required.");
        await using var engine = await UciEngine.Start(path, 1, 32, CancellationToken.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var position = new EnginePosition("4k3/8/8/3q4/4P3/8/8/4K3 w - - 0 1", []);
        var result = await new PuzzleValidator(engine).Validate(position, deadline.Token);
        Assert.True(result.Rejection is null, result.Rejection + " " + result.Diagnostic); Assert.NotNull(result.Solution);
        Assert.Equal("material", result.Solution.Objective); Assert.True(result.Solution.MaterialTarget >= 100);
        var choice = Assert.Single(result.Solution.Root.Choices);
        Assert.Equal("e4d5", choice.Move); Assert.NotNull(choice.Reply); Assert.True(choice.Resolved);
        ChessRules.Replay(position.InitialFen, [choice.Move, choice.Reply]);
    }

    // Explicit engine double: legal mate-in-one moves are assigned mate scores;
    // all other moves get fixture centipawns, not claimed chess evaluations.
    internal sealed class MateFixtureEngine(bool unstable = false) : IChessEngine
    {
        public Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct)
        {
            var legal = searchMoves ?? ChessRules.View(ChessRules.Replay(position.InitialFen, position.History)).LegalMoves;
            var mates = legal.Where(m => ChessRules.Replay(position.InitialFen, position.History.Append(m)).EndGame?.EndgameType.ToString() == "Checkmate").Order().ToArray();
            var scores = legal.Select(m => (Move: m, Score: mates.Contains(m) && !(unstable && budget.Nodes > 250_000 && m == mates.Last()) ? new EngineScore("mate", 1) : new("cp", 0)))
                .OrderByDescending(m => m.Score.Kind == "mate").ThenBy(m => m.Move).Take(budget.MultiPv).ToArray();
            return Task.FromResult(new SearchResult("Labelled puzzle engine double", "{}", scores[0].Move,
                scores.Select((m, i) => new SearchLine(i + 1, 15, 10000, 1, m.Score, [m.Move], false)).ToArray(), 1, 1000, true));
        }
    }
}
