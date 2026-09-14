using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Imports;
using ChessLens.Core.Insights;

namespace ChessLens.Tests;

public sealed class InsightRulesTests
{
    [Fact]
    public void Four_core_categories_keep_traceable_evidence_and_unique_error_denominators()
    {
        var games = new[] { TacticalFixture(), TacticalFixture(), OpeningFixture(), OpeningFixture() };
        var result = InsightRules.Build(games, new() { MinimumGames = 2, RecurringGames = 2, MinimumClockMoves = 2 });
        Assert.Equal(4, result.AnalysedGames); Assert.Equal(8, result.EligibleMoves); Assert.Equal(4, result.VerifiedErrorMoves);
        Assert.Equal(4, result.ClockMoves); Assert.Equal(.5, result.ClockCoverage); Assert.True(result.ClockAvailable);
        Assert.Equal(new[] { "hanging-material", "missed-tactics", "opening-mistakes", "time-trouble" }, result.Findings.Select(f => f.Category).Order().ToArray());
        Assert.All(result.Findings, f => { Assert.Equal(2, f.Occurrences); Assert.Equal("recurring-pattern", f.Status); Assert.Equal(2, f.AffectedGames); });
        Assert.Equal(8, result.Findings.Sum(f => f.Occurrences)); // Tags overlap; the overall numerator remains four.
        var material = result.Findings.Single(f => f.Category == "hanging-material");
        Assert.Equal(8, material.EligibleMoves); Assert.Equal(.25, material.OccurrenceRate);
        Assert.All(material.Evidence, e => { Assert.Equal(900, e.BestMaterialChange); Assert.Equal(-100, e.PlayedMaterialChange); Assert.Equal("e4d5", e.BestMove); Assert.NotEqual(Guid.Empty, e.RunId); });
        var timing = result.Findings.Single(f => f.Category == "time-trouble");
        Assert.Equal(4, timing.EligibleMoves); Assert.Equal(.5, timing.OccurrenceRate);
        Assert.All(timing.Evidence, e => { Assert.Equal(3, e.RemainingSeconds); Assert.Equal(592, e.SpentSeconds); });
    }

    [Fact]
    public void Small_samples_and_missing_timing_do_not_create_confident_claims()
    {
        var result = InsightRules.Build([TacticalFixture()], new());
        Assert.All(result.Findings, f => { Assert.Equal("early-signal", f.Status); Assert.Equal("low", f.Confidence); });
        Assert.False(result.ClockAvailable); Assert.Equal(0, result.ClockCoverage);
        Assert.DoesNotContain(result.Findings, f => f.Category == "opening-mistakes" || f.Category == "time-trouble");
        Assert.Empty(InsightRules.Build([], new()).Findings);
    }

    [Fact]
    public void Compensation_and_illegal_or_provisional_lines_are_not_material_loss_evidence()
    {
        var compensated = TacticalFixture();
        var mistake = compensated.Run.Moves.Single(m => m.Ply == 1);
        mistake.PlayedScoreValue = 500; mistake.CentipawnLoss = 100;
        mistake.PlayedSearchJson = Search("e1f1", new("cp", 500), ["e1f1", "d5e4", "f1g1", "e4e5"]);
        var result = InsightRules.Build([compensated], new());
        Assert.DoesNotContain(result.Evidence, e => e.Tags.Contains("hanging-material"));
        mistake.BestSearchJson = Search("e4d5", new("cp", 600), ["e4e8"]);
        result = InsightRules.Build([compensated], new());
        Assert.Empty(result.Evidence); Assert.Equal(1, result.RejectedEvidenceMoves);
        mistake.Provisional = true;
        result = InsightRules.Build([compensated], new());
        Assert.Empty(result.Evidence); Assert.Equal(2, result.ProvisionalMoves);
    }

    [Fact]
    public void Clock_coverage_excludes_missing_annotations_and_unsupported_controls()
    {
        var game = OpeningFixture().Game;
        var moves = game.Moves.OrderBy(m => m.Ply).ToArray();
        Assert.NotNull(InsightRules.Clock("600+5", moves, moves[2]));
        moves[0].ClockSeconds = null;
        Assert.Null(InsightRules.Clock("600+5", moves, moves[2]));
        Assert.Null(InsightRules.Clock("40/7200:3600", moves, moves[2]));
        Assert.Null(InsightRules.Clock(null, moves, moves[2]));
        moves[0].ClockSeconds = 1; moves[2].ClockSeconds = 100;
        Assert.Null(InsightRules.Clock("600+5", moves, moves[2]));
    }

    internal static InsightGame TacticalFixture()
    {
        var game = new PgnReader().Parse($"""
            [Event "Labelled tactical test double {Guid.NewGuid()}"]
            [FEN "4k3/8/8/3q4/4P3/8/8/4K3 w - - 0 1"]
            1.Kf1 Qxe4 2.Kg1 Qe5 0-1
            """);
        var run = Run(game);
        var mistake = run.Moves.Single(m => m.Ply == 1);
        mistake.BestMove = "e4d5"; mistake.BestScoreValue = 600; mistake.PlayedScoreValue = -900;
        mistake.CentipawnLoss = 1500; mistake.Classification = "blunder"; mistake.Provisional = false;
        mistake.BestSearchJson = Search("e4d5", new("cp", 600), ["e4d5", "e8f7", "d5d6", "f7e6"]);
        mistake.PlayedSearchJson = Search("e1f1", new("cp", -900), ["e1f1", "d5e4", "f1g1", "e4e5"]);
        return new(game, "white", run);
    }
    internal static InsightGame OpeningFixture()
    {
        var game = new PgnReader().Parse($$"""
            [Event "Labelled clock test double {{Guid.NewGuid()}}"]
            [TimeControl "600+5"]
            [Opening "Illustrative opening fixture"]
            1.f3 {[%clk 0:09:55]} e5 {[%clk 0:09:55]} 2.g4 {[%clk 0:00:08]} Qh4# {[%clk 0:09:50]} 0-1
            """);
        var run = Run(game);
        var mistake = run.Moves.Single(m => m.Ply == 3);
        mistake.BestMove = "d2d4"; mistake.PlayedScoreKind = "mate"; mistake.PlayedScoreValue = -1;
        mistake.CentipawnLoss = null; mistake.MateTransition = "allowed-forced-mate"; mistake.Classification = "blunder"; mistake.Provisional = false;
        mistake.BestSearchJson = Search("d2d4", new("cp", 0), ["d2d4", "e5d4", "d1d4", "b8c6"]);
        mistake.PlayedSearchJson = Search("g2g4", new("mate", -1), ["g2g4", "d8h4"]);
        return new(game, "white", run);
    }
    private static AnalysisRun Run(SourceGame game)
    {
        var run = new AnalysisRun { OwnerId = "labelled-test-owner", GameId = game.Id, State = "completed", CompletedPlies = game.Moves.Count, TotalPlies = game.Moves.Count };
        run.Moves = game.Moves.Select(m => new MoveAnalysis { RunId = run.Id, Ply = m.Ply, FenBefore = m.FenBefore, PlayedMove = m.Uci,
            Side = m.Side, BestMove = m.Uci, BestScoreKind = "cp", PlayedScoreKind = "cp", Classification = "sound", Provisional = true,
            CentipawnLoss = 0, BestSearchJson = Search(m.Uci, new("cp", 0), [m.Uci]), PlayedSearchJson = Search(m.Uci, new("cp", 0), [m.Uci]),
            BudgetJson = "{}", CacheIdentity = "labelled-fixture", Complete = true }).ToList();
        return run;
    }
    private static string Search(string best, EngineScore score, string[] pv) => JsonSerializer.Serialize(new SearchResult(
        "Fixture engine (test double)", "{}", best, [new(1, 15, 250_000, 100, score, pv, false)], 100, 1000, true));
}
