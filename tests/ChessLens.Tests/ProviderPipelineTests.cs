using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Api;
using ChessLens.Core.Analysis;
using ChessLens.Core.Imports;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class ProviderPipelineTests(DatabaseFixture fixture)
{
    private static long sourceId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static string NewId() => Interlocked.Increment(ref sourceId).ToString(System.Globalization.CultureInfo.InvariantCulture);
    private async Task<HttpClient> Account()
    {
        var client = fixture.CreateClient(); await Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/account/register", new { email = $"provider-{Guid.NewGuid():N}@test.example", password = "ChessLens.Test2026!" })).StatusCode);
        await Csrf(client);
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LensDbContext>().ProviderThrottles.ExecuteDeleteAsync();
        return client;
    }
    private static async Task Csrf(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }
    private static async Task<Guid> Submit(HttpClient client, string provider = "chesscom")
    {
        var result = await client.PostAsJsonAsync("/api/imports/provider", ProviderContractTests.Request with { Provider = provider });
        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private async Task<bool> Tick(IProviderHttpClient transport)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await new ProviderImportProcessor(scope.ServiceProvider.GetRequiredService<LensDbContext>(), new PgnReader(), transport).RunNext(CancellationToken.None);
    }
    private async Task Drain(IProviderHttpClient transport)
    {
        for (var i = 0; i < 40; i++) if (!await Tick(transport)) return;
        throw new InvalidOperationException("Provider fixture did not finish within the expected tick bound.");
    }
    private sealed class FixtureTransport(string month, string? lichess = null) : IProviderHttpClient
    {
        public List<Uri> Requests { get; } = [];
        public Task<string> Get(Uri endpoint, CancellationToken ct)
        {
            Requests.Add(endpoint); ProviderContracts.RequireSupportedEndpoint(endpoint);
            return Task.FromResult(endpoint.Host == "lichess.org" ? lichess ?? "" : endpoint.AbsolutePath.EndsWith("/archives", StringComparison.Ordinal)
                ? "{\"archives\":[\"https://api.chess.com/pub/player/learner/games/2026/09\"]}" : month);
        }
    }
    private sealed class CallbackTransport(Func<Uri, Task<string>> callback) : IProviderHttpClient
    { public Task<string> Get(Uri endpoint, CancellationToken ct) => callback(endpoint); }

    [Fact]
    public async Task Mixed_provider_import_and_annotation_resync_preserve_original_sources_and_unverified_profiles()
    {
        using var alice = await Account(); using var bob = await Account(); var externalId = NewId();
        Assert.Equal("null", await alice.GetStringAsync("/api/insights"));
        var first = await Submit(alice); var duplicateActive = await Submit(alice); Assert.Equal(first, duplicateActive);
        Assert.Equal("null", await alice.GetStringAsync($"/api/jobs/{first}/analysis"));
        var transport = new FixtureTransport(ProviderContractTests.ChessMonth(ProviderContractTests.ChessGame(externalId), ProviderContractTests.ChessGame(NewId(), rules: "chess960")));
        await Drain(transport);
        var saved = await alice.GetFromJsonAsync<JsonElement>($"/api/jobs/{first}");
        Assert.Equal("completed", saved.GetProperty("state").GetString()); Assert.Equal(1, saved.GetProperty("imported").GetInt32()); Assert.Equal(1, saved.GetProperty("failures").GetArrayLength());
        var library = await alice.GetFromJsonAsync<JsonElement>("/api/games"); var gameId = library.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal("null", await alice.GetStringAsync($"/api/games/{gameId}/analysis"));
        Assert.Equal("white", library.GetProperty("items")[0].GetProperty("playerColour").GetString());
        var profiles = await alice.GetFromJsonAsync<JsonElement>("/api/imports/profiles"); Assert.False(profiles[0].GetProperty("ownershipVerified").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/jobs/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/jobs/{first}/analysis", new ImportBatchRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/jobs/{first}/analysis")).StatusCode);
        var second = await Submit(alice);
        await Drain(new FixtureTransport(ProviderContractTests.ChessMonth(ProviderContractTests.ChessGame(externalId, PgnTests.Game.Replace("f3", "f3 {changed annotations}")))));
        var resync = await alice.GetFromJsonAsync<JsonElement>($"/api/jobs/{second}"); Assert.Equal(1, resync.GetProperty("duplicates").GetInt32());
        var game = await alice.GetFromJsonAsync<JsonElement>($"/api/games/{gameId}"); Assert.Equal(PgnTests.Game, game.GetProperty("rawPgn").GetString());
        Assert.Equal(1, (await alice.GetFromJsonAsync<JsonElement>("/api/games")).GetProperty("total").GetInt32());
        var conflict = await Submit(alice);
        await Drain(new FixtureTransport(ProviderContractTests.ChessMonth(ProviderContractTests.ChessGame(externalId, "1. e4 e5 0-1"))));
        Assert.Contains("different moves", (await alice.GetFromJsonAsync<JsonElement>($"/api/jobs/{conflict}")).GetProperty("failures")[0].GetProperty("message").GetString());
        Assert.Equal(PgnTests.Game, (await alice.GetFromJsonAsync<JsonElement>($"/api/games/{gameId}")).GetProperty("rawPgn").GetString());
    }

    [Fact]
    public async Task Two_workers_and_owners_delivering_the_same_source_keep_one_game_and_each_association()
    {
        using var alice = await Account(); using var bob = await Account(); var id = NewId();
        await Submit(alice); await Submit(bob);
        var transport = new FixtureTransport(ProviderContractTests.ChessMonth(ProviderContractTests.ChessGame(id)));
        await Task.WhenAll(Tick(transport), Tick(transport)); await Drain(transport);
        // A contended provider gate may have deferred one checkpoint for two seconds.
        await using (var scope = fixture.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LensDbContext>().ImportJobs.Where(j => j.State == "queued" && j.Source == "chesscom")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.NextAttemptAt, (DateTimeOffset?)null));
        await Drain(transport);
        var a = await alice.GetFromJsonAsync<JsonElement>("/api/games"); var b = await bob.GetFromJsonAsync<JsonElement>("/api/games");
        Assert.Equal(1, a.GetProperty("total").GetInt32()); Assert.Equal(1, b.GetProperty("total").GetInt32());
        Assert.Equal(a.GetProperty("items")[0].GetProperty("id").GetGuid(), b.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Saved_archive_checkpoint_resumes_without_refetching_the_archive_list()
    {
        using var client = await Account(); var id = await Submit(client);
        var first = new FixtureTransport(ProviderContractTests.ChessMonth(ProviderContractTests.ChessGame(NewId())));
        Assert.True(await Tick(first)); Assert.Single(first.Requests);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            // Labelled expired-lease fixture; actual OS/process interruption is a separate release gate.
            await db.ImportJobs.Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "running").SetProperty(j => j.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        var replacement = new CallbackTransport(endpoint =>
        {
            Assert.False(endpoint.AbsolutePath.EndsWith("/archives", StringComparison.Ordinal));
            return Task.FromResult(ProviderContractTests.ChessMonth(ProviderContractTests.ChessGame(NewId())));
        });
        await Drain(replacement);
        Assert.Equal("completed", (await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{id}")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Global_rate_limit_checkpoint_pauses_without_blocking_cancellation_or_other_work()
    {
        using var client = await Account(); var id = await Submit(client, "lichess");
        var calls = 0;
        var limit = new CallbackTransport(_ => { calls++; throw new ProviderRetryException("Fixture 429: pause", TimeSpan.FromMinutes(1)); });
        Assert.True(await Tick(limit)); Assert.False(await Tick(limit)); Assert.Equal(1, calls);
        var job = await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{id}"); Assert.Equal("queued", job.GetProperty("state").GetString());
        Assert.True(job.GetProperty("nextAttemptAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddSeconds(50));
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/jobs/{id}/cancel", null)).StatusCode);
        Assert.True(await Tick(limit)); Assert.Equal(1, calls);
        Assert.Equal("cancelled", (await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{id}")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Deleting_during_a_provider_request_discards_late_data_and_profiles()
    {
        using var client = await Account(); var id = await Submit(client, "lichess");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = Tick(new CallbackTransport(async _ => { entered.SetResult(); await finish.Task; return ProviderContractTests.LichessGame(); }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/account/data")).StatusCode);
        finish.SetResult(); await running;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/jobs/{id}")).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/games")).GetProperty("total").GetInt32());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/imports/profiles")).EnumerateArray());
    }

    [Fact]
    public async Task Imported_batch_reserves_budget_admits_three_reviews_and_recovers_idempotently()
    {
        using var client = await Account(); var id = await Submit(client);
        await Drain(new FixtureTransport(ProviderContractTests.ChessMonth(Enumerable.Range(0, 5).Select(_ => ProviderContractTests.ChessGame(NewId())).ToArray())));
        var submitted = await client.PostAsJsonAsync($"/api/jobs/{id}/analysis", new ImportBatchRequest("quick", 5));
        Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        var batch = (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(batch, (await (await client.PostAsJsonAsync($"/api/jobs/{id}/analysis", new ImportBatchRequest("quick", 5))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); var processor = new ImportBatchProcessor(db);
            Assert.True(await processor.RunNext(CancellationToken.None));
            Assert.Equal(3, await db.AnalysisRuns.CountAsync(r => r.BatchId == batch && r.State == "queued"));
            Assert.Equal(2, await db.AnalysisRuns.CountAsync(r => r.BatchId == batch && r.State == "waiting"));
            // Explicit scheduling fixture, not evidence of actual engine review.
            await db.AnalysisRuns.Where(r => r.BatchId == batch && r.State == "queued").ExecuteUpdateAsync(s => s.SetProperty(r => r.State, "completed"));
        }
        await using (var replacement = fixture.Services.CreateAsyncScope())
        {
            var db = replacement.ServiceProvider.GetRequiredService<LensDbContext>();
            Assert.True(await new ImportBatchProcessor(db).RunNext(CancellationToken.None));
            Assert.Equal(2, await db.AnalysisRuns.CountAsync(r => r.BatchId == batch && r.State == "queued"));
        }
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/analysis-batches/{batch}/cancel", null)).StatusCode);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); await new ImportBatchProcessor(db).RunNext(CancellationToken.None);
            Assert.Equal(2, await db.AnalysisRuns.CountAsync(r => r.BatchId == batch && r.State == "queued" && r.CancelRequested));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/account/data")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/analysis-batches/{batch}")).StatusCode);
    }
}
