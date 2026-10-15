using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Imports;
using ChessLens.Core.Analysis;
using ChessLens.Core.Coaching;
using ChessLens.Core.Games;
using ChessLens.Core.Insights;
using ChessLens.Core.Persistence;
using ChessLens.Core.Reports;
using ChessLens.Core.Training;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class RealtimeTests(DatabaseFixture fixture)
{
    private async Task<(HttpClient Client, string Cookie)> Account()
    {
        var client = fixture.CreateClient();
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var response = await client.PostAsJsonAsync("/api/account/register", new { email = $"realtime-{Guid.NewGuid():N}@test.example", password = "ChessLens.Realtime2026!" });
        response.EnsureSuccessStatusCode();
        var cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        csrf = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return (client, cookie);
    }

    private HubConnection Connect(string cookie) => new HubConnectionBuilder().WithUrl("http://localhost/hubs/progress", o =>
    {
        o.Transports = HttpTransportType.LongPolling;
        o.Headers["Cookie"] = cookie;
        o.HttpMessageHandlerFactory = _ => fixture.Server.CreateHandler();
    }).Build();

    [Fact]
    public async Task Owner_gets_progress_and_cannot_subscribe_to_another_owners_job()
    {
        var (client, cookie) = await Account();
        using var ownClient = client;
        var (other, otherCookie) = await Account();
        using var otherClient = other;
        await using var connection = Connect(cookie);
        await using var foreignConnection = Connect(otherCookie);
        await connection.StartAsync(); await foreignConnection.StartAsync();
        var notification = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var foreignIds = new List<Guid>();
        connection.On<JsonElement>("jobProgress", value => notification.TrySetResult(value));
        foreignConnection.On<JsonElement>("jobProgress", value => { lock (foreignIds) foreignIds.Add(value.GetProperty("id").GetGuid()); });
        var response = await client.PostAsJsonAsync("/api/imports/pgn", new PgnImportRequest(PgnTests.Game));
        response.EnsureSuccessStatusCode();
        var job = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = job.GetProperty("id").GetGuid();
        await connection.InvokeAsync("SubscribeJob", id);
        var rejection = await Assert.ThrowsAsync<HubException>(() => foreignConnection.InvokeAsync("SubscribeJob", id));
        Assert.Contains("Job not found", rejection.Message);
        var received = await notification.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(id, received.GetProperty("id").GetGuid());
        await foreignConnection.StopAsync();
        Assert.DoesNotContain(id, foreignIds);
        // A new connection retrieves persisted state; delivery is not authoritative storage.
        await connection.StopAsync(); await connection.StartAsync();
        var state = await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{id}");
        Assert.Equal(id, state.GetProperty("id").GetGuid());
        Assert.True(state.GetProperty("version").GetInt64() >= received.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Every_job_kind_requires_its_owner_on_the_actual_hub_connection()
    {
        var (client, cookie) = await Account(); using var alice = client;
        var (other, otherCookie) = await Account(); using var bob = other;
        var owner = (await alice.GetFromJsonAsync<JsonElement>("/api/account/me")).GetProperty("id").GetString()!;
        var imported = await alice.PostAsJsonAsync("/api/imports/pgn", new PgnImportRequest(PgnTests.Game));
        imported.EnsureSuccessStatusCode();
        var importId = (await imported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Guid[] ids;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
            // Administrative fixtures exercise authorization only, never chess execution.
            // Historical UpdatedAt values keep them outside the broadcaster's live window.
            var old = DateTimeOffset.UtcNow.AddDays(-1);
            var game = new PgnReader().Parse($"[Event \"Original authorization fixture {Guid.NewGuid()}\"]\n[White \"Learner\"]\n[Black \"Opponent\"]\n[Result \"0-1\"]\n1.e4 e5 0-1");
            db.Games.Add(game); db.UserGames.Add(new UserGame { OwnerId = owner, GameId = game.Id });
            var run = new AnalysisRun { OwnerId = owner, GameId = game.Id, State = "cancelled", UpdatedAt = old };
            var move = new MoveAnalysis { RunId = run.Id, Ply = 1, FenBefore = game.InitialFen, PlayedMove = "e2e4", Side = "white", BestMove = "e2e4",
                BestScoreKind = "cp", PlayedScoreKind = "cp", Classification = "sound", BestSearchJson = "{}", PlayedSearchJson = "{}", BudgetJson = "{}", CacheIdentity = "authorization-fixture" };
            var channel = new PositionReviewChannel { Id = Guid.NewGuid(), OwnerId = owner, GameId = game.Id, UpdatedAt = old };
            var position = new PositionAnalysisJob { OwnerId = owner, GameId = game.Id, ChannelId = channel.Id,
                InitialFen = game.InitialFen, HistoryJson = "[]", Fen = game.InitialFen, State = "cancelled", UpdatedAt = old };
            var insight = new InsightSnapshot { OwnerId = owner, FilterJson = "{}", OptionsJson = "{}", State = "cancelled", UpdatedAt = old };
            var puzzle = new Puzzle { OwnerId = owner, GameId = game.Id, RunId = run.Id, Ply = 1, State = "cancelled", UpdatedAt = old };
            var batch = new ImportAnalysisBatch { OwnerId = owner, ImportJobId = importId, Profile = "deep", RunIdsJson = "[]", State = "cancelled", UpdatedAt = old };
            var coaching = new CoachingJob { OwnerId = owner, GameId = game.Id, RunId = run.Id, Ply = 1,
                CacheIdentity = Guid.NewGuid().ToString(), EvidenceJson = "{}", State = "cancelled", UpdatedAt = old };
            var practice = new PracticeSession { OwnerId = owner, GameId = game.Id, RunId = run.Id, Ply = 1, Side = "white",
                StartingPositionJson = "{}", SourceEvidenceJson = "{}", ObjectiveJson = "{}", State = "cancelled", UpdatedAt = old };
            var report = new ReportSnapshot { OwnerId = owner, Kind = "progress", ScopeIdentity = Guid.NewGuid().ToString(),
                CacheIdentity = Guid.NewGuid().ToString(), InputsJson = "{}", State = "cancelled", UpdatedAt = old };
            db.AddRange(run, move, channel, position, insight, puzzle, batch, coaching, practice, report);
            await db.SaveChangesAsync();
            ids = [importId, run.Id, position.Id, insight.Id, puzzle.Id, batch.Id, coaching.Id, practice.Id, report.Id];
        }
        await using var owned = Connect(cookie); await using var foreign = Connect(otherCookie);
        await owned.StartAsync(); await foreign.StartAsync();
        foreach (var id in ids)
        {
            await owned.InvokeAsync("SubscribeJob", id);
            var rejection = await Assert.ThrowsAsync<HubException>(() => foreign.InvokeAsync("SubscribeJob", id));
            Assert.Contains("Job not found", rejection.Message);
        }
        (await alice.DeleteAsync("/api/account/data")).EnsureSuccessStatusCode();
        foreach (var id in ids)
            await Assert.ThrowsAsync<HubException>(() => owned.InvokeAsync("SubscribeJob", id));
    }

    [Fact]
    public async Task Foreign_browser_origin_is_rejected_before_hub_negotiation()
    {
        var (client, _) = await Account();
        using var own = client;
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        var response = await client.PostAsync("/hubs/progress/negotiate?negotiateVersion=1", null);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }
}
