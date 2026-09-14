using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Api;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Imports;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class PositionAnalysisTests(DatabaseFixture fixture)
{
    private async Task<(HttpClient Client, Guid GameId)> Setup()
    {
        var client = fixture.CreateClient();
        async Task Csrf()
        {
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        }
        await Csrf();
        (await client.PostAsJsonAsync("/api/account/register", new { email = $"position-{Guid.NewGuid():N}@test.example", password = "ChessLens.Position2026!" })).EnsureSuccessStatusCode();
        await Csrf();
        var user = await client.GetFromJsonAsync<JsonElement>("/api/account/me");
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        var game = new PgnReader().Parse($"[Event \"{Guid.NewGuid()}\"]\n" + PgnTests.Game);
        db.Games.Add(game); db.UserGames.Add(new() { OwnerId = user.GetProperty("id").GetString()!, GameId = game.Id });
        await db.SaveChangesAsync();
        return (client, game.Id);
    }

    [Fact]
    public async Task Navigation_barrier_rejects_late_requests_and_late_engine_results()
    {
        var (client, game) = await Setup(); using var owner = client;
        var channel = Guid.NewGuid();
        async Task<HttpResponseMessage> Post(int sequence, bool cancel = false) => await client.PostAsJsonAsync($"/api/games/{game}/position-analysis",
            new PositionAnalysisRequest(channel, sequence, 1, ["e7e5"], cancel));
        var response = await Post(2); response.EnsureSuccessStatusCode();
        var queued = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = queued.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(1)).StatusCode);
        Assert.Equal(id, (await (await Post(2)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        var engine = new FixtureEngine(async () => Assert.Equal(HttpStatusCode.NoContent, (await Post(3, true)).StatusCode));
        Assert.True(await new PositionAnalysisProcessor(db, engine).RunNext(CancellationToken.None));
        Assert.Equal(new[] { "f2f3", "e7e5" }, engine.LastPosition!.History);
        var cancelled = await client.GetFromJsonAsync<JsonElement>($"/api/position-analysis/{id}");
        Assert.Equal("cancelled", cancelled.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("result").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(2)).StatusCode);
        var next = await (await Post(4)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(await new PositionAnalysisProcessor(db, new FixtureEngine()).RunNext(CancellationToken.None));
        var completed = await client.GetFromJsonAsync<JsonElement>($"/api/position-analysis/{next.GetProperty("id").GetGuid()}");
        Assert.Equal("completed", completed.GetProperty("state").GetString());
        Assert.Equal("Fixture engine (test double)", completed.GetProperty("result").GetProperty("engine").GetString());
        Assert.False(await new PositionAnalysisProcessor(db, new FixtureEngine()).RunNext(CancellationToken.None));
    }

    [Fact]
    public async Task Position_jobs_enforce_owner_and_legal_source_history()
    {
        var (alice, game) = await Setup(); using var owner = alice;
        var (bob, otherGame) = await Setup(); using var other = bob;
        var channel = Guid.NewGuid();
        var request = new PositionAnalysisRequest(channel, 1, 0, []);
        var response = await alice.PostAsJsonAsync($"/api/games/{game}/position-analysis", request);
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/position-analysis/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/games/{game}/position-analysis", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/games/{otherGame}/position-analysis", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/games/{game}/position-analysis", request with { Sequence = 2, Variation = ["e2e5"] })).StatusCode);
        (await alice.PostAsJsonAsync($"/api/games/{game}/position-analysis", request with { Sequence = 3, CancelOnly = true })).EnsureSuccessStatusCode();
        (await alice.DeleteAsync("/api/account/data")).EnsureSuccessStatusCode();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        Assert.False(await db.PositionAnalysisJobs.AnyAsync(j => j.Id == id));
        Assert.False(await db.PositionReviewChannels.AnyAsync(c => c.Id == channel));
    }

    private sealed class FixtureEngine(Func<Task>? duringSearch = null) : IChessEngine
    {
        public EnginePosition? LastPosition { get; private set; }
        public async Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct)
        {
            LastPosition = position;
            if (duringSearch is not null) await duringSearch();
            var moves = ChessRules.View(ChessRules.Replay(position.InitialFen, position.History)).LegalMoves.Take(3).ToArray();
            return new("Fixture engine (test double)", "{}", moves[0], moves.Select((move, rank) =>
                new SearchLine(rank + 1, 10, 1000, 5, new("cp", 0), [move], false)).ToArray(), 5, 1000, true);
        }
    }
}
