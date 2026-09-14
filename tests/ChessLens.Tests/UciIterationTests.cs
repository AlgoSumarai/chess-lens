using ChessLens.Core.Analysis;

namespace ChessLens.Tests;

public sealed class UciIterationTests
{
    [Fact]
    public void A_late_partial_update_at_the_same_depth_cannot_corrupt_a_complete_candidate_batch()
    {
        var batches = new UciIterations(3);
        batches.Add(Line(1, "e2e4", 100)); batches.Add(Line(2, "d2d4", 100)); batches.Add(Line(3, "g1f3", 100));
        // Real Stockfish can output a final line at an already completed depth.
        // Replacing rank 3 in that older batch would duplicate rank 2's move.
        batches.Add(Line(3, "d2d4", 200));
        var result = batches.CompleteFor("e2e4"); Assert.NotNull(result);
        Assert.Equal(new[] { "e2e4", "d2d4", "g1f3" }, result.Select(l => l.Pv[0]));
        Assert.All(result, l => Assert.Equal(100, l.Nodes));
        batches.Add(Line(1, "d2d4", 300)); batches.Add(Line(2, "e2e4", 300));
        Assert.Null(batches.CompleteFor("d2d4"));
        batches.Add(Line(3, "g1f3", 300));
        Assert.Equal(300, batches.CompleteFor("d2d4")![0].Nodes);
    }
    [Fact]
    public void Bounds_mixed_depths_and_duplicate_candidate_moves_are_not_complete_evidence()
    {
        var batches = new UciIterations(2);
        batches.Add(Line(1, "e2e4", 100)); batches.Add(Line(2, "d2d4", 100) with { Bound = true });
        Assert.Null(batches.CompleteFor("e2e4"));
        batches.Add(Line(1, "e2e4", 200)); batches.Add(Line(2, "d2d4", 200) with { Depth = 9 });
        Assert.Null(batches.CompleteFor("e2e4"));
        batches.Add(Line(1, "e2e4", 300)); batches.Add(Line(2, "e2e4", 300));
        Assert.Null(batches.CompleteFor("e2e4"));
    }
    private static SearchLine Line(int rank, string move, long nodes) => new(rank, 10, nodes, 1, new("cp", 0), [move], false);
}
