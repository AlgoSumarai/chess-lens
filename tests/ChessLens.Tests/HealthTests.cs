using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Operations;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class HealthTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Database_connectivity_alone_is_not_worker_readiness_and_old_or_failed_heartbeats_expire()
    {
        using var client = fixture.CreateClient();
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        // Operational-status fixtures; this test does not run a worker process.
        var now = DateTimeOffset.UtcNow;
        var status = new WorkerStatus { Id = Guid.NewGuid(), LastSeen = now, EngineCheckedAt = now, MainLaneAt = now, InteractiveLaneAt = now, Ready = true };
        db.WorkerStatuses.Add(status); await db.SaveChangesAsync();
        try
        {
            var response = await client.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("ready", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("worker").GetString());
            status.LastSeen = now.AddSeconds(-16); await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
            status.LastSeen = DateTimeOffset.UtcNow; status.Ready = false; await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
            status.Ready = true; status.EngineCheckedAt = now.AddMinutes(-4); await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
            status.EngineCheckedAt = now; status.MainLaneAt = now.AddMinutes(-17); await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
            status.MainLaneAt = now; status.InteractiveLaneAt = now.AddMinutes(-5); await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
        finally { await db.WorkerStatuses.Where(s => s.Id == status.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public void Engine_and_each_work_lane_must_have_recent_progress()
    {
        var now = DateTimeOffset.UtcNow;
        var status = new WorkerStatus { EngineCheckedAt = now, MainLaneAt = now, InteractiveLaneAt = now };
        Assert.True(status.HasRecentProgress(now));
        status.EngineCheckedAt = null; Assert.False(status.HasRecentProgress(now));
        status.EngineCheckedAt = now.AddMinutes(-3); status.MainLaneAt = now.AddMinutes(-16); status.InteractiveLaneAt = now.AddMinutes(-4);
        Assert.True(status.HasRecentProgress(now)); Assert.False(status.HasRecentProgress(now.AddTicks(1)));
    }
}
