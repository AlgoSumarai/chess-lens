using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Insights;
using ChessLens.Core.Persistence;
using ChessLens.Core.Reports;
using ChessLens.Core.Training;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class ReportPipelineTests(DatabaseFixture fixture)
{
    private async Task<(HttpClient Client, string Owner)> Account()
    {
        var client = fixture.CreateClient();
        async Task Csrf()
        {
            var token = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        }
        await Csrf(); (await client.PostAsJsonAsync("/api/account/register", new { email = $"report-{Guid.NewGuid():N}@test.example", password = "ChessLens.Report2026!" })).EnsureSuccessStatusCode();
        await Csrf(); return (client, (await client.GetFromJsonAsync<JsonElement>("/api/account/me")).GetProperty("id").GetString()!);
    }
    private static ReportPeriod Week() => ReportCalendar.Week(ReportCalendar.Monday(DateTimeOffset.UtcNow, "UTC").AddDays(-7), "UTC", DateTimeOffset.UtcNow);
    private async Task<(SourceGame Game, AnalysisRun Run)> Seed(string owner, ReportPeriod period, string source = "pgn", string colour = "white", string? opening = null,
        string? control = "300+0", string? pool = null, bool analysed = true, bool undated = false, string profile = "quick")
    {
        var row = InsightRulesTests.TacticalFixture(); var game = row.Game; var run = row.Run;
        game.Provider = source; game.PlayedAt = undated ? null : period.From.AddDays(1); game.TimeControl = control; game.Opening = opening; game.RatingPool = pool;
        game.WhiteRating = 1500; game.BlackRating = 1600; run.OwnerId = owner; run.Profile = profile;
        run.EngineVersion = "Fixture engine (test double)"; run.SettingsJson = "{}"; run.ClassificationSettingsJson = JsonSerializer.Serialize(new AnalysisOptions());
        run.CreatedAt = period.From; run.UpdatedAt = period.From.AddDays(2);
        foreach (var move in run.Moves) move.Provisional = false;
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        db.Games.Add(game); db.UserGames.Add(new() { OwnerId = owner, GameId = game.Id, PlayerColour = colour }); if (analysed) db.AnalysisRuns.Add(run);
        await db.SaveChangesAsync(); return (game, run);
    }
    private static async Task<JsonElement> Post(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task Tick()
    {
        await using var scope = fixture.Services.CreateAsyncScope(); Assert.True(await new ReportProcessor(scope.ServiceProvider.GetRequiredService<LensDbContext>()).RunNext(CancellationToken.None));
    }
    private static async Task<JsonElement> Get(HttpClient client, Guid id) => await client.GetFromJsonAsync<JsonElement>($"/api/reports/{id}");

    [Fact]
    public async Task Weekly_generation_is_concurrently_idempotent_owner_scoped_and_frozen_across_reanalysis_timezone_and_settings_changes()
    {
        var (client, owner) = await Account(); using var alice = client; var (other, _) = await Account(); using var bob = other;
        var period = Week(); var source = await Seed(owner, period, undated: true);
        var requests = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Post(alice, "/api/reports/weekly", new { weekFrom = period.FromDate })));
        var id = requests[0].GetProperty("id").GetGuid(); Assert.All(requests, r => Assert.Equal(id, r.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/reports/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/reports/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/reports/weekly", new { weekFrom = period.FromDate.AddDays(7) })).StatusCode);
        await Tick(); var saved = await Get(alice, id); Assert.Equal("completed", saved.GetProperty("state").GetString());
        Assert.Equal(1, saved.GetProperty("result").GetProperty("coverage").GetProperty("undatedGames").GetInt32());
        Assert.Equal(1, saved.GetProperty("result").GetProperty("results").GetProperty("losses").GetInt32());
        var original = saved.GetProperty("result").GetRawText();
        var reanalysis = new AnalysisRun { OwnerId = owner, GameId = source.Game.Id, State = "completed", Profile = "deep", UpdatedAt = DateTimeOffset.UtcNow,
            EngineVersion = source.Run.EngineVersion, SettingsJson = "{}", ClassificationSettingsJson = source.Run.ClassificationSettingsJson };
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); db.AnalysisRuns.Add(reanalysis); await db.SaveChangesAsync();
        }
        Assert.Equal(id, (await Post(alice, "/api/reports/weekly", new { weekFrom = period.FromDate })).GetProperty("id").GetGuid());
        (await alice.PutAsJsonAsync("/api/account/preferences", new { timezone = "Africa/Johannesburg", coachingLevel = "beginner", preferredTimeControls = "rapid" })).EnsureSuccessStatusCode();
        var changedZone = await Post(alice, "/api/reports/weekly", new { weekFrom = period.FromDate }); Assert.NotEqual(id, changedZone.GetProperty("id").GetGuid()); await Tick();
        Assert.Equal("Africa/Johannesburg", changedZone.GetProperty("period").GetProperty("timezone").GetString());
        Assert.Equal("UTC", (await Get(alice, id)).GetProperty("period").GetProperty("timezone").GetString());
        Assert.Equal(original, (await Get(alice, id)).GetProperty("result").GetRawText());
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>(); var options = new InsightOptions { EndgameAllowQueens = true };
            var capture = new ReportCapture(db); var inputs = await capture.Capture(owner, period, null, options, CancellationToken.None);
            options.EndgameAllowQueens = false; Assert.True(inputs.DetectorOptions.EndgameAllowQueens);
            var revision = await capture.Enqueue(owner, inputs, CancellationToken.None); Assert.Equal(2, revision!.Revision);
        }
        await Tick(); Assert.Equal(original, (await Get(alice, id)).GetProperty("result").GetRawText());
        (await alice.DeleteAsync("/api/account/data")).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/reports/{id}")).StatusCode);
        var empty = await Post(alice, "/api/reports/weekly", new { weekFrom = period.FromDate }); Assert.Equal("empty", empty.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Durable_scheduler_backfills_completed_weeks_and_reuses_existing_revisions_after_repeat_delivery()
    {
        var (client, owner) = await Account(); using var alice = client; var period = Week(); await Seed(owner, period); await Seed(owner, ReportCalendar.Previous(period));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            var users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.Timezone }).ToArrayAsync();
            foreach (var user in users)
                if (!await db.ReportSchedules.AnyAsync(s => s.OwnerId == user.Id)) db.ReportSchedules.Add(new() { OwnerId = user.Id, Timezone = user.Timezone, NextCheckAt = DateTimeOffset.UtcNow.AddDays(1) });
            await db.SaveChangesAsync(); await db.ReportSchedules.Where(s => s.OwnerId == owner).ExecuteUpdateAsync(s => s.SetProperty(s => s.NextCheckAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        async Task Schedule()
        {
            await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            await db.ReportSchedules.Where(s => s.OwnerId == owner).ExecuteUpdateAsync(s => s.SetProperty(s => s.NextCheckAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
            Assert.True(await new ReportScheduler(db, Options.Create(new InsightOptions())).RunNext(CancellationToken.None));
        }
        await Schedule(); await Tick(); await Schedule(); await Tick(); await Schedule();
        var reports = await alice.GetFromJsonAsync<JsonElement>("/api/reports?kind=weekly"); Assert.Equal(2, reports.GetProperty("total").GetInt32());
        await Schedule(); Assert.Equal(2, (await alice.GetFromJsonAsync<JsonElement>("/api/reports?kind=weekly")).GetProperty("total").GetInt32());
        (await alice.PutAsJsonAsync("/api/account/preferences", new { timezone = "Africa/Johannesburg", coachingLevel = "beginner", preferredTimeControls = "rapid" })).EnsureSuccessStatusCode();
        await Schedule(); await Tick(); Assert.Equal(3, (await alice.GetFromJsonAsync<JsonElement>("/api/reports?kind=weekly")).GetProperty("total").GetInt32());
        await alice.DeleteAsync("/api/account/data");
    }

    [Fact]
    public async Task Cancelled_jobs_stay_terminal_expired_leases_resume_and_active_budget_reuses_or_rejects_correctly()
    {
        var (client, owner) = await Account(); using var alice = client; var period = Week(); await Seed(owner, period);
        var queued = await Post(alice, "/api/reports/weekly", new { weekFrom = period.FromDate }); var id = queued.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await alice.PostAsJsonAsync("/api/progress", new ProgressFilter(period.FromDate, period.ToDate.AddDays(-1)))).StatusCode);
        (await alice.PostAsync($"/api/reports/{id}/cancel", null)).EnsureSuccessStatusCode();
        await using (var scope = fixture.Services.CreateAsyncScope()) Assert.False(await new ReportProcessor(scope.ServiceProvider.GetRequiredService<LensDbContext>()).RunNext(CancellationToken.None));
        Assert.Equal("cancelled", (await Get(alice, id)).GetProperty("state").GetString());
        await Post(alice, $"/api/reports/{id}/retry", new { });
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            await db.ReportSnapshots.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.State, "running").SetProperty(r => r.Attempts, 1).SetProperty(r => r.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        await Tick(); Assert.Equal("completed", (await Get(alice, id)).GetProperty("state").GetString());
        alice.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsync($"/api/reports/{id}/retry", null)).StatusCode);
        await using var final = fixture.Services.CreateAsyncScope(); await final.ServiceProvider.GetRequiredService<LensDbContext>().ReportSnapshots.Where(r => r.Id == id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Progress_filters_missing_dates_groups_ratings_denominators_and_prior_comparisons_are_grounded()
    {
        var (client, owner) = await Account(); using var alice = client; var period = Week(); var previous = ReportCalendar.Previous(period);
        for (var i = 0; i < 5; i++) { await Seed(owner, period, "lichess", opening: "Labelled fixture opening", pool: "blitz"); await Seed(owner, previous, "lichess", opening: "Labelled fixture opening", pool: "blitz"); }
        await Seed(owner, period, "chesscom", opening: "Labelled fixture opening", pool: "blitz");
        await Seed(owner, period, "lichess", colour: "black", opening: "Labelled fixture opening", pool: "blitz");
        await Seed(owner, period, "lichess", control: "600+5", pool: "rapid");
        await Seed(owner, period, "lichess", opening: "Labelled fixture opening", pool: "blitz", undated: true);
        await Seed(owner, period, "lichess", opening: "Labelled fixture opening", pool: "blitz", profile: "deep");
        var filter = new ProgressFilter(period.FromDate, period.ToDate.AddDays(-1), "lichess", "white", "Labelled fixture opening", TimeControl: "300+0", TimeControlGroup: "blitz");
        var queued = await Post(alice, "/api/progress", filter); var id = queued.GetProperty("id").GetGuid(); await Tick(); var result = (await Get(alice, id)).GetProperty("result");
        Assert.Equal(6, result.GetProperty("matchingGames").GetInt32()); Assert.Equal(1, result.GetProperty("undatedGamesExcluded").GetInt32());
        var groups = result.GetProperty("groups").EnumerateArray().ToArray(); Assert.Equal(2, groups.Length);
        var compatible = groups.Single(g => g.GetProperty("analysis").GetProperty("profile").GetString() == "quick");
        Assert.Equal(5, compatible.GetProperty("coverage").GetProperty("reviewedGames").GetInt32());
        Assert.Equal(10, compatible.GetProperty("summary").GetProperty("eligibleMoves").GetInt32());
        Assert.Equal(5, compatible.GetProperty("summary").GetProperty("verifiedErrorMoves").GetInt32());
        Assert.Equal("supported", compatible.GetProperty("comparison").GetProperty("status").GetString());
        Assert.Equal(0, compatible.GetProperty("comparison").GetProperty("rateDifference").GetDouble()); Assert.Equal(5, compatible.GetProperty("ratings").GetArrayLength());
        Assert.Equal("insufficient", groups.Single(g => g.GetProperty("analysis").GetProperty("profile").GetString() == "deep").GetProperty("comparison").GetProperty("status").GetString());
        var unknown = await Post(alice, "/api/progress", filter with { Opening = null, UnknownOpening = true, TimeControl = null, TimeControlGroup = "rapid" });
        await Tick(); Assert.Equal(1, (await Get(alice, unknown.GetProperty("id").GetGuid())).GetProperty("result").GetProperty("matchingGames").GetInt32());
        await alice.DeleteAsync("/api/account/data");
    }

    [Fact]
    public async Task Capture_before_deletion_cannot_enqueue_afterward_and_resource_limits_survive_deletion()
    {
        var (client, owner) = await Account(); using var alice = client; await Seed(owner, Week());
        ReportInputs captured;
        await using (var scope = fixture.Services.CreateAsyncScope()) captured = await new ReportCapture(scope.ServiceProvider.GetRequiredService<LensDbContext>()).Capture(owner, Week(), null, new(), CancellationToken.None);
        await alice.DeleteAsync("/api/account/data");
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            await Assert.ThrowsAsync<ReportDataChanged>(() => new ReportCapture(db).Enqueue(owner, captured, CancellationToken.None));
            db.ResourceUsages.Add(new() { OwnerId = owner, Kind = "report", Units = 20 }); await db.SaveChangesAsync();
        }
        await Seed(owner, Week()); Assert.Equal(HttpStatusCode.TooManyRequests, (await alice.PostAsJsonAsync("/api/reports/weekly", new { weekFrom = Week().FromDate })).StatusCode);
        await alice.DeleteAsync("/api/account/data");
    }

    [Theory]
    [InlineData("179", null)]
    [InlineData("180", null)]
    [InlineData("60+3", null)]
    [InlineData("0", null)]
    [InlineData("40/7200:3600", null)]
    [InlineData("1/86400", null)]
    [InlineData(null, "daily")]
    [InlineData("NaN", null)]
    public async Task PostgreSQL_grouping_matches_the_versioned_domain_function_for_supported_and_untrusted_controls(string? control, string? pool)
    {
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        var expected = TimeControlGroups.Group(control, pool);
        var actual = await db.Database.SqlQuery<string>($"SELECT chesslens_time_control_group_v1({control}, {pool}) AS \"Value\"").SingleAsync(); Assert.Equal(expected, actual);
    }
}
