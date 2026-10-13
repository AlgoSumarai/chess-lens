using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Coaching;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class CoachingPipelineTests(DatabaseFixture fixture)
{
    private async Task<HttpClient> Account()
    {
        var client = fixture.CreateClient(); await Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/account/register", new { email = $"coach-{Guid.NewGuid():N}@test.example", password = "ChessLens.Test2026!" })).StatusCode);
        await Csrf(client); return client;
    }
    private static async Task Csrf(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("token").GetString());
    }
    private async Task<(Guid Run, Guid Game, string Owner)> Seed(HttpClient client)
    {
        var owner = (await client.GetFromJsonAsync<JsonElement>("/api/account/me")).GetProperty("id").GetString()!;
        var source = InsightRulesTests.TacticalFixture(); source.Run.OwnerId = owner;
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        db.Games.Add(source.Game); db.UserGames.Add(new() { OwnerId = owner, GameId = source.Game.Id, PlayerColour = "white" });
        db.AnalysisRuns.Add(source.Run); await db.SaveChangesAsync(); return (source.Run.Id, source.Game.Id, owner);
    }
    private async Task<Guid> Queued((Guid Run, Guid Game, string Owner) source)
    {
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        var run = await db.AnalysisRuns.Include(r => r.Moves).SingleAsync(r => r.Id == source.Run);
        var game = await db.Games.Include(g => g.Moves).SingleAsync(g => g.Id == source.Game);
        var evidence = CoachingRules.Build(game, run, run.Moves.Single(m => m.Ply == 1), "beginner");
        var job = new CoachingJob { OwnerId = source.Owner, GameId = source.Game, RunId = source.Run, Ply = 1, CacheIdentity = Guid.NewGuid().ToString("N"),
            EvidenceJson = JsonSerializer.Serialize(evidence, CoachingRules.Json), Model = "fixture-model", ModelRequested = true };
        db.CoachingJobs.Add(job); await db.SaveChangesAsync(); return job.Id;
    }
    private async Task Tick(ICoachingModel model, CoachingOptions? options = null, CancellationToken ct = default)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.True(await new CoachingProcessor(scope.ServiceProvider.GetRequiredService<LensDbContext>(), model, Options.Create(options ?? CoachingRulesTests.Options())).RunNext(ct));
    }
    private sealed class FixtureModel(Func<CoachingModelRequest, CancellationToken, Task<string?>> answer, bool configured = true) : ICoachingModel
    {
        public bool Configured => configured;
        public string Name => "fixture-model";
        public int Calls { get; private set; }
        public async Task<CoachingModelReply?> Explain(CoachingModelRequest request, CancellationToken ct)
        { Calls++; var json = await answer(request, ct); return json is null ? null : new(json, "fixture-returned-model-version", "labelled-model-double", "fixture-response-id"); }
    }

    [Fact]
    public async Task No_key_coaching_is_cached_level_aware_owner_scoped_and_frozen()
    {
        using var alice = await Account(); using var bob = await Account(); var source = await Seed(alice);
        var response = await alice.PostAsJsonAsync("/api/coaching", new { runId = source.Run, ply = 1 });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<JsonElement>(); var id = job.GetProperty("id").GetGuid();
        Assert.Equal("completed", job.GetProperty("state").GetString()); Assert.Equal("factual", job.GetProperty("result").GetProperty("mode").GetString());
        var again = await (await alice.PostAsJsonAsync("/api/coaching", new { runId = source.Run, ply = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, again.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/coaching/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync("/api/coaching", new { runId = source.Run, ply = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/coaching/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/coaching", new { runId = source.Run, ply = 3 })).StatusCode); // Provisional fixture.
        Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync("/api/account/preferences", new { timezone = "Africa/Johannesburg", coachingLevel = "advanced", preferredTimeControls = "blitz", useModelCoaching = true })).StatusCode);
        var changed = await (await alice.PostAsJsonAsync("/api/coaching", new { runId = source.Run, ply = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(id, changed.GetProperty("id").GetGuid()); Assert.Equal("advanced", changed.GetProperty("result").GetProperty("level").GetString());
        Assert.Equal("beginner", (await alice.GetFromJsonAsync<JsonElement>($"/api/coaching/{id}")).GetProperty("result").GetProperty("level").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync("/api/account/data")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/coaching/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("valid", "model-selected", null)]
    [InlineData("illegal", "factual", "unsupported-model-output")]
    [InlineData("failure", "factual", "provider-failure")]
    [InlineData("timeout", "factual", "provider-timeout")]
    [InlineData("no-key", "factual", "provider-unavailable")]
    public async Task Optional_model_outcomes_are_validated_or_replaced_by_useful_factual_coaching(string outcome, string mode, string? reason)
    {
        using var client = await Account(); var source = await Seed(client); var id = await Queued(source);
        var model = new FixtureModel(async (request, ct) =>
        {
            if (outcome == "failure") throw new HttpRequestException("Labelled provider-failure fixture");
            if (outcome == "timeout") { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return null; }
            return outcome == "illegal" ? CoachingRulesTests.Plan(request.Evidence).Replace("e4d5", "e1e8", StringComparison.Ordinal) : CoachingRulesTests.Plan(request.Evidence);
        }, outcome != "no-key");
        var options = CoachingRulesTests.Options(); options.TimeoutSeconds = 1; await Tick(model, options);
        var result = (await client.GetFromJsonAsync<JsonElement>($"/api/coaching/{id}")).GetProperty("result");
        Assert.Equal(mode, result.GetProperty("mode").GetString()); Assert.Equal(reason, result.GetProperty("fallbackReason").GetString());
        if (outcome == "valid") { Assert.Equal("fixture-returned-model-version", result.GetProperty("model").GetString()); Assert.Equal("labelled-model-double", result.GetProperty("modelProvider").GetString()); }
        Assert.NotEmpty(result.GetProperty("lesson").GetString()!); Assert.Equal("e4d5", result.GetProperty("betterLine")[0].GetString());
        Assert.Equal(outcome == "no-key" ? 0 : 1, model.Calls);
    }

    [Fact]
    public async Task Reserved_cost_and_request_budgets_survive_imported_data_deletion()
    {
        using var client = await Account(); var source = await Seed(client); var first = await Queued(source);
        var model = new FixtureModel((request, _) => Task.FromResult<string?>(CoachingRulesTests.Plan(request.Evidence)));
        var options = CoachingRulesTests.Options(); options.DailyOwnerRequests = 1; await Tick(model, options); Assert.Equal(1, model.Calls);
        await client.DeleteAsync("/api/account/data");
        var secondSource = await Seed(client); var second = await Queued(secondSource); await Tick(model, options);
        Assert.Equal(1, model.Calls);
        Assert.Equal("model-budget-reached", (await client.GetFromJsonAsync<JsonElement>($"/api/coaching/{second}")).GetProperty("result").GetProperty("fallbackReason").GetString());
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        var usage = await db.CoachingUsages.SingleAsync(u => u.OwnerId == source.Owner);
        Assert.Null(usage.JobId); Assert.Equal(33_280, usage.ReservedMicroDollars); Assert.Equal(32_768, usage.MaximumInputBytes);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/coaching/{first}")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_or_deletion_during_a_model_request_cannot_publish_a_late_explanation(bool delete)
    {
        using var client = await Account(); var source = await Seed(client); var id = await Queued(source);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new FixtureModel(async (request, _) => { entered.SetResult(); await release.Task; return CoachingRulesTests.Plan(request.Evidence); });
        var pending = Tick(model); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (delete) Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/account/data")).StatusCode);
        else Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/coaching/{id}/cancel", null)).StatusCode);
        release.SetResult(); await pending;
        if (delete) Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/coaching/{id}")).StatusCode);
        else
        {
            var result = await client.GetFromJsonAsync<JsonElement>($"/api/coaching/{id}"); Assert.Equal("cancelled", result.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("result").ValueKind);
        }
    }

    [Fact]
    public async Task Interrupted_model_attempt_recovers_to_facts_without_repeating_paid_work()
    {
        using var client = await Account(); var source = await Seed(client); var id = await Queued(source);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            // Labelled durable-recovery fixture, not an OS-level termination test.
            await db.CoachingJobs.Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "running")
                .SetProperty(j => j.ModelAttempted, true).SetProperty(j => j.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
            db.CoachingUsages.Add(new() { OwnerId = source.Owner, JobId = id, ReservedMicroDollars = 33_280, MaximumInputBytes = 32_768, MaximumOutputTokens = 512 });
            await db.SaveChangesAsync();
        }
        var model = new FixtureModel((_, _) => throw new InvalidOperationException("A previously attempted request must not be sent twice."));
        await Tick(model); Assert.Equal(0, model.Calls);
        var result = await client.GetFromJsonAsync<JsonElement>($"/api/coaching/{id}"); Assert.Equal("completed", result.GetProperty("state").GetString());
        Assert.Equal("interrupted-model-request", result.GetProperty("result").GetProperty("fallbackReason").GetString());
    }
}
