using ChessLens.Core.Analysis;
using System.Diagnostics;
using ChessLens.Core.Games;
using Microsoft.Extensions.Options;

namespace ChessLens.Tests;

public sealed class EngineSemanticsTests
{
    [Fact]
    public void White_display_scores_do_not_change_moving_player_loss()
    {
        var best = new EngineScore("cp", 120); var played = new EngineScore("cp", -130);
        Assert.Equal(-120, best.ForWhite("black").Value);
        Assert.Equal(250, MoveClassifier.Compare(best, played, false).CentipawnLoss);
        Assert.Equal("blunder", MoveClassifier.Compare(best, played, false).Label);
    }
    [Fact]
    public void Mate_transitions_remain_explicit()
    {
        var loss = MoveClassifier.Compare(new("cp", 0), new("mate", -3), false);
        Assert.Null(loss.CentipawnLoss); Assert.Equal("allowed-forced-mate", loss.MateTransition);
        Assert.Equal("missed-forced-mate", MoveClassifier.Compare(new("mate", 2), new("cp", 600), false).MateTransition);
    }
    [Fact]
    public void Uci_bound_scores_are_not_treated_as_exact()
    {
        var line = UciEngine.ParseInfo("info depth 12 multipv 1 score cp 102 lowerbound nodes 53000 time 250 pv e2e4 e7e5");
        Assert.NotNull(line); Assert.True(line.Bound); Assert.Equal(53000, line.Nodes); Assert.Equal(new[] { "e2e4", "e7e5" }, line.Pv);
    }
    [Fact]
    public void History_and_budget_are_part_of_cache_identity()
    {
        var a = new EnginePosition(ChessRules.InitialFen, []);
        var b = new EnginePosition(ChessRules.InitialFen, ["g1f3", "g8f6", "f3g1", "f6g8"]);
        Assert.NotEqual(a.CacheIdentity("SF19", "settings", SearchBudget.Quick), b.CacheIdentity("SF19", "settings", SearchBudget.Quick));
        Assert.NotEqual(a.CacheIdentity("SF19", "settings", SearchBudget.Quick), a.CacheIdentity("SF19", "settings", SearchBudget.Deep));
    }

    [Fact]
    public void Custom_classification_thresholds_are_validated_and_applied()
    {
        var options = new AnalysisOptions { InaccuracyCentipawns = 80, MistakeCentipawns = 160, BlunderCentipawns = 320 };
        Assert.Equal("inaccuracy", options.Compare(new("cp", 0), new("cp", -120), false).Label);
        options.MistakeCentipawns = 50;
        Assert.Throws<ArgumentException>(options.Validate);
    }
}

[Trait("Category", "Stockfish")]
public sealed class RealEngineTests
{
    private static string Executable => Environment.GetEnvironmentVariable("Stockfish__Path")
        ?? throw new InvalidOperationException("Real engine tests require Stockfish__Path. Use scripts/install-stockfish.ps1 and set that variable, or exclude Category=Stockfish.");

    [Theory]
    [InlineData("7k/5Q2/6K1/8/8/8/8/8 w - - 0 1", "white")]
    [InlineData("8/8/8/8/8/6k1/5q2/7K b - - 0 1", "black")]
    public async Task Real_engine_finds_mate_for_both_colours(string fen, string side)
    {
        await using var engine = await UciEngine.Start(Executable, 1, 32, CancellationToken.None);
        var result = await engine.Search(new(fen, []), new("test", 10000, 1000), null, 20, CancellationToken.None);
        Assert.Contains("Stockfish 19", result.Engine);
        Assert.True(result.Complete);
        var score = result.Lines.Single(l => l.Rank == 1).Score;
        Assert.Equal("mate", score.Kind); Assert.Equal(1, score.Value);
        Assert.Equal(side == "white" ? 1 : -1, score.ForWhite(side).Value);
        var board = ChessRules.Replay(fen, [result.BestMove]);
        Assert.True(board.IsEndGame);
    }

    [Fact]
    public async Task Restricted_continuation_and_history_are_accepted_by_real_protocol()
    {
        await using var engine = await UciEngine.Start(Executable, 1, 32, CancellationToken.None);
        var history = new[] { "g1f3", "g8f6", "f3g1", "f6g8" };
        var result = await engine.Search(new(ChessRules.InitialFen, history), SearchBudget.Quick, ["e2e4"], 20, CancellationToken.None);
        Assert.Equal("e2e4", result.BestMove);
        Assert.Equal("e2e4", result.Lines[0].Pv[0]);
    }

    [Fact]
    public async Task Cancellation_kills_the_process_and_marks_it_unusable()
    {
        await using var engine = await UciEngine.Start(Executable, 1, 32, CancellationToken.None);
        await engine.Search(new(ChessRules.InitialFen, []), SearchBudget.Quick, null, 20, CancellationToken.None);
        using var process = Process.GetProcessById(engine.ProcessId);
        var baseline = process.TotalProcessorTime;
        using var cancellation = new CancellationTokenSource();
        var search = engine.Search(new(ChessRules.InitialFen, []), new("cancel-test", 5_000_000, 15_000), null, 20, cancellation.Token);
        var deadline = Stopwatch.StartNew();
        while (!search.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            process.Refresh();
            if (process.TotalProcessorTime - baseline > TimeSpan.FromMilliseconds(100)) break;
            await Task.Delay(20);
        }
        Assert.False(search.IsCompleted); // Cancellation targets actual engine work, not preflight validation.
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search);
        Assert.False(engine.IsHealthy);
    }

    [Fact]
    public async Task Repetition_history_changes_the_same_board_from_losing_to_drawn()
    {
        // Labelled synthetic fixture: Black is a queen down, but ...Nb8 repeats
        // the original position for the third time. A FEN cannot encode this claim.
        const string start = "1n2k3/8/8/8/8/8/8/1N1QK3 w - - 0 1";
        string[] history = ["b1c3", "b8c6", "c3b1", "c6b8", "b1c3", "b8c6", "c3b1"];
        var currentFen = ChessRules.Replay(start, history).ToFen();
        await using var engine = await UciEngine.Start(Executable, 1, 32, CancellationToken.None);
        var repeated = await engine.Search(new(start, history), SearchBudget.Deep, ["c6b8"], 20, CancellationToken.None);
        var fresh = await engine.Search(new(currentFen, []), SearchBudget.Deep, ["c6b8"], 20, CancellationToken.None);
        var drawScore = repeated.Lines.Single(l => l.Rank == 1).Score;
        var losingScore = fresh.Lines.Single(l => l.Rank == 1).Score;
        Assert.Equal("cp", drawScore.Kind);
        Assert.InRange(drawScore.Value, -2, 2);
        Assert.True(losingScore.Kind == "cp" && losingScore.Value < -300 || losingScore.Kind == "mate" && losingScore.Value < 0,
            $"Without repetition the queen deficit must remain losing, got {losingScore}.");
    }

    [Fact]
    public async Task Fifty_move_counter_is_preserved_in_custom_start_positions()
    {
        const string board = "4k3/8/2n5/8/8/8/8/1N1QK3 b - - ";
        await using var engine = await UciEngine.Start(Executable, 1, 32, CancellationToken.None);
        var claim = await engine.Search(new(board + "99 50", []), SearchBudget.Deep, ["c6b8"], 20, CancellationToken.None);
        var fresh = await engine.Search(new(board + "0 1", []), SearchBudget.Deep, ["c6b8"], 20, CancellationToken.None);
        Assert.Equal("cp", claim.Lines[0].Score.Kind);
        Assert.InRange(claim.Lines[0].Score.Value, -2, 2);
        Assert.True(fresh.Lines[0].Score.Kind == "cp" && fresh.Lines[0].Score.Value < -300 ||
            fresh.Lines[0].Score.Kind == "mate" && fresh.Lines[0].Score.Value < 0);
    }

    [Fact]
    public async Task Cache_reuses_only_compatible_searches_and_keeps_evidence_isolated()
    {
        await using var engine = await UciEngine.Start(Executable, 1, 32, CancellationToken.None);
        var position = new EnginePosition(ChessRules.InitialFen, []);
        var first = await engine.Search(position, SearchBudget.Quick, null, 20, CancellationToken.None);
        Assert.False(first.CacheHit);
        var cached = await engine.Search(position, SearchBudget.Quick, null, 20, CancellationToken.None);
        Assert.True(cached.CacheHit);
        Assert.Equal(first.BestMove, cached.BestMove);
        cached.Lines[0].Pv[0] = "test mutation";
        Assert.Equal(first.BestMove, (await engine.Search(position, SearchBudget.Quick, null, 20, CancellationToken.None)).Lines[0].Pv[0]);
        Assert.False((await engine.Search(position, SearchBudget.Quick, [first.BestMove], 20, CancellationToken.None)).CacheHit);
        Assert.False((await engine.Search(position, SearchBudget.Quick, null, 10, CancellationToken.None)).CacheHit);
        Assert.False((await engine.Search(position, SearchBudget.Deep, null, 20, CancellationToken.None)).CacheHit);
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.Search(position, SearchBudget.Quick, null, 20, stopped.Token));
    }

    [Fact]
    public async Task Pool_replaces_a_cancelled_process_and_releases_its_slot()
    {
        await using var pool = new EnginePool(Options.Create(new EngineOptions { Path = Executable, Processes = 1, Threads = 1, HashMb = 32 }));
        var position = new EnginePosition(ChessRules.InitialFen, []);
        await pool.Search(position, SearchBudget.Quick, null, 20, CancellationToken.None);
        using var cancel = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pool.Search(position, new("interrupted", 5_000_000, 15_000), null, 20, cancel.Token));
        var replacement = await pool.Search(position, SearchBudget.Quick, null, 20, CancellationToken.None);
        Assert.True(replacement.Complete);
        Assert.False(replacement.CacheHit);
    }
}
