using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Persistence;
using ChessLens.Core.Training;
using ChessLens.Core.Insights;
using ChessLens.Core.Analysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class PracticePipelineTests(DatabaseFixture fixture)
{
    private async Task<(HttpClient Client, string Owner, Guid Run)> Account(bool seed = true)
    {
        var client = fixture.CreateClient();
        async Task Csrf()
        {
            var token = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        }
        await Csrf(); (await client.PostAsJsonAsync("/api/account/register", new { email = $"practice-{Guid.NewGuid():N}@test.example", password = "ChessLens.Practice2026!" })).EnsureSuccessStatusCode();
        await Csrf(); var owner = (await client.GetFromJsonAsync<JsonElement>("/api/account/me")).GetProperty("id").GetString()!;
        return (client, owner, seed ? await Seed(owner) : Guid.Empty);
    }
    private async Task<Guid> Seed(string owner)
    {
        var source = InsightRulesTests.TacticalFixture(); source.Run.OwnerId = owner;
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        db.Games.Add(source.Game); db.UserGames.Add(new() { OwnerId = owner, GameId = source.Game.Id, PlayerColour = "white" }); db.AnalysisRuns.Add(source.Run);
        await db.SaveChangesAsync(); return source.Run.Id;
    }
    private static async Task<JsonElement> Post(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static object Command(JsonElement session, string? move = null) => new { version = session.GetProperty("version").GetInt64(), attemptId = session.GetProperty("attempt").GetProperty("id").GetGuid(), move };
    private static Guid Id(JsonElement session) => session.GetProperty("id").GetGuid();
    private static async Task<JsonElement> Get(HttpClient client, Guid id) => await client.GetFromJsonAsync<JsonElement>($"/api/practice/{id}");
    private async Task Tick(PracticeRulesTests.FixtureEngine? engine = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.True(await new PracticeProcessor(scope.ServiceProvider.GetRequiredService<LensDbContext>(), engine ?? new(score: 600)).RunNext(CancellationToken.None));
    }

    [Fact]
    public async Task Saved_sessions_hide_answers_accept_alternatives_resume_undo_hints_retry_and_keep_finished_attempts()
    {
        var (client, owner, run) = await Account(); using var alice = client;
        var session = await Post(alice, "/api/practice", new { runId = run, ply = 1 }); var id = Id(session);
        Assert.Equal("preparing", session.GetProperty("state").GetString()); Assert.Equal(JsonValueKind.Null, session.GetProperty("attempt").GetProperty("review").ValueKind);
        Assert.Equal(id, Id(await Post(alice, "/api/practice", new { runId = run, ply = 1 })));
        await Tick(); session = await Get(alice, id); Assert.Equal("active", session.GetProperty("state").GetString());
        Assert.Equal("preserve-advantage", session.GetProperty("objective").GetProperty("kind").GetString());
        Assert.DoesNotContain("bestSearch", session.GetRawText()); Assert.DoesNotContain("bestMove", session.GetRawText());
        var stale = Command(session);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/practice/{id}/moves", Command(session, "e8e7"))).StatusCode);
        session = await Post(alice, $"/api/practice/{id}/moves", Command(session, "e4d5"));
        Assert.Equal("waiting", session.GetProperty("state").GetString()); Assert.Equal("e4d5", (await Get(alice, id)).GetProperty("attempt").GetProperty("history")[0].GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsJsonAsync($"/api/practice/{id}/hint", stale)).StatusCode);
        // Labelled expired durable lease: resume the same job, preserving the submitted move.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            await db.PracticeJobs.Where(j => j.SessionId == id && j.State == "queued").ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "running").SetProperty(j => j.Attempts, 1).SetProperty(j => j.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        await Tick(); session = await Get(alice, id); Assert.Equal(2, session.GetProperty("attempt").GetProperty("history").GetArrayLength());
        session = await Post(alice, $"/api/practice/{id}/hint", Command(session)); await Tick(); session = await Get(alice, id);
        Assert.NotNull(session.GetProperty("attempt").GetProperty("hint").GetString());
        session = await Post(alice, $"/api/practice/{id}/undo", Command(session));
        Assert.Equal(0, session.GetProperty("attempt").GetProperty("history").GetArrayLength()); Assert.True(session.GetProperty("attempt").GetProperty("assisted").GetBoolean());
        for (var decision = 1; decision <= 3; decision++)
        {
            var move = session.GetProperty("attempt").GetProperty("legalMoves")[0].GetString();
            session = await Post(alice, $"/api/practice/{id}/moves", Command(session, decision == 1 ? "e4d5" : move)); await Tick(); session = await Get(alice, id);
        }
        Assert.Equal("completed", session.GetProperty("state").GetString()); var attempt = session.GetProperty("attempt"); var firstId = attempt.GetProperty("id").GetGuid();
        Assert.Equal("succeeded", attempt.GetProperty("state").GetString()); Assert.True(attempt.GetProperty("assisted").GetBoolean());
        Assert.Equal(3, attempt.GetProperty("review").GetProperty("grades").GetArrayLength());
        Assert.Equal(4, attempt.GetProperty("review").GetProperty("allGrades").GetArrayLength());
        var frozen = (await alice.GetFromJsonAsync<JsonElement>($"/api/practice/{id}/attempts/{firstId}")).GetRawText();
        session = await Post(alice, $"/api/practice/{id}/retry", Command(session)); Assert.NotEqual(firstId, session.GetProperty("attempt").GetProperty("id").GetGuid());
        Assert.Equal(frozen, (await alice.GetFromJsonAsync<JsonElement>($"/api/practice/{id}/attempts/{firstId}")).GetRawText());
        Assert.False(session.GetProperty("attempt").GetProperty("assisted").GetBoolean());
        await Tick(new(score: 0)); session = await Get(alice, id);
        Assert.Equal("decision-safety", session.GetProperty("attempt").GetProperty("objective").GetProperty("kind").GetString());
        Assert.Equal(frozen, (await alice.GetFromJsonAsync<JsonElement>($"/api/practice/{id}/attempts/{firstId}")).GetRawText());
        (await alice.DeleteAsync("/api/account/data")).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/practice/{id}")).StatusCode);
        await using var finalScope = fixture.Services.CreateAsyncScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<LensDbContext>();
        Assert.Empty(await finalDb.PracticeJobs.Where(j => j.SessionId == id).ToListAsync());
        Assert.True(await finalDb.ResourceUsages.AnyAsync(u => u.OwnerId == owner));
    }

    [Fact]
    public async Task Supporting_weakness_links_require_exact_owned_snapshot_evidence_and_are_saved_for_review()
    {
        var (client, owner, runId) = await Account(); using var alice = client;
        Guid snapshotId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            var run = await db.AnalysisRuns.Include(r => r.Moves).SingleAsync(r => r.Id == runId);
            var game = await db.Games.Include(g => g.Moves).SingleAsync(g => g.Id == run.GameId);
            var filter = new InsightFilter(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow); var options = new InsightOptions();
            var summary = InsightRules.Build([new(game, "white", run)], options); Assert.NotEmpty(summary.Findings);
            var result = new InsightResult(filter, options, 1, 1, 0, 0, 0, false, [new("Labelled snapshot", "quick", run.AnalysisVersion, new AnalysisOptions(), [run.Id], summary)]);
            var snapshot = new InsightSnapshot { OwnerId = owner, State = "completed", FilterJson = JsonSerializer.Serialize(filter), OptionsJson = JsonSerializer.Serialize(options), ResultJson = JsonSerializer.Serialize(result) };
            snapshotId = snapshot.Id; db.InsightSnapshots.Add(snapshot); await db.SaveChangesAsync();
        }
        var unrelatedRun = await Seed(owner);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/practice", new { runId = unrelatedRun, ply = 1, weaknessSnapshotId = snapshotId })).StatusCode);
        var session = await Post(alice, "/api/practice", new { runId, ply = 1, weaknessSnapshotId = snapshotId }); var id = Id(session);
        session = await Post(alice, $"/api/practice/{id}/cancel", Command(session));
        Assert.Equal(snapshotId, session.GetProperty("attempt").GetProperty("review").GetProperty("weaknessSnapshotId").GetGuid());
        await alice.DeleteAsync("/api/account/data");
    }

    [Fact]
    public async Task Unstable_relative_loss_retries_with_a_distinct_deeper_budget_and_does_not_reuse_the_failed_pair()
    {
        var (client, _, run) = await Account(); using var alice = client;
        var session = await Post(alice, "/api/practice", new { runId = run, ply = 1 }); var id = Id(session);
        await Tick(); session = await Get(alice, id); await Post(alice, $"/api/practice/{id}/moves", Command(session, "e4d5"));
        await Tick(new(mode: "unstable", score: 600)); Assert.Equal("waiting", (await Get(alice, id)).GetProperty("state").GetString());
        await Tick(new(mode: "unstable", score: 600)); session = await Get(alice, id); Assert.Equal("active", session.GetProperty("state").GetString());
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); var attempt = await db.PracticeAttempts.SingleAsync(a => a.SessionId == id);
            var grade = PracticeProcessor.Read<PracticeGrade[]>(attempt.GradesJson).Single(); Assert.Equal(500_000, grade.Best.First.Budget.Nodes); Assert.Equal(1_500_000, grade.Played.Final.Budget.Nodes);
            Assert.Equal(2, (await db.PracticeJobs.SingleAsync(j => j.SessionId == id && j.Kind == "turn")).Attempts);
        }
        await alice.DeleteAsync("/api/account/data");
    }

    [Fact]
    public async Task Earlier_rule_jobs_are_ungraded_without_engine_work_and_can_start_a_distinct_current_version_session()
    {
        var (client, _, run) = await Account(); using var alice = client;
        var session = await Post(alice, "/api/practice", new { runId = run, ply = 1 }); var id = Id(session);
        await using (var scope = fixture.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LensDbContext>().PracticeSessions.Where(s => s.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(s => s.RulesVersion, "chesslens-practice-v1"));
        var engine = new PracticeRulesTests.FixtureEngine(); await Tick(engine); Assert.Empty(engine.Calls);
        session = await Get(alice, id); Assert.Equal("ungraded", session.GetProperty("attempt").GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsJsonAsync($"/api/practice/{id}/retry", Command(session))).StatusCode);
        var current = await Post(alice, "/api/practice", new { runId = run, ply = 1 }); Assert.NotEqual(id, Id(current));
        Assert.Equal(PracticeRules.Version, current.GetProperty("rulesVersion").GetString());
        Assert.Equal("chesslens-practice-v1", (await Get(alice, id)).GetProperty("rulesVersion").GetString()); await alice.DeleteAsync("/api/account/data");
    }

    [Fact]
    public async Task Owner_csrf_verified_identity_and_concurrent_duplicate_commands_are_enforced()
    {
        var (client, _, run) = await Account(); using var alice = client;
        var (other, _, _) = await Account(false); using var bob = other;
        var creations = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Post(alice, "/api/practice", new { runId = run, ply = 1 })));
        var id = Id(creations[0]); Assert.All(creations, s => Assert.Equal(id, Id(s)));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/practice/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync("/api/practice", new { runId = run, ply = 1 })).StatusCode);
        foreach (var action in new[] { "moves", "hint", "undo", "retry", "cancel" })
            Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/practice/{id}/{action}", Command(creations[0]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/practice", new { runId = run, ply = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostAsJsonAsync("/api/practice", new { runId = run, ply = 1, weaknessSnapshotId = Guid.NewGuid() })).StatusCode); // Existing session retains its original optional link.
        var secondRun = await Seed((await alice.GetFromJsonAsync<JsonElement>("/api/account/me")).GetProperty("id").GetString()!);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync("/api/practice", new { runId = secondRun, ply = 1, weaknessSnapshotId = Guid.NewGuid() })).StatusCode);
        await Tick(); var session = await Get(alice, id);
        var commands = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => alice.PostAsJsonAsync($"/api/practice/{id}/moves", Command(session, "e4d5"))));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, commands.Select(c => c.StatusCode).Order().ToArray());
        alice.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync($"/api/practice/{id}/cancel", Command(await Get(alice, id)))).StatusCode);
        // Read-only ownership does not depend on a CSRF token; clean up through the database fixture.
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        await db.PracticeSessions.Where(s => s.Id == id).ExecuteDeleteAsync();
    }

    [Theory]
    [InlineData("undo")]
    [InlineData("cancel")]
    [InlineData("retry")]
    [InlineData("delete")]
    public async Task Late_engine_results_cannot_revive_stale_cancelled_retried_or_deleted_work(string action)
    {
        var (client, _, run) = await Account(); using var alice = client;
        var session = await Post(alice, "/api/practice", new { runId = run, ply = 1 }); var id = Id(session);
        await Tick(); session = await Get(alice, id); session = await Post(alice, $"/api/practice/{id}/moves", Command(session, "e4d5"));
        var command = Command(session);
        await Tick(new(score: 600, callback: async () =>
        {
            if (action == "delete") (await alice.DeleteAsync("/api/account/data")).EnsureSuccessStatusCode();
            else await Post(alice, $"/api/practice/{id}/{action}", command);
        }));
        if (action == "delete") Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/practice/{id}")).StatusCode);
        else
        {
            session = await Get(alice, id); Assert.Equal(action == "undo" ? "active" : action == "cancel" ? "cancelled" : "preparing", session.GetProperty("state").GetString());
            Assert.Equal(action == "cancel" ? 1 : 0, session.GetProperty("attempt").GetProperty("history").GetArrayLength());
            await alice.DeleteAsync("/api/account/data");
        }
    }

    [Fact]
    public async Task Three_incomplete_executions_or_exhausted_leases_end_ungraded_and_quotas_survive_deletion()
    {
        var (client, owner, run) = await Account(); using var alice = client;
        var session = await Post(alice, "/api/practice", new { runId = run, ply = 1 }); var id = Id(session);
        foreach (var expected in new[] { "preparing", "preparing", "failed" })
        { await Tick(new(mode: "incomplete")); Assert.Equal(expected, (await Get(alice, id)).GetProperty("state").GetString()); }
        session = await Get(alice, id); Assert.Equal("ungraded", session.GetProperty("attempt").GetProperty("state").GetString());
        Assert.Empty(session.GetProperty("attempt").GetProperty("review").GetProperty("grades").EnumerateArray());
        session = await Post(alice, $"/api/practice/{id}/retry", Command(session));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            await db.PracticeJobs.Where(j => j.SessionId == id && j.State == "queued").ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "running").SetProperty(j => j.Attempts, 3).SetProperty(j => j.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        await Tick(); Assert.Equal("ungraded", (await Get(alice, id)).GetProperty("attempt").GetProperty("state").GetString());
        await alice.DeleteAsync("/api/account/data"); run = await Seed(owner);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); db.ResourceUsages.Add(new() { OwnerId = owner, Kind = "practice-session", Units = 40 }); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await alice.PostAsJsonAsync("/api/practice", new { runId = run, ply = 1 })).StatusCode);
        await alice.DeleteAsync("/api/account/data");
    }
}
