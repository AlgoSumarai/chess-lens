using System.Text.Json;
using ChessLens.Core.Accounts;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Imports;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class AnalysisPipelineTests(DatabaseFixture fixture)
{
    private async Task<Guid> Queue(bool cancelled = false)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        var owner = new AppUser { UserName = "pipeline-" + Guid.NewGuid().ToString("N") };
        db.Users.Add(owner);
        var game = new PgnReader().Parse($"[Event \"{Guid.NewGuid()}\"]\n" + PgnTests.Game);
        db.Games.Add(game);
        db.UserGames.Add(new UserGame { OwnerId = owner.Id, GameId = game.Id, PlayerColour = "white" });
        var run = new AnalysisRun { OwnerId = owner.Id, GameId = game.Id, TotalPlies = game.Moves.Count, CancelRequested = cancelled };
        db.AnalysisRuns.Add(run); await db.SaveChangesAsync();
        return run.Id;
    }

    [Fact]
    public async Task Results_are_versioned_and_repeated_delivery_does_not_duplicate_plies()
    {
        var id = await Queue();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        var processor = new AnalysisProcessor(db, new FixtureEngine());
        Assert.True(await processor.RunNext(CancellationToken.None));
        Assert.False(await processor.RunNext(CancellationToken.None));
        db.ChangeTracker.Clear();
        var run = await db.AnalysisRuns.Include(r => r.Moves).SingleAsync(r => r.Id == id);
        Assert.Equal("completed", run.State); Assert.Equal(4, run.Moves.Count); Assert.Equal(4, run.CompletedPlies);
        Assert.Equal("Fixture engine (test double)", run.EngineVersion);
        foreach (var move in run.Moves)
        {
            Assert.True(move.Complete);
            Assert.Equal(1, move.Side == "white" ? move.Ply % 2 : 1 - move.Ply % 2);
            Assert.NotEmpty(move.CacheIdentity);
            var evidence = JsonSerializer.Deserialize<SearchResult>(move.BestSearchJson)!;
            Assert.Equal(move.BestMove, evidence.BestMove);
        }
        // Re-delivery resumes at the checkpoint instead of inserting the moves again.
        run.State = "running"; run.LeaseUntil = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.True(await processor.RunNext(CancellationToken.None));
        Assert.Equal(4, await db.MoveAnalyses.CountAsync(m => m.RunId == id));
    }

    [Fact]
    public async Task Cancellation_is_terminal_and_does_not_start_an_engine_search()
    {
        var id = await Queue(cancelled: true);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        var engine = new FixtureEngine();
        await new AnalysisProcessor(db, engine).RunNext(CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.Equal("cancelled", (await db.AnalysisRuns.SingleAsync(r => r.Id == id)).State);
        Assert.Equal(0, engine.Searches);
    }

    [Fact]
    public async Task Expired_final_lease_becomes_permanent_failure()
    {
        var id = await Queue();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        await db.AnalysisRuns.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.State, "running")
            .SetProperty(r => r.Attempts, 4).SetProperty(r => r.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.False(await new AnalysisProcessor(db, new FixtureEngine()).RunNext(CancellationToken.None));
        Assert.Equal("failed", (await db.AnalysisRuns.SingleAsync(r => r.Id == id)).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_partial_review_resumes_in_a_new_worker_scope(bool hostShutdown)
    {
        var id = await Queue();
        string savedEvidence;
        using var stopping = new CancellationTokenSource();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            var engine = new InterruptedEngine(stopping, hostShutdown);
            var processor = new AnalysisProcessor(db, engine);
            if (hostShutdown)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.RunNext(stopping.Token));
            else
                await Assert.ThrowsAsync<IOException>(() => processor.RunNext(stopping.Token));
            db.ChangeTracker.Clear();
            var partial = await db.AnalysisRuns.Include(r => r.Moves).SingleAsync(r => r.Id == id);
            Assert.Equal(1, partial.CompletedPlies);
            savedEvidence = Assert.Single(partial.Moves).BestSearchJson;
            Assert.Equal(hostShutdown ? "running" : "queued", partial.State);
            Assert.Equal(1, partial.Attempts);
            // Simulate lease expiry after a host shutdown, without sleeping five minutes.
            if (hostShutdown)
                await db.AnalysisRuns.Where(r => r.Id == id).ExecuteUpdateAsync(s =>
                    s.SetProperty(r => r.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            var replacement = new FixtureEngine();
            var changedSettings = Options.Create(new AnalysisOptions { InaccuracyCentipawns = 400, MistakeCentipawns = 800, BlunderCentipawns = 1200 });
            Assert.True(await new AnalysisProcessor(db, replacement, changedSettings).RunNext(CancellationToken.None));
            db.ChangeTracker.Clear();
            var completed = await db.AnalysisRuns.Include(r => r.Moves).SingleAsync(r => r.Id == id);
            Assert.Equal("completed", completed.State);
            Assert.Equal(2, completed.Attempts);
            Assert.Equal(new[] { 1, 2, 3, 4 }, completed.Moves.OrderBy(m => m.Ply).Select(m => m.Ply));
            Assert.Equal(savedEvidence, completed.Moves.Single(m => m.Ply == 1).BestSearchJson);
            Assert.DoesNotContain(0, replacement.HistoryLengths);
            Assert.Equal(50, JsonSerializer.Deserialize<AnalysisOptions>(completed.ClassificationSettingsJson!)!.InaccuracyCentipawns);
            Assert.All(completed.Moves.Where(m => m.CentipawnLoss == 250), m => Assert.Equal("blunder", m.Classification));
        }
    }

    private sealed class InterruptedEngine(CancellationTokenSource stopping, bool hostShutdown) : IChessEngine
    {
        private readonly FixtureEngine inner = new();
        public Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct)
        {
            if (position.History.Length > 0)
            {
                if (hostShutdown) { stopping.Cancel(); ct.ThrowIfCancellationRequested(); }
                throw new IOException("Labelled test fixture: engine process terminated after a committed ply.");
            }
            return inner.Search(position, budget, searchMoves, skill, ct);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Classification_stability_uses_evidence_scores_not_the_order_of_equivalent_alternatives(bool shiftingScores)
    {
        var id = await Queue();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        Assert.True(await new AnalysisProcessor(db, new AlternativeEngine(shiftingScores)).RunNext(CancellationToken.None));
        db.ChangeTracker.Clear();
        var move = await db.MoveAnalyses.SingleAsync(m => m.RunId == id && m.Ply == 1);
        Assert.Equal("blunder", move.Classification);
        Assert.Equal(shiftingScores, move.Provisional);
        var verification = JsonDocument.Parse(move.BudgetJson).RootElement.GetProperty("verification");
        Assert.NotEqual(verification.GetProperty("firstBestMove").GetString(), verification.GetProperty("finalBestMove").GetString());
        Assert.Equal(!shiftingScores, verification.GetProperty("stableBestScore").GetBoolean());
    }

    [Fact]
    public async Task Resuming_an_older_analysis_version_preserves_its_original_stability_rule()
    {
        var id = await Queue();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        await db.AnalysisRuns.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.AnalysisVersion, "chesslens-analysis-v2"));
        await new AnalysisProcessor(db, new AlternativeEngine(false)).RunNext(CancellationToken.None);
        db.ChangeTracker.Clear();
        var move = await db.MoveAnalyses.SingleAsync(m => m.RunId == id && m.Ply == 1);
        Assert.True(move.Provisional);
        Assert.Equal("legacy-best-move-stability", JsonDocument.Parse(move.BudgetJson).RootElement.GetProperty("verification").GetProperty("version").GetString());
    }

    private sealed class AlternativeEngine(bool shiftingScores) : IChessEngine
    {
        public Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct)
        {
            var legal = ChessRules.View(ChessRules.Replay(position.InitialFen, position.History)).LegalMoves;
            var deep = budget.Nodes >= SearchBudget.Deep.Nodes;
            // The source fixture starts with f3. Do not depend on the rules
            // library's move-enumeration order or accidentally select that move.
            var alternatives = legal.Where(m => m != "f2f3").Order().ToArray();
            var best = searchMoves?.First() ?? alternatives[deep ? 1 : 0];
            var score = (searchMoves is null ? 0 : -250) + (deep && shiftingScores ? 200 : 0);
            return Task.FromResult(new SearchResult("Fixture engine (test double)", "{}", best,
                [new(1, 12, 1000, 5, new("cp", score), [best], false)], 5, 1000, true));
        }
    }

    private sealed class FixtureEngine : IChessEngine
    {
        public int Searches { get; private set; }
        public List<int> HistoryLengths { get; } = [];
        public Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct)
        {
            Searches++;
            HistoryLengths.Add(position.History.Length);
            var legal = ChessRules.View(ChessRules.Replay(position.InitialFen, position.History)).LegalMoves;
            var best = searchMoves?.First() ?? legal[0];
            return Task.FromResult(new SearchResult("Fixture engine (test double)", "{}", best,
                [new SearchLine(1, 12, 1000, 5, new EngineScore("cp", searchMoves is null ? 0 : -250), [best], false)], 5, 1000, true));
        }
    }
}
