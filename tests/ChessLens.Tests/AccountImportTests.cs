using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Imports;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class AccountImportTests(DatabaseFixture fixture)
{
    private async Task<HttpClient> Account()
    {
        var client = fixture.CreateClient();
        await Csrf(client);
        var result = await client.PostAsJsonAsync("/api/account/register", new { email = $"{Guid.NewGuid():N}@test.example", password = "ChessLens.Test2026!" });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        await Csrf(client);
        return client;
    }

    private static async Task Csrf(HttpClient client)
    {
        var json = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", json.GetProperty("token").GetString());
    }

    private async Task ProcessAll()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        var processor = new ImportProcessor(db, new PgnReader());
        while (await processor.RunNext(CancellationToken.None)) { }
    }

    private static async Task<Guid> Import(HttpClient client, string pgn = PgnTests.Game)
    {
        var res = await client.PostAsJsonAsync("/api/imports/pgn", new PgnImportRequest(pgn));
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var job = await res.Content.ReadFromJsonAsync<JsonElement>();
        return job.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Cookie_authentication_requires_csrf_and_protects_routes()
    {
        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/games")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/account/register", new { email = "csrf@test.example", password = "ChessLens.Test2026!" })).StatusCode);
        using var client = await Account();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/account/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/account/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/games")).StatusCode);
    }

    [Fact]
    public async Task Mixed_import_is_persistent_deduplicated_and_owner_scoped()
    {
        using var alice = await Account();
        using var bob = await Account();
        var jobId = await Import(alice, PgnTests.Game + "\n\n[White \"Malformed\"]\n1.e5 e4 0-1");
        await ProcessAll();
        var job = await alice.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("completed", job.GetProperty("state").GetString());
        Assert.Equal(1, job.GetProperty("imported").GetInt32());
        Assert.Equal(1, job.GetProperty("failures").GetArrayLength());
        var library = await alice.GetFromJsonAsync<JsonElement>("/api/games");
        Assert.Equal(1, library.GetProperty("total").GetInt32());
        var gameId = library.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.Null, library.GetProperty("items")[0].GetProperty("playerColour").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/games/{gameId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/jobs/{jobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/games/{gameId}/position", new { ply = 0, variation = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/games/{gameId}/identity", new { colour = "black" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/jobs/{jobId}/cancel", null)).StatusCode);
        var secondId = await Import(alice);
        await ProcessAll();
        var second = await alice.GetFromJsonAsync<JsonElement>($"/api/jobs/{secondId}");
        Assert.Equal(1, second.GetProperty("duplicates").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsJsonAsync($"/api/games/{gameId}/identity", new { colour = "white" })).StatusCode);
        var valid = await alice.PostAsJsonAsync($"/api/games/{gameId}/position", new { ply = 0, variation = new[] { "e2e4" } });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/games/{gameId}/position", new { ply = 0, variation = new[] { "e2e5" } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync("/api/account/data")).StatusCode);
        var after = await alice.GetFromJsonAsync<JsonElement>("/api/games");
        Assert.Equal(0, after.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/jobs/{jobId}")).StatusCode);
    }

    [Fact]
    public async Task Interrupted_import_recovers_and_repeated_execution_keeps_one_association()
    {
        using var client = await Account();
        var id = await Import(client);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            await db.ImportJobs.Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "running")
                .SetProperty(j => j.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1)).SetProperty(j => j.LeaseToken, "dead-worker"));
        }
        await ProcessAll();
        await ProcessAll();
        var library = await client.GetFromJsonAsync<JsonElement>("/api/games");
        Assert.Equal(1, library.GetProperty("total").GetInt32());
        var job = await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{id}");
        Assert.Equal("completed", job.GetProperty("state").GetString());
        Assert.True(job.GetProperty("version").GetInt64() >= 3);
    }

    [Fact]
    public async Task Demo_workspaces_are_independent_and_marked_as_demo()
    {
        using var first = fixture.CreateClient();
        using var second = fixture.CreateClient();
        await Csrf(first); await Csrf(second);
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsync("/api/account/demo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsync("/api/account/demo", null)).StatusCode);
        var a = await first.GetFromJsonAsync<JsonElement>("/api/account/me");
        var b = await second.GetFromJsonAsync<JsonElement>("/api/account/me");
        Assert.True(a.GetProperty("isDemo").GetBoolean());
        Assert.NotEqual(a.GetProperty("id").GetString(), b.GetProperty("id").GetString());
        await ProcessAll();
        var games = await first.GetFromJsonAsync<JsonElement>("/api/games");
        Assert.Equal(2, games.GetProperty("total").GetInt32());
        await Csrf(first);
        Assert.Equal(HttpStatusCode.NoContent, (await first.DeleteAsync("/api/account/data")).StatusCode);
        var otherGames = await second.GetFromJsonAsync<JsonElement>("/api/games");
        Assert.Equal(2, otherGames.GetProperty("total").GetInt32());
    }
}
