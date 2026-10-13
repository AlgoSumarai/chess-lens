using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Api;
using ChessLens.Core.Analysis;
using ChessLens.Core.Imports;
using ChessLens.Core.Insights;
using ChessLens.Core.Persistence;
using ChessLens.Core.Training;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class ResourceAdmissionTests(DatabaseFixture fixture)
{
    private async Task<(HttpClient Client, string Owner)> Account()
    {
        var client = fixture.CreateClient();
        async Task Csrf()
        {
            var token = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        }
        await Csrf(); (await client.PostAsJsonAsync("/api/account/register", new { email = $"resource-{Guid.NewGuid():N}@test.example", password = "ChessLens.Resource2026!" })).EnsureSuccessStatusCode();
        await Csrf(); return (client, (await client.GetFromJsonAsync<JsonElement>("/api/account/me")).GetProperty("id").GetString()!);
    }
    private async Task<Guid> Import(HttpClient client, int games = 1)
    {
        var pgn = string.Join("\n\n", Enumerable.Range(0, games).Select(_ => $"[Event \"Original quota fixture {Guid.NewGuid()}\"]\n" + PgnTests.Game));
        var response = await client.PostAsJsonAsync("/api/imports/pgn", new PgnImportRequest(pgn)); response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        while (await new ImportProcessor(db, new PgnReader()).RunNext(CancellationToken.None)) { }
        return id;
    }

    [Fact]
    public async Task Import_budget_is_atomic_and_survives_deletion_while_another_owner_remains_independent()
    {
        var (client, owner) = await Account(); using var alice = client;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); db.ResourceUsages.Add(new() { OwnerId = owner, Kind = "import", Units = 19 }); await db.SaveChangesAsync();
        }
        var accepted = await alice.PostAsJsonAsync("/api/imports/pgn", new PgnImportRequest(PgnTests.Game)); Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await alice.PostAsJsonAsync("/api/imports/pgn", new PgnImportRequest(PgnTests.Game))).StatusCode);
        (await alice.DeleteAsync("/api/account/data")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await alice.PostAsJsonAsync("/api/imports/provider", new ProviderImportRequest("lichess", "Learner", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, 1))).StatusCode);
        var (otherClient, _) = await Account(); using var bob = otherClient; await Import(bob); await bob.DeleteAsync("/api/account/data");
        await using var final = fixture.Services.CreateAsyncScope(); Assert.Equal(20, await final.ServiceProvider.GetRequiredService<LensDbContext>().ResourceUsages.Where(u => u.OwnerId == owner && u.Kind == "import").SumAsync(u => u.Units));
    }

    [Fact]
    public async Task Concurrent_review_requests_cannot_exceed_the_retained_half_move_boundary()
    {
        var (client, owner) = await Account(); using var alice = client; await Import(alice, 2);
        var ids = (await alice.GetFromJsonAsync<JsonElement>("/api/games")).GetProperty("items").EnumerateArray().Select(g => g.GetProperty("id").GetGuid()).ToArray();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); db.ResourceUsages.Add(new() { OwnerId = owner, Kind = "analysis-plies", Units = 4996 }); await db.SaveChangesAsync();
        }
        var responses = await Task.WhenAll(ids.Select(id => alice.PostAsJsonAsync($"/api/games/{id}/analysis", new AnalysisRequest())));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        await alice.DeleteAsync("/api/account/data"); await Import(alice);
        var fresh = (await alice.GetFromJsonAsync<JsonElement>("/api/games")).GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await alice.PostAsJsonAsync($"/api/games/{fresh}/analysis", new AnalysisRequest())).StatusCode);
        await alice.DeleteAsync("/api/account/data");
    }

    [Fact]
    public async Task Invalid_position_rolls_back_channel_accounting_and_exhausted_position_work_does_not_create_a_channel()
    {
        var (client, owner) = await Account(); using var alice = client; await Import(alice);
        var game = (await alice.GetFromJsonAsync<JsonElement>("/api/games")).GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/games/{game}/position-analysis", new { channelId = Guid.NewGuid(), sequence = 1, ply = 0, variation = new[] { "e2e5" } })).StatusCode);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); Assert.False(await db.PositionReviewChannels.AnyAsync(c => c.OwnerId == owner));
            Assert.False(await db.ResourceUsages.AnyAsync(u => u.OwnerId == owner && u.Kind == "position-channel"));
            db.ResourceUsages.Add(new() { OwnerId = owner, Kind = "position-review", Units = 200 }); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await alice.PostAsJsonAsync($"/api/games/{game}/position-analysis", new { channelId = Guid.NewGuid(), sequence = 1, ply = 0, variation = Array.Empty<string>() })).StatusCode);
        await using (var scope = fixture.Services.CreateAsyncScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<LensDbContext>().PositionReviewChannels.AnyAsync(c => c.OwnerId == owner));
        await alice.DeleteAsync("/api/account/data");
    }

    [Fact]
    public async Task Shared_queue_cap_serializes_different_owners_and_cancellation_remains_available()
    {
        var (foreignClient, foreign) = await Account(); using var blocker = foreignClient;
        var accounts = new List<HttpClient>(); var owners = new List<string>();
        try
        {
            for (var i = 0; i < 4; i++) { var account = await Account(); accounts.Add(account.Client); owners.Add(account.Owner); }
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); var slots = ResourceAdmission.MaximumPendingJobs - await ResourceAdmission.Pending(db, CancellationToken.None) - 2;
                // Labelled administrative queue fixtures, not provider or process evidence.
                db.ImportJobs.AddRange(Enumerable.Range(0, slots / 2).Select(_ => new ImportJob { OwnerId = foreign, PayloadJson = "{}", State = "queued" }));
                db.InsightSnapshots.AddRange(Enumerable.Range(0, slots - slots / 2).Select(_ => new InsightSnapshot { OwnerId = foreign, FilterJson = "{}", OptionsJson = "{}", State = "queued" }));
                await db.SaveChangesAsync();
            }
            var responses = await Task.WhenAll(accounts.Select(client => client.PostAsJsonAsync("/api/imports/pgn", new PgnImportRequest(PgnTests.Game))));
            Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Accepted)); Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
            await using (var scope = fixture.Services.CreateAsyncScope()) Assert.Equal(1000, await ResourceAdmission.Pending(scope.ServiceProvider.GetRequiredService<LensDbContext>(), CancellationToken.None));
            using var anonymous = fixture.CreateClient();
            var csrf = await anonymous.GetFromJsonAsync<JsonElement>("/api/account/csrf"); anonymous.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            int usersBefore;
            await using (var scope = fixture.Services.CreateAsyncScope()) usersBefore = await scope.ServiceProvider.GetRequiredService<LensDbContext>().Users.CountAsync();
            Assert.Equal(HttpStatusCode.TooManyRequests, (await anonymous.PostAsync("/api/account/demo", null)).StatusCode);
            await using (var scope = fixture.Services.CreateAsyncScope()) Assert.Equal(usersBefore, await scope.ServiceProvider.GetRequiredService<LensDbContext>().Users.CountAsync());
            var winner = Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.Accepted); var job = (await responses[winner].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.Accepted, (await accounts[winner].PostAsync($"/api/jobs/{job}/cancel", null)).StatusCode);
        }
        finally
        {
            await blocker.DeleteAsync("/api/account/data"); foreach (var client in accounts) { await client.DeleteAsync("/api/account/data"); client.Dispose(); }
        }
    }

    [Fact]
    public async Task Replacing_own_position_work_at_capacity_does_not_require_an_extra_queue_slot()
    {
        var (client, owner) = await Account(); using var alice = client; await Import(alice);
        var (other, foreign) = await Account(); using var blocker = other;
        try
        {
            var game = (await alice.GetFromJsonAsync<JsonElement>("/api/games")).GetProperty("items")[0].GetProperty("id").GetGuid();
            var channel = Guid.NewGuid();
            var first = await alice.PostAsJsonAsync($"/api/games/{game}/position-analysis", new { channelId = channel, sequence = 1, ply = 0, variation = Array.Empty<string>() });
            first.EnsureSuccessStatusCode();
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
                var slots = ResourceAdmission.MaximumPendingJobs - await ResourceAdmission.Pending(db, CancellationToken.None);
                // Administrative queue fixtures test admission only; no worker consumes these payloads.
                db.ImportJobs.AddRange(Enumerable.Range(0, slots).Select(_ => new ImportJob { OwnerId = foreign, PayloadJson = "{}", State = "queued" }));
                await db.SaveChangesAsync();
            }
            var replacement = await alice.PostAsJsonAsync($"/api/games/{game}/position-analysis", new { channelId = channel, sequence = 2, ply = 1, variation = Array.Empty<string>() });
            Assert.Equal(HttpStatusCode.Accepted, replacement.StatusCode);
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
                Assert.Equal(1000, await ResourceAdmission.Pending(db, CancellationToken.None));
                var jobs = await db.PositionAnalysisJobs.Where(j => j.OwnerId == owner).OrderBy(j => j.Sequence).ToArrayAsync();
                Assert.Equal(new[] { "cancelled", "queued" }, jobs.Select(j => j.State));
                Assert.Equal(2, await db.ResourceUsages.Where(u => u.OwnerId == owner && u.Kind == "position-review").SumAsync(u => u.Units));
            }
        }
        finally { await blocker.DeleteAsync("/api/account/data"); await alice.DeleteAsync("/api/account/data"); }
    }

    [Fact]
    public async Task Deep_batch_defers_a_running_quick_review_instead_of_reusing_its_profile()
    {
        var (client, owner) = await Account(); using var alice = client; var import = await Import(alice);
        var game = (await alice.GetFromJsonAsync<JsonElement>("/api/games")).GetProperty("items")[0].GetProperty("id").GetGuid();
        var quick = await alice.PostAsJsonAsync($"/api/games/{game}/analysis", new AnalysisRequest("quick")); quick.EnsureSuccessStatusCode();
        var quickId = (await quick.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsJsonAsync($"/api/games/{game}/analysis", new AnalysisRequest("deep"))).StatusCode);
        var batch = await alice.PostAsJsonAsync($"/api/jobs/{import}/analysis", new ImportBatchRequest("deep", 1)); batch.EnsureSuccessStatusCode();
        var row = await batch.Content.ReadFromJsonAsync<JsonElement>(); var deepId = row.GetProperty("runs")[0].GetProperty("id").GetGuid();
        Assert.NotEqual(quickId, deepId); Assert.Equal("deep", row.GetProperty("runs")[0].GetProperty("profile").GetString()); Assert.Equal(0, row.GetProperty("reusedReviews").GetInt32());
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); var admission = new ImportBatchProcessor(db); await admission.RunNext(CancellationToken.None);
            Assert.Equal("waiting", (await db.AnalysisRuns.AsNoTracking().SingleAsync(r => r.Id == deepId)).State);
            await db.AnalysisRuns.Where(r => r.Id == quickId).ExecuteUpdateAsync(s => s.SetProperty(r => r.State, "completed"));
            await admission.RunNext(CancellationToken.None); Assert.Equal("queued", (await db.AnalysisRuns.AsNoTracking().SingleAsync(r => r.Id == deepId)).State);
            Assert.Equal(8, await db.ResourceUsages.Where(u => u.OwnerId == owner && u.Kind == "analysis-plies").SumAsync(u => u.Units));
        }
        await alice.DeleteAsync("/api/account/data");
    }
}
