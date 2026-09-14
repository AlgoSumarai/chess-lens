using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Games;
using ChessLens.Core.Insights;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class InsightSnapshotTests(DatabaseFixture fixture)
{
    private async Task<(HttpClient Client, string Owner)> Account()
    {
        var client = fixture.CreateClient();
        async Task Csrf()
        {
            var json = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", json.GetProperty("token").GetString());
        }
        await Csrf();
        (await client.PostAsJsonAsync("/api/account/register", new { email = $"insights-{Guid.NewGuid():N}@test.example", password = "ChessLens.Insights2026!" })).EnsureSuccessStatusCode();
        await Csrf();
        var account = await client.GetFromJsonAsync<JsonElement>("/api/account/me");
        return (client, account.GetProperty("id").GetString()!);
    }
    private async Task Process()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.True(await new InsightProcessor(scope.ServiceProvider.GetRequiredService<LensDbContext>()).RunNext(CancellationToken.None));
    }
    private static InsightFilter Filter => new(new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Snapshots_are_owner_scoped_idempotent_during_work_and_remain_frozen_after_identity_changes()
    {
        var (client, owner) = await Account(); using var alice = client;
        var (foreign, _) = await Account(); using var bob = foreign;
        var inputs = new[] { InsightRulesTests.TacticalFixture(), InsightRulesTests.OpeningFixture() };
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            foreach (var input in inputs)
            {
                input.Game.PlayedAt = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
                input.Run.OwnerId = owner; input.Run.EngineVersion = "Fixture engine (test double)"; input.Run.SettingsJson = "{}";
                db.Games.Add(input.Game); db.UserGames.Add(new UserGame { OwnerId = owner, GameId = input.Game.Id, PlayerColour = "white" }); db.AnalysisRuns.Add(input.Run);
            }
            await db.SaveChangesAsync();
        }
        var response = await alice.PostAsJsonAsync("/api/insights", Filter); response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var duplicate = await alice.PostAsJsonAsync("/api/insights", Filter);
        Assert.Equal(id, (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/insights/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/insights/{id}/cancel", null)).StatusCode);
        await Process();
        var initial = await alice.GetFromJsonAsync<JsonElement>($"/api/insights/{id}");
        Assert.Equal("completed", initial.GetProperty("state").GetString());
        var summary = initial.GetProperty("result").GetProperty("groups")[0].GetProperty("summary");
        Assert.Equal(4, summary.GetProperty("eligibleMoves").GetInt32());
        Assert.Equal(2, summary.GetProperty("verifiedErrorMoves").GetInt32());
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            await db.UserGames.Where(g => g.OwnerId == owner).ExecuteUpdateAsync(s => s.SetProperty(g => g.PlayerColour, "black"));
        }
        var next = await alice.PostAsJsonAsync("/api/insights", Filter); next.EnsureSuccessStatusCode();
        var nextId = (await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.NotEqual(id, nextId); await Process();
        var updated = await alice.GetFromJsonAsync<JsonElement>($"/api/insights/{nextId}");
        Assert.Equal(0, updated.GetProperty("result").GetProperty("groups")[0].GetProperty("summary").GetProperty("verifiedErrorMoves").GetInt32());
        Assert.Equal(initial.GetProperty("result").GetRawText(), (await alice.GetFromJsonAsync<JsonElement>($"/api/insights/{id}")).GetProperty("result").GetRawText());
        (await alice.DeleteAsync("/api/account/data")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/insights/{id}")).StatusCode);
    }
}
