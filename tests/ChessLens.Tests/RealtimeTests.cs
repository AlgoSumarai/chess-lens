using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Imports;
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
    public async Task Foreign_browser_origin_is_rejected_before_hub_negotiation()
    {
        var (client, _) = await Account();
        using var own = client;
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        var response = await client.PostAsync("/hubs/progress/negotiate?negotiateVersion=1", null);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }
}
