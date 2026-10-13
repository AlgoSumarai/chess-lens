using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Imports;
using ChessLens.Core.Insights;

namespace ChessLens.Tests;

public sealed class ObjectiveInsightTests
{
    internal static InsightGame Fixture(int lastPlayed = 0, bool provisionalMiddle = false, bool winningMate = false)
    {
        var game = new PgnReader().Parse($"""
            [Event "Labelled conversion/endgame engine-evidence fixture {Guid.NewGuid()}"]
            [Date "2026.10.01"]
            [FEN "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1"]
            1.Kf1 Kf7 2.Kg1 Kg6 3.Kh1 Kh6 0-1
            """);
        var run = new AnalysisRun { OwnerId = "labelled-objective-fixture", GameId = game.Id, State = "completed", Profile = "deep",
            EngineVersion = "Labelled objective engine double", SettingsJson = "{}", TotalPlies = game.Moves.Count, CompletedPlies = game.Moves.Count };
        run.Moves = game.Moves.Select(source =>
        {
            var isLast = source.Ply == 5;
            var bestMove = isLast ? "e2e4" : source.Uci;
            var bestScore = source.Side == "white" ? new EngineScore(winningMate ? "mate" : "cp", winningMate ? 5 : 400) : new("cp", 0);
            var playedScore = isLast ? new EngineScore("cp", lastPlayed) : bestScore;
            var classification = MoveClassifier.Compare(bestScore, playedScore, false);
            string Search(string root, EngineScore score) => JsonSerializer.Serialize(new SearchResult(run.EngineVersion, "{}", root,
                [new(1, 15, 250_000, 50, score, [root], false)], 50, 1000, true));
            return new MoveAnalysis { RunId = run.Id, Ply = source.Ply, FenBefore = source.FenBefore, PlayedMove = source.Uci, Side = source.Side,
                BestMove = bestMove, BestScoreKind = bestScore.Kind, BestScoreValue = bestScore.Value, PlayedScoreKind = playedScore.Kind,
                PlayedScoreValue = playedScore.Value, CentipawnLoss = classification.CentipawnLoss, MateTransition = classification.MateTransition,
                Classification = classification.Label, Provisional = provisionalMiddle && source.Ply == 3, Complete = true,
                BestSearchJson = Search(bestMove, bestScore), PlayedSearchJson = Search(source.Uci, playedScore), BudgetJson = "{}", CacheIdentity = "labelled-objective-fixture" };
        }).ToList();
        return new(game, "white", run);
    }

    [Fact]
    public void Repeated_advantage_losses_and_endgame_errors_use_distinct_eligible_decisions_and_one_total_error()
    {
        var result = InsightRules.Build([Fixture(), Fixture()], new() { MinimumGames = 2, RecurringGames = 2 });
        var conversion = result.Findings.Single(f => f.Category == "conversion-problems");
        Assert.Equal(2, conversion.Occurrences); Assert.Equal(4, conversion.EligibleMoves); Assert.Equal(.5, conversion.OccurrenceRate);
        Assert.Equal("recurring-pattern", conversion.Status); Assert.All(conversion.Evidence, e => { Assert.Equal(5, e.Ply); Assert.Equal("centipawn-advantage", e.ConversionObjective); });
        var endgame = result.Findings.Single(f => f.Category == "endgame-errors"); Assert.Equal(6, endgame.EligibleMoves); Assert.Equal(2, endgame.Occurrences);
        Assert.Equal(2, result.VerifiedErrorMoves); Assert.Equal(4, result.Findings.Sum(f => f.Occurrences));
        var coverage = result.CategoryCoverage!.Single(c => c.Category == "conversion-problems");
        Assert.Equal(6, coverage.CandidateDecisions); Assert.Equal(6, coverage.StableEvidenceDecisions); Assert.Equal(4, coverage.CentipawnOpportunities); Assert.Equal(0, coverage.MateOpportunities);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    [InlineData(400, false)]
    public void Lost_advantage_threshold_is_inclusive_and_preserved_advantages_are_negative_cases(int played, bool expected)
    {
        var result = InsightRules.Build([Fixture(played)], new());
        Assert.Equal(expected, result.Findings.Any(f => f.Category == "conversion-problems"));
        Assert.All(result.Findings, f => Assert.Equal("early-signal", f.Status));
    }

    [Fact]
    public void Missing_provisional_or_inconsistent_history_breaks_persistence_and_discloses_coverage()
    {
        var partial = Fixture(provisionalMiddle: true);
        var result = InsightRules.Build([partial], new()); Assert.DoesNotContain(result.Findings, f => f.Category == "conversion-problems");
        var coverage = result.CategoryCoverage!.Single(c => c.Category == "endgame-errors"); Assert.Equal(3, coverage.CandidateDecisions); Assert.Equal(2, coverage.EligibleDecisions); Assert.Equal(1, coverage.MissingOrProvisionalDecisions);
        partial.Run.Moves.Single(m => m.Ply == 3).Provisional = false;
        partial.Run.Moves.Single(m => m.Ply == 3).BestScoreValue++;
        result = InsightRules.Build([partial], new()); Assert.DoesNotContain(result.Findings, f => f.Category == "conversion-problems");
        Assert.Equal(1, result.CategoryCoverage!.Single(c => c.Category == "conversion-problems").RejectedEvidenceDecisions);
    }

    [Fact]
    public void Mate_objective_failures_have_their_own_opportunities_and_never_enter_centipawn_totals()
    {
        var result = InsightRules.Build([Fixture(winningMate: true)], new());
        var finding = result.Findings.Single(f => f.Category == "conversion-problems"); Assert.Equal(1, finding.MateTransitions); Assert.Equal(0, finding.CentipawnLossSum);
        Assert.Equal("forced-mate-continuation", finding.Evidence[0].ConversionObjective);
        var coverage = result.CategoryCoverage!.Single(c => c.Category == "conversion-problems"); Assert.Equal(0, coverage.CentipawnOpportunities); Assert.Equal(2, coverage.MateOpportunities);
    }

    [Fact]
    public void Material_eligibility_and_versioned_settings_do_not_rewrite_legacy_snapshots()
    {
        var game = Fixture();
        Assert.Empty(InsightRules.Build([game], new(), detectorVersion: InsightRules.LegacyVersion).Findings);
        Assert.Null(InsightRules.Build([game], new(), detectorVersion: InsightRules.LegacyVersion).CategoryCoverage);
        Assert.DoesNotContain(InsightRules.Build([game], new() { ConversionPersistenceDecisions = 4 }).Findings, f => f.Category == "conversion-problems");
        Assert.DoesNotContain(InsightRules.Build([game], new() { EndgameMinimumLoss = 500 }).Findings, f => f.Category == "endgame-errors");
        var withQueens = InsightRulesTests.TacticalFixture();
        Assert.DoesNotContain(InsightRules.Build([withQueens], new()).Findings, f => f.Category == "endgame-errors");
        Assert.Contains(InsightRules.Build([withQueens], new() { EndgameAllowQueens = true, EndgameMaximumNonPawnPieces = 1 }).Findings, f => f.Category == "endgame-errors");
        Assert.Throws<ArgumentException>(() => new InsightOptions { ConversionLostAdvantageCentipawns = 300 }.Validate());
        Assert.Throws<ArgumentException>(() => new InsightOptions { EndgameMaximumNonPawnPieces = 0 }.Validate());
    }

    [Fact]
    [Trait("Category", "Stockfish")]
    public async Task Real_stockfish_endgame_stalemate_error_preserves_explicit_mate_evidence()
    {
        var game = new PgnReader().Parse("[FEN \"7k/5Q2/6K1/8/8/8/8/8 w - - 0 1\"]\n1.Qe6 1/2-1/2");
        var path = Environment.GetEnvironmentVariable("Stockfish__Path") ?? throw new InvalidOperationException("Stockfish__Path is required.");
        await using var engine = await UciEngine.Start(path, 1, 32, CancellationToken.None);
        var position = new EnginePosition(game.InitialFen, []); var budget = new SearchBudget("real-objective-check", 100_000, 3000);
        var best = await engine.Search(position, budget, null, 20, CancellationToken.None);
        var played = await engine.Search(position, budget, [game.Moves[0].Uci], 20, CancellationToken.None);
        var b = best.Lines.Single(l => l.Rank == 1); var p = played.Lines.Single(l => l.Rank == 1);
        Assert.True(best.Complete && played.Complete); Assert.Equal("mate", b.Score.Kind); Assert.Equal(0, p.Score.Value);
        var compared = MoveClassifier.Compare(b.Score, p.Score, false);
        var run = new AnalysisRun { OwnerId = "real-engine-fixture", GameId = game.Id, Profile = "deep", State = "completed", EngineVersion = best.Engine, SettingsJson = best.SettingsJson, TotalPlies = 1, CompletedPlies = 1 };
        run.Moves.Add(new() { RunId = run.Id, Ply = 1, FenBefore = game.InitialFen, PlayedMove = game.Moves[0].Uci, Side = "white", BestMove = best.BestMove,
            BestScoreKind = b.Score.Kind, BestScoreValue = b.Score.Value, PlayedScoreKind = p.Score.Kind, PlayedScoreValue = p.Score.Value,
            Classification = compared.Label, CentipawnLoss = compared.CentipawnLoss, MateTransition = compared.MateTransition,
            Complete = true, Provisional = false, BestSearchJson = JsonSerializer.Serialize(best), PlayedSearchJson = JsonSerializer.Serialize(played), BudgetJson = JsonSerializer.Serialize(budget), CacheIdentity = "real-engine-objective-check" });
        var result = InsightRules.Build([new(game, "white", run)], new() { EndgameAllowQueens = true });
        var finding = result.Findings.Single(f => f.Category == "endgame-errors"); Assert.Equal(1, finding.MateTransitions); Assert.Equal(0, finding.CentipawnLossSum);
        Assert.Equal("missed-forced-mate", finding.Evidence[0].MateTransition); Assert.Equal(run.Id, finding.Evidence[0].RunId);
        Assert.True(ChessRules.Replay(game.InitialFen, [game.Moves[0].Uci]).IsEndGame);
    }

    [Fact]
    [Trait("Category", "Stockfish")]
    public async Task Real_stockfish_persisted_advantage_then_rook_loss_supports_conversion_and_default_endgame_rules()
    {
        var game = new PgnReader().Parse("[FEN \"4k3/pp6/5n2/8/8/8/PPPP4/4K2R w - - 0 1\"]\n1.Kf1 Ng4 2.Rh2 Nxh2+ 0-1");
        var path = Environment.GetEnvironmentVariable("Stockfish__Path") ?? throw new InvalidOperationException("Stockfish__Path is required.");
        await using var engine = await UciEngine.Start(path, 1, 32, CancellationToken.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var run = new AnalysisRun { OwnerId = "real-objective-fixture", GameId = game.Id, Profile = "deep", State = "completed", TotalPlies = 4, CompletedPlies = 4 };
        foreach (var move in game.Moves.Where(m => m.Side == "white"))
        {
            var position = new EnginePosition(game.InitialFen, game.Moves.Where(m => m.Ply < move.Ply).OrderBy(m => m.Ply).Select(m => m.Uci).ToArray());
            async Task<(SearchResult Best, SearchResult Played)> Pair(long nodes)
            {
                var budget = new SearchBudget("real-conversion-check", nodes, 8000);
                var best = await engine.Search(position, budget, null, 20, deadline.Token);
                var played = await engine.Search(position, budget, [move.Uci], 20, deadline.Token); return (best, played);
            }
            var first = await Pair(250_000); var final = await Pair(750_000);
            var b = final.Best.Lines.Single(l => l.Rank == 1); var p = final.Played.Lines.Single(l => l.Rank == 1);
            Assert.True(first.Best.Complete && first.Played.Complete && final.Best.Complete && final.Played.Complete);
            Assert.Equal("cp", b.Score.Kind); Assert.Equal("cp", p.Score.Kind);
            Assert.InRange(Math.Abs(first.Best.Lines[0].Score.Value - b.Score.Value), 0, 75);
            Assert.InRange(Math.Abs(first.Played.Lines[0].Score.Value - p.Score.Value), 0, 75);
            Assert.True(b.Score.Value >= 300, $"Expected substantial checked advantage at ply {move.Ply}, got {b.Score}.");
            var classified = MoveClassifier.Compare(b.Score, p.Score, false);
            run.EngineVersion = final.Best.Engine; run.SettingsJson = final.Best.SettingsJson;
            run.Moves.Add(new() { RunId = run.Id, Ply = move.Ply, FenBefore = move.FenBefore, PlayedMove = move.Uci, Side = move.Side,
                BestMove = final.Best.BestMove, BestScoreKind = b.Score.Kind, BestScoreValue = b.Score.Value, PlayedScoreKind = p.Score.Kind, PlayedScoreValue = p.Score.Value,
                CentipawnLoss = classified.CentipawnLoss, MateTransition = classified.MateTransition, Classification = classified.Label,
                Provisional = false, Complete = true, BestSearchJson = JsonSerializer.Serialize(final.Best), PlayedSearchJson = JsonSerializer.Serialize(final.Played),
                BudgetJson = JsonSerializer.Serialize(new { first, final }), CacheIdentity = "real-conversion-full-history-check" });
        }
        var result = InsightRules.Build([new(game, "white", run)], new());
        var conversion = result.Findings.Single(f => f.Category == "conversion-problems"); Assert.Equal(1, conversion.EligibleMoves); Assert.Equal(3, conversion.Evidence[0].Ply);
        Assert.Contains(result.Findings, f => f.Category == "endgame-errors"); Assert.Equal(1, result.VerifiedErrorMoves);
    }
}
