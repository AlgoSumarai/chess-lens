using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Imports;
using ChessLens.Core.Persistence;
using ChessLens.Core.Training;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class PuzzlePipelineTests(DatabaseFixture fixture)
{
    private async Task<(HttpClient Client, string Owner)> Account()
    {
        var client = fixture.CreateClient();
        async Task Csrf()
        {
            var token = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        }
        await Csrf();
        (await client.PostAsJsonAsync("/api/account/register", new { email = $"puzzle-{Guid.NewGuid():N}@test.example", password = "ChessLens.Puzzles2026!" })).EnsureSuccessStatusCode();
        await Csrf(); return (client, (await client.GetFromJsonAsync<JsonElement>("/api/account/me")).GetProperty("id").GetString()!);
    }
    private async Task<Guid> Seed(string owner)
    {
        var game = new PgnReader().Parse($"""
            [Event "Labelled synthetic puzzle fixture {Guid.NewGuid()}"]
            [White "Learner"]
            [Black "Partner"]
            [Result "1/2-1/2"]
            [SetUp "1"]
            [FEN "7k/5Q2/6K1/8/8/8/8/8 w - - 0 1"]

            1. Qf1 Kg8 2. Qf7+ Kh8 1/2-1/2
            """);
        var run = new AnalysisRun { OwnerId = owner, GameId = game.Id, State = "completed", TotalPlies = 4, CompletedPlies = 4, EngineVersion = "Labelled puzzle engine double", SettingsJson = "{}" };
        var source = game.Moves[0];
        run.Moves.Add(new() { RunId = run.Id, Ply = 1, Side = "white", FenBefore = source.FenBefore, PlayedMove = source.Uci, BestMove = "f7g7", BestScoreKind = "mate", BestScoreValue = 1,
            PlayedScoreKind = "cp", PlayedScoreValue = 0, MateTransition = "missed-forced-mate", Classification = "mistake", Complete = true, Provisional = false,
            BestSearchJson = "{}", PlayedSearchJson = "{}", BudgetJson = "{}", CacheIdentity = "labelled-puzzle-fixture" });
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        db.Games.Add(game); db.UserGames.Add(new UserGame { OwnerId = owner, GameId = game.Id, PlayerColour = "white" }); db.AnalysisRuns.Add(run); await db.SaveChangesAsync(); return run.Id;
    }
    private async Task Process(IChessEngine? engine = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.True(await new PuzzleProcessor(scope.ServiceProvider.GetRequiredService<LensDbContext>(), engine ?? new PuzzleValidatorTests.MateFixtureEngine()).RunNext(CancellationToken.None));
    }
    private static async Task<JsonElement> Post(HttpClient client, string path, object? body = null)
    {
        var response = body is null ? await client.PostAsync(path, null) : await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Published_puzzles_hide_answers_and_persist_alternatives_hints_attempts_and_spaced_review_with_owner_isolation()
    {
        var (client, owner) = await Account(); using var alice = client;
        var (other, _) = await Account(); using var bob = other;
        var runId = await Seed(owner); var queued = await Post(alice, "/api/puzzles", new { runId, ply = 1 }); var id = queued.GetProperty("id").GetGuid();
        Assert.Equal(id, (await Post(alice, "/api/puzzles", new { runId, ply = 1 })).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/puzzles/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/puzzles/{id}/cancel", null)).StatusCode);
        await Process();
        var published = await alice.GetFromJsonAsync<JsonElement>($"/api/puzzles/{id}");
        Assert.Equal("ready", published.GetProperty("state").GetString());
        Assert.False(published.TryGetProperty("solution", out _)); Assert.False(published.TryGetProperty("runId", out _));
        var started = await Post(alice, $"/api/puzzles/{id}/attempts"); var attempt = started.GetProperty("id").GetGuid();
        Assert.Equal(attempt, (await Post(alice, $"/api/puzzles/{id}/attempts")).GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, started.GetProperty("review").ValueKind);
        Assert.False(started.TryGetProperty("bestMove", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/puzzles/attempts/{attempt}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/puzzles/attempts/{attempt}/moves", new { version = 0, move = "f7g7" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/puzzles/attempts/{attempt}/moves", new { version = 0, move = "a1a8" })).StatusCode);
        var hint = await Post(alice, $"/api/puzzles/attempts/{attempt}/hint", new { version = 0 }); Assert.Equal(1, hint.GetProperty("hintsUsed").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsJsonAsync($"/api/puzzles/attempts/{attempt}/hint", new { version = 0 })).StatusCode);
        string alternate;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); var saved = await db.Puzzles.SingleAsync(p => p.Id == id);
            var solution = JsonSerializer.Deserialize<PuzzleSolution>(saved.SolutionJson!)!;
            alternate = solution.Root.Choices[1].Move;
        }
        var solved = await Post(alice, $"/api/puzzles/attempts/{attempt}/moves", new { version = 1, move = alternate });
        Assert.Equal("solved", solved.GetProperty("state").GetString()); Assert.Equal(runId, solved.GetProperty("review").GetProperty("runId").GetGuid());
        Assert.InRange((solved.GetProperty("reviewDueAt").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalHours, 23.9, 24.1);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsJsonAsync($"/api/puzzles/attempts/{attempt}/moves", new { version = 1, move = alternate })).StatusCode);
        var next = (await Post(alice, $"/api/puzzles/{id}/attempts")).GetProperty("id").GetGuid();
        var failed = await Post(alice, $"/api/puzzles/attempts/{next}/moves", new { version = 0, move = "f7f1" });
        Assert.Equal("failed", failed.GetProperty("state").GetString()); Assert.InRange((failed.GetProperty("reviewDueAt").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalHours, 3.9, 4.1);
        foreach (var days in new[] { 1, 3 })
        {
            next = (await Post(alice, $"/api/puzzles/{id}/attempts")).GetProperty("id").GetGuid();
            var success = await Post(alice, $"/api/puzzles/attempts/{next}/moves", new { version = 0, move = alternate });
            Assert.InRange((success.GetProperty("reviewDueAt").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalDays, days - .01, days + .01);
        }
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            Assert.Equal(4, await db.PuzzleAttempts.CountAsync(a => a.PuzzleId == id));
            Assert.Equal(1, await db.Puzzles.CountAsync(p => p.Id == id));
        }
        (await alice.DeleteAsync("/api/account/data")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/puzzles/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/puzzles/attempts/{attempt}")).StatusCode);
    }

    [Fact]
    public async Task Expired_validation_leases_resume_and_cancelled_jobs_cannot_be_revived_by_late_engine_results()
    {
        var (client, owner) = await Account(); using var alice = client;
        var runId = await Seed(owner); var id = (await Post(alice, "/api/puzzles", new { runId, ply = 1 })).GetProperty("id").GetGuid();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            await db.Puzzles.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.State, "running").SetProperty(p => p.Attempts, 1).SetProperty(p => p.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        await Process(); Assert.Equal("ready", (await alice.GetFromJsonAsync<JsonElement>($"/api/puzzles/{id}")).GetProperty("state").GetString());
        runId = await Seed(owner); var cancelled = (await Post(alice, "/api/puzzles", new { runId, ply = 1 })).GetProperty("id").GetGuid();
        await Process(new CallbackEngine(async () => (await alice.PostAsync($"/api/puzzles/{cancelled}/cancel", null)).EnsureSuccessStatusCode()));
        Assert.Equal("cancelled", (await alice.GetFromJsonAsync<JsonElement>($"/api/puzzles/{cancelled}")).GetProperty("state").GetString());
        await using var finalScope = fixture.Services.CreateAsyncScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<LensDbContext>();
        Assert.Null((await finalDb.Puzzles.SingleAsync(p => p.Id == cancelled)).SolutionJson);
        Assert.False(await new PuzzleProcessor(finalDb, new PuzzleValidatorTests.MateFixtureEngine()).RunNext(CancellationToken.None));
    }
    [Fact]
    public async Task Incomplete_engine_evidence_retries_only_three_times_and_never_publishes_an_answer()
    {
        var (client, owner) = await Account(); using var alice = client;
        var runId = await Seed(owner); var id = (await Post(alice, "/api/puzzles", new { runId, ply = 1 })).GetProperty("id").GetGuid();
        foreach (var state in new[] { "queued", "queued", "failed" })
        {
            await Process(new IncompleteEngine());
            Assert.Equal(state, (await alice.GetFromJsonAsync<JsonElement>($"/api/puzzles/{id}")).GetProperty("state").GetString());
        }
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        var saved = await db.Puzzles.SingleAsync(p => p.Id == id); Assert.Equal(3, saved.Attempts); Assert.Null(saved.SolutionJson);
        Assert.False(await new PuzzleProcessor(db, new IncompleteEngine()).RunNext(CancellationToken.None));
    }
    private sealed class IncompleteEngine : IChessEngine
    {
        public async Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct) =>
            await new PuzzleValidatorTests.MateFixtureEngine().Search(position, budget, searchMoves, skill, ct) with { Complete = false };
    }
    private sealed class CallbackEngine(Func<Task> callback) : IChessEngine
    {
        private bool called;
        public async Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct)
        {
            if (!called) { called = true; await callback(); }
            return await new PuzzleValidatorTests.MateFixtureEngine().Search(position, budget, searchMoves, skill, ct);
        }
    }
}
