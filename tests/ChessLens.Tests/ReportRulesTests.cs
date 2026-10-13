using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Insights;
using ChessLens.Core.Reports;

namespace ChessLens.Tests;

public sealed class ReportRulesTests
{
    private static (ReportInputs Inputs, Dictionary<Guid, SourceGame> Games, Dictionary<Guid, AnalysisRun> Runs) Fixture(int currentGames = 5, int previousGames = 5)
    {
        var period = ReportCalendar.Progress(new(2026, 10, 5), new(2026, 10, 11)); var previous = ReportCalendar.Previous(period);
        var games = new Dictionary<Guid, SourceGame>(); var runs = new Dictionary<Guid, AnalysisRun>();
        ReportGameReference[] Make(int count, ReportPeriod selected)
        {
            return Enumerable.Range(0, count).Select(index =>
            {
                var row = InsightRulesTests.TacticalFixture(); var run = row.Run; var game = row.Game;
                game.PlayedAt = selected.From.AddDays(index % 5); game.TimeControl = "300+0";
                run.EngineVersion = "Fixture engine (test double)"; run.SettingsJson = "{}"; run.ClassificationSettingsJson = JsonSerializer.Serialize(new AnalysisOptions());
                foreach (var move in run.Moves) move.Provisional = false;
                games.Add(game.Id, game); runs.Add(run.Id, run);
                return new ReportGameReference(game.Id, "white", "pgn", null, null, null, game.TimeControl, "blitz", game.Result, game.PlayedAt, 1500 + index, game.Moves.Count,
                    new(run.Id, run.EngineVersion, run.Profile, run.AnalysisVersion, run.SettingsJson, run.ClassificationSettingsJson, selected.From.AddDays(2), true));
            }).ToArray();
        }
        var current = new ReportSelection(currentGames, 0, Make(currentGames, period), 0, false, [], []);
        var prior = new ReportSelection(previousGames, 0, Make(previousGames, previous), 0, false, [], []);
        return (new(period, previous, null, DateTimeOffset.Parse("2026-10-13T00:00:00Z"), 0, InsightRules.Version, new(), current, prior, 50), games, runs);
    }

    [Fact]
    public void Large_periods_keep_recent_prefixes_without_permanently_blocking_automatic_reports()
    {
        var (inputs, _, _) = Fixture();
        var current = inputs.Current with { Games = inputs.Current.Games.Select(g => g with { Plies = 4_000 }).ToArray() };
        var previous = inputs.Previous with { Games = inputs.Previous.Games.Select(g => g with { Plies = 4_000 }).ToArray() };
        var bounded = ReportEvidenceBudget.Apply(current, previous);
        Assert.Equal(current.Games.Take(3), bounded.Current.Games); Assert.Equal(previous.Games.Take(2), bounded.Previous.Games);
        Assert.Equal(20_000, bounded.Current.Games.Sum(g => g.Plies) + bounded.Previous.Games.Sum(g => g.Plies));
        Assert.Equal(5, bounded.Current.MatchingGames); Assert.Equal(5, bounded.Previous.MatchingGames);
        Assert.Equal(current.Training, bounded.Current.Training);
        var uneven = ReportEvidenceBudget.Apply(current, previous with { Games = [previous.Games[0]] });
        Assert.Equal(4, uneven.Current.Games.Length); Assert.Single(uneven.Previous.Games);
        Assert.Throws<ArgumentException>(() => ReportEvidenceBudget.Apply(current with { Games = [current.Games[0] with { Plies = 20_001 }] }, previous));
    }

    [Fact]
    public void Supported_rates_have_unique_error_and_category_denominators_with_no_cross_provider_pool_or_analysis_average()
    {
        var (inputs, games, runs) = Fixture(); var result = ReportRules.Build(inputs, games, runs); var group = Assert.Single(result.Groups);
        Assert.Equal(5, group.Summary.VerifiedErrorMoves); Assert.Equal(10, group.Summary.EligibleMoves); Assert.Equal("supported", group.Comparison.Status);
        Assert.Equal(0, group.Comparison.RateDifference); Assert.Equal(5, result.Results.Losses); Assert.Equal(10, group.Coverage.StableDecisions);
        var material = group.WeaknessRates.Single(r => r.Category == "hanging-material"); Assert.Equal(5, material.Occurrences); Assert.Equal(10, material.EligibleDecisions);
        Assert.Equal("supported", material.Comparison.Status); Assert.Contains(group.WeaknessRates, r => r.Category == "conversion-problems" && r.EligibleDecisions == 0 && r.Comparison.Status == "insufficient");
        Assert.All(group.Summary.Findings, f => Assert.InRange(f.Evidence.Length, 1, 3)); Assert.Equal(5, group.Ratings.Length);
        inputs = inputs with { Current = inputs.Current with { Games = inputs.Current.Games.Select((g, i) => i switch
        {
            0 => g with { Source = "lichess", RatingPool = "blitz" },
            1 => g with { Source = "chesscom", RatingPool = "blitz" },
            2 => g with { Source = "lichess", RatingPool = "rapid" },
            3 => g with { Analysis = g.Analysis! with { Profile = "deep" } },
            _ => g
        }).ToArray() } };
        var split = ReportRules.Build(inputs, games, runs); Assert.Equal(5, split.Groups.Length);
        Assert.All(split.Groups, g => Assert.Equal("insufficient", g.Comparison.Status));
    }

    [Theory]
    [InlineData("sparse")]
    [InlineData("capped")]
    [InlineData("coverage")]
    [InlineData("unknown-settings")]
    [InlineData("overlap")]
    [InlineData("unreviewed")]
    [InlineData("missing-identity")]
    public void Unsupported_comparisons_explain_missing_sample_evidence_instead_of_producing_a_change(string condition)
    {
        var (inputs, games, runs) = Fixture(condition == "sparse" ? 4 : 5);
        if (condition == "capped") inputs = inputs with { Current = inputs.Current with { MatchingGames = 500 } };
        if (condition == "coverage") foreach (var g in inputs.Current.Games.Take(2)) runs[g.Analysis!.RunId].Moves.Single(m => m.Ply == 3).Provisional = true;
        if (condition == "unknown-settings") inputs = inputs with { Current = inputs.Current with { Games = inputs.Current.Games.Select(g => g with { Analysis = g.Analysis! with { SettingsKnown = false } }).ToArray() } };
        if (condition == "overlap") inputs = inputs with { Previous = inputs.Current };
        if (condition is "unreviewed" or "missing-identity")
        {
            var extra = inputs.Current.Games.Take(2).Select(g => g with { GameId = Guid.NewGuid(), Analysis = null, Colour = condition == "missing-identity" ? null : "white" }).ToArray();
            inputs = inputs with { Current = inputs.Current with { MatchingGames = 7, Games = [.. inputs.Current.Games, .. extra] } };
        }
        var comparison = Assert.Single(ReportRules.Build(inputs, games, runs).Groups).Comparison;
        Assert.Equal("insufficient", comparison.Status); Assert.Null(comparison.RateDifference); Assert.NotEmpty(comparison.Explanation);
    }

    [Fact]
    public void Training_assistance_cancellation_and_ungraded_results_stay_separate_and_do_not_imply_game_improvement()
    {
        var (inputs, games, runs) = Fixture(); var id = inputs.Current.Games[0].GameId;
        var outcomes = new[] { ("succeeded", false), ("succeeded", true), ("needs-practice", false), ("cancelled", false), ("ungraded", true) };
        inputs = inputs with { Current = inputs.Current with { TrainingCount = 5, Training = outcomes.Select(o => new TrainingObservation("practice", Guid.NewGuid(), id, o.Item1, o.Item2, "Labelled practice fixture", "preserve-advantage", inputs.Period.To.AddSeconds(-1))).ToArray() } };
        var result = ReportRules.Build(inputs, games, runs); var training = Assert.Single(result.Training);
        Assert.Equal(5, training.Attempts); Assert.Equal(1, training.SuccessfulUnassisted); Assert.Equal(1, training.SuccessfulAssisted);
        Assert.Equal(1, training.NeedsPractice); Assert.Equal(1, training.CancelledOrRestarted); Assert.Equal(1, training.Ungraded);
        Assert.Equal(0, Assert.Single(result.Groups).Comparison.RateDifference); Assert.Contains("training completion alone", result.Groups[0].Comparison.Explanation);
    }

    [Fact]
    public void Improved_observed_error_rate_is_descriptive_even_when_results_remain_losses_and_no_weakness_is_fabricated()
    {
        var (inputs, games, runs) = Fixture();
        foreach (var reference in inputs.Current.Games)
        {
            var move = runs[reference.Analysis!.RunId].Moves.Single(m => m.Ply == 1);
            move.BestMove = move.PlayedMove; move.BestScoreValue = move.PlayedScoreValue; move.BestSearchJson = move.PlayedSearchJson;
            move.Classification = "sound"; move.CentipawnLoss = 0;
        }
        var result = ReportRules.Build(inputs, games, runs); var group = Assert.Single(result.Groups);
        Assert.Equal(-.5, group.Comparison.RateDifference); Assert.Equal(5, result.Results.Losses); Assert.Empty(group.Summary.Findings);
        Assert.Contains(result.PracticePlan, p => p.Contains("does not support a recurring weakness"));
    }
}
