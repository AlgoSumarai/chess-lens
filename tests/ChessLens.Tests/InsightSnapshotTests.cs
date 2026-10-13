using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Games;
using ChessLens.Core.Insights;
using ChessLens.Core.Analysis;
using ChessLens.Core.Reports;
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
    public async Task Private_clock_coverage_is_owner_scoped_in_insights_and_frozen_in_report_inputs()
    {
        var (client, owner) = await Account(); using var alice = client; var (otherClient, other) = await Account(); using var bob = otherClient;
        var input = InsightRulesTests.OpeningFixture(); var original = input.Game.RawPgn; var clocks = PgnAnnotations.Clocks(input.Game);
        input.Game.PlayedAt = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero); input.Game.RawPgn = PgnAnnotations.Canonical(input.Game);
        foreach (var move in input.Game.Moves) move.ClockSeconds = null;
        var otherRun = InsightRulesTests.OpeningFixture().Run; otherRun.GameId = input.Game.Id;
        foreach (var (run, id) in new[] { (input.Run, owner), (otherRun, other) })
        { run.OwnerId = id; run.EngineVersion = "Fixture engine (test double)"; run.SettingsJson = "{}"; run.ClassificationSettingsJson = JsonSerializer.Serialize(new AnalysisOptions()); }
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); db.Games.Add(input.Game); db.AnalysisRuns.AddRange(input.Run, otherRun);
            db.UserGames.AddRange(new UserGame { OwnerId = owner, GameId = input.Game.Id, PlayerColour = "white", OriginalPgn = original, ClockSecondsJson = clocks },
                new UserGame { OwnerId = other, GameId = input.Game.Id, PlayerColour = "white", OriginalPgn = input.Game.RawPgn, ClockSecondsJson = "[null,null,null,null]" });
            await db.SaveChangesAsync();
        }
        foreach (var (account, expected) in new[] { (alice, 2), (bob, 0) })
        {
            var response = await account.PostAsJsonAsync("/api/insights", Filter); response.EnsureSuccessStatusCode();
            var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); await Process();
            var result = await account.GetFromJsonAsync<JsonElement>($"/api/insights/{id}");
            Assert.Equal(expected, result.GetProperty("result").GetProperty("groups")[0].GetProperty("summary").GetProperty("clockMoves").GetInt32());
        }
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); var capture = new ReportCapture(db);
            var saved = await capture.Capture(owner, ReportCalendar.Progress(new(2026, 8, 20), new(2026, 8, 20)), null, new(), CancellationToken.None);
            Assert.Equal(clocks, Assert.Single(saved.Current.Games).ClockSecondsJson);
            await db.UserGames.Where(g => g.OwnerId == owner && g.GameId == input.Game.Id).ExecuteUpdateAsync(s => s.SetProperty(g => g.ClockSecondsJson, "[null,null,null,null]"));
            var frozen = await ReportRules.Generate(db, owner, saved, CancellationToken.None);
            Assert.Equal(2, Assert.Single(frozen.Groups).Summary.ClockMoves);
        }
        await alice.DeleteAsync("/api/account/data"); await bob.DeleteAsync("/api/account/data");
    }

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

    [Fact]
    public async Task Resumed_v1_snapshots_keep_legacy_rules_and_new_v2_settings_preserve_prior_results()
    {
        var (client, owner) = await Account(); using var account = client;
        var input = ObjectiveInsightTests.Fixture(); input.Run.OwnerId = owner;
        var filter = new InsightFilter(new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        var legacy = new InsightSnapshot { OwnerId = owner, FilterJson = JsonSerializer.Serialize(filter), OptionsJson = "{\"MinimumGames\":5,\"RecurringGames\":3,\"OpeningFullMoves\":10,\"MinimumLoss\":100,\"MinimumMaterial\":100,\"MinimumClockMoves\":20,\"MinimumClockCoverage\":0.5}",
            DetectorVersion = InsightRules.LegacyVersion, State = "running", LeaseToken = "labelled-old-worker", LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-1) };
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); db.Games.Add(input.Game);
            db.UserGames.Add(new() { OwnerId = owner, GameId = input.Game.Id, PlayerColour = "white" }); db.AnalysisRuns.Add(input.Run);
            db.InsightSnapshots.Add(legacy); await db.SaveChangesAsync();
        }
        await Process();
        var old = await account.GetFromJsonAsync<JsonElement>($"/api/insights/{legacy.Id}");
        var oldSummary = old.GetProperty("result").GetProperty("groups")[0].GetProperty("summary");
        Assert.Equal(InsightRules.LegacyVersion, oldSummary.GetProperty("detectorVersion").GetString()); Assert.Empty(oldSummary.GetProperty("findings").EnumerateArray());
        var created = await account.PostAsJsonAsync("/api/insights", filter); created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); await Process();
        var current = await account.GetFromJsonAsync<JsonElement>($"/api/insights/{id}");
        var currentSummary = current.GetProperty("result").GetProperty("groups")[0].GetProperty("summary");
        Assert.Equal(InsightRules.Version, currentSummary.GetProperty("detectorVersion").GetString()); Assert.Equal(2, currentSummary.GetProperty("findings").GetArrayLength());
        var changed = new InsightSnapshot { OwnerId = owner, FilterJson = JsonSerializer.Serialize(filter), OptionsJson = JsonSerializer.Serialize(new InsightOptions { ConversionPersistenceDecisions = 4, EndgameMinimumLoss = 500 }) };
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); db.InsightSnapshots.Add(changed); await db.SaveChangesAsync();
        }
        await Process();
        Assert.Empty((await account.GetFromJsonAsync<JsonElement>($"/api/insights/{changed.Id}")).GetProperty("result").GetProperty("groups")[0].GetProperty("summary").GetProperty("findings").EnumerateArray());
        Assert.Equal(old.GetProperty("result").GetRawText(), (await account.GetFromJsonAsync<JsonElement>($"/api/insights/{legacy.Id}")).GetProperty("result").GetRawText());
        Assert.Equal(current.GetProperty("result").GetRawText(), (await account.GetFromJsonAsync<JsonElement>($"/api/insights/{id}")).GetProperty("result").GetRawText());
    }
}
