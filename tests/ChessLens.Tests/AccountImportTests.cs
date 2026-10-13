using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Imports;
using ChessLens.Core.Games;
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
    public async Task Shared_game_deduplication_preserves_each_owners_first_private_annotations_and_clocks()
    {
        using var alice = await Account(); using var bob = await Account();
        var header = $"[Event \"Original isolation fixture {Guid.NewGuid()}\"]\n[White \"Learner\"]\n[Black \"Opponent\"]\n[TimeControl \"300+0\"]\n";
        var a = header + "[Annotator \"Alice private annotator\"]\n1.e4 {Alice private comment [%clk 0:04:58]} e5 2.Nf3 Nc6 1/2-1/2";
        var b = header + "[Annotator \"Bob private annotator\"]\n1.e4 {Bob own comment [%clk 0:04:41]} e5 2.Nf3 Nc6 1/2-1/2";
        await Import(alice, a); await ProcessAll(); await Import(bob, b); await ProcessAll();
        var id = (await alice.GetFromJsonAsync<JsonElement>("/api/games")).GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(id, (await bob.GetFromJsonAsync<JsonElement>("/api/games")).GetProperty("items")[0].GetProperty("id").GetGuid());
        async Task Check(HttpClient client, string own, string other, double clock)
        {
            var view = await client.GetFromJsonAsync<JsonElement>($"/api/games/{id}"); var raw = view.GetProperty("rawPgn").GetString()!;
            Assert.Contains(own, raw); Assert.DoesNotContain(other, raw); Assert.Equal(clock, view.GetProperty("moves")[0].GetProperty("clockSeconds").GetDouble());
            Assert.Equal("owned", view.GetProperty("annotationState").GetString());
        }
        await Check(alice, "Alice private comment", "Bob own comment", 298); await Check(bob, "Bob own comment", "Alice private", 281);
        string canonical;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); var source = await db.Games.Include(g => g.Moves).SingleAsync(g => g.Id == id);
            canonical = source.RawPgn; Assert.DoesNotContain("private", canonical); Assert.DoesNotContain("Annotator", source.HeadersJson);
            Assert.All(source.Moves, move => Assert.Null(move.ClockSeconds)); Assert.Empty(await db.LegacyPgnAnnotations.Where(p => p.GameId == id).ToArrayAsync());
        }
        await Import(alice, a.Replace("Alice private comment", "Changed later annotation")); await ProcessAll();
        await Check(alice, "Alice private comment", "Changed later annotation", 298);
        (await alice.DeleteAsync("/api/account/data")).EnsureSuccessStatusCode(); await Check(bob, "Bob own comment", "Alice private", 281);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); Assert.Equal(canonical, (await db.Games.SingleAsync(g => g.Id == id)).RawPgn);
            Assert.Single(await db.UserGames.Where(g => g.GameId == id).ToArrayAsync());
        }
        (await bob.DeleteAsync("/api/account/data")).EnsureSuccessStatusCode();
        await using var final = fixture.Services.CreateAsyncScope(); Assert.False(await final.ServiceProvider.GetRequiredService<LensDbContext>().Games.AnyAsync(g => g.Id == id));
    }

    [Fact]
    public async Task Unattributed_legacy_annotations_are_withheld_restored_only_from_own_reimport_and_removed_on_data_deletion()
    {
        using var alice = await Account(); using var bob = await Account();
        var owner = (await alice.GetFromJsonAsync<JsonElement>("/api/account/me")).GetProperty("id").GetString()!;
        var other = (await bob.GetFromJsonAsync<JsonElement>("/api/account/me")).GetProperty("id").GetString()!;
        var raw = $"[Event \"Original legacy isolation fixture {Guid.NewGuid()}\"]\n[White \"Learner\"]\n[Black \"Opponent\"]\n1.e4 {{Unattributed private legacy comment [%clk 0:04:58]}} e5 1/2-1/2";
        var source = new PgnReader().Parse(raw); source.RawPgn = PgnAnnotations.Canonical(source); foreach (var move in source.Moves) move.ClockSeconds = null;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); db.Games.Add(source);
            db.UserGames.AddRange(new UserGame { OwnerId = owner, GameId = source.Id }, new UserGame { OwnerId = other, GameId = source.Id });
            db.LegacyPgnAnnotations.Add(new() { GameId = source.Id, RawPgn = raw }); await db.SaveChangesAsync();
        }
        foreach (var client in new[] { alice, bob })
        {
            var view = await client.GetFromJsonAsync<JsonElement>($"/api/games/{source.Id}");
            Assert.DoesNotContain("private legacy", view.GetProperty("rawPgn").GetString()); Assert.Equal("unattributed", view.GetProperty("annotationState").GetString());
            Assert.All(view.GetProperty("moves").EnumerateArray(), m => Assert.Equal(JsonValueKind.Null, m.GetProperty("clockSeconds").ValueKind));
        }
        var importId = await Import(alice, raw); await ProcessAll();
        Assert.Equal(1, (await alice.GetFromJsonAsync<JsonElement>($"/api/jobs/{importId}")).GetProperty("duplicates").GetInt32());
        Assert.Contains("private legacy", (await alice.GetFromJsonAsync<JsonElement>($"/api/games/{source.Id}")).GetProperty("rawPgn").GetString());
        Assert.DoesNotContain("private legacy", (await bob.GetFromJsonAsync<JsonElement>($"/api/games/{source.Id}")).GetProperty("rawPgn").GetString());
        (await bob.DeleteAsync("/api/account/data")).EnsureSuccessStatusCode();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); Assert.False(await db.LegacyPgnAnnotations.AnyAsync(p => p.GameId == source.Id));
            Assert.Equal(source.RawPgn, (await db.Games.SingleAsync(g => g.Id == source.Id)).RawPgn);
            Assert.Contains("private legacy", (await db.UserGames.SingleAsync(g => g.GameId == source.Id)).OriginalPgn);
        }
        await alice.DeleteAsync("/api/account/data");
    }

    [Fact]
    public async Task Pre_upgrade_partial_import_restores_committed_owner_annotations_without_duplicate_counts_or_new_source_ids()
    {
        using var alice = await Account(); var owner = (await alice.GetFromJsonAsync<JsonElement>("/api/account/me")).GetProperty("id").GetString()!;
        var first = $"[Event \"Original partial annotation fixture {Guid.NewGuid()}\"]\n[White \"Learner\"]\n[Black \"Opponent\"]\n1.e4 {{Own committed note [%clk 0:04:41]}} e5 1/2-1/2";
        var second = $"[Event \"Original next annotation fixture {Guid.NewGuid()}\"]\n[White \"Learner\"]\n[Black \"Opponent\"]\n1.d4 d5 1/2-1/2";
        var game = new PgnReader().Parse(first); game.RawPgn = PgnAnnotations.Canonical(game); foreach (var move in game.Moves) move.ClockSeconds = null;
        var job = new ImportJob { OwnerId = owner, PayloadJson = JsonSerializer.Serialize(new PgnImportRequest(first + "\n\n" + second)), State = "running",
            Imported = 1, Processed = 1, LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-1) };
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); db.Games.Add(game); db.UserGames.Add(new() { OwnerId = owner, GameId = game.Id }); db.ImportJobs.Add(job); await db.SaveChangesAsync();
        }
        await ProcessAll(); var saved = await alice.GetFromJsonAsync<JsonElement>($"/api/jobs/{job.Id}");
        Assert.Equal("completed", saved.GetProperty("state").GetString()); Assert.Equal(2, saved.GetProperty("imported").GetInt32()); Assert.Equal(0, saved.GetProperty("duplicates").GetInt32());
        var view = await alice.GetFromJsonAsync<JsonElement>($"/api/games/{game.Id}"); Assert.Contains("Own committed note", view.GetProperty("rawPgn").GetString());
        Assert.Equal(281, view.GetProperty("moves")[0].GetProperty("clockSeconds").GetDouble());
        Assert.Equal(2, (await alice.GetFromJsonAsync<JsonElement>("/api/games")).GetProperty("total").GetInt32());
        await alice.DeleteAsync("/api/account/data");
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
