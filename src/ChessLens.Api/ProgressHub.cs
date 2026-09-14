using ChessLens.Core.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Api;

[Authorize]
public sealed class ProgressHub(LensDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "owner:" + Context.User!.Owner());
        await base.OnConnectedAsync();
    }
    public async Task SubscribeJob(Guid id)
    {
        var owner = Context.User!.Owner();
        if (!await db.ImportJobs.AnyAsync(j => j.Id == id && j.OwnerId == owner) &&
            !await db.AnalysisRuns.AnyAsync(j => j.Id == id && j.OwnerId == owner) &&
            !await db.PositionAnalysisJobs.AnyAsync(j => j.Id == id && j.OwnerId == owner) &&
            !await db.InsightSnapshots.AnyAsync(j => j.Id == id && j.OwnerId == owner) &&
            !await db.Puzzles.AnyAsync(j => j.Id == id && j.OwnerId == owner))
            throw new HubException("Job not found.");
        // Subscription is owner-wide; this explicit check prevents IDs from granting additional access.
    }
}

public sealed class ProgressBroadcaster(IServiceScopeFactory scopes, IHubContext<ProgressHub> hub, ILogger<ProgressBroadcaster> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cursor = DateTimeOffset.UtcNow;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, stoppingToken);
                var next = DateTimeOffset.UtcNow;
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
                // A short overlap tolerates transaction commit timing; clients ignore repeated versions.
                var since = cursor.AddSeconds(-3);
                var imports = await db.ImportJobs.AsNoTracking().Where(j => j.UpdatedAt >= since).ToListAsync(stoppingToken);
                foreach (var job in imports)
                    await hub.Clients.Group("owner:" + job.OwnerId).SendAsync("jobProgress", new { kind = "import", job.Id, job.Version, state = GameEndpoints.JobView(job) }, stoppingToken);
                var runs = await db.AnalysisRuns.AsNoTracking().Include(r => r.Moves.Where(m => m.Ply == db.AnalysisRuns.Where(a => a.Id == m.RunId).Select(a => a.CompletedPlies).First()))
                    .Where(r => r.UpdatedAt >= since).ToListAsync(stoppingToken);
                foreach (var run in runs)
                    await hub.Clients.Group("owner:" + run.OwnerId).SendAsync("jobProgress", new { kind = "analysis", run.Id, run.GameId, run.Version, state = AnalysisEndpoints.View(run) }, stoppingToken);
                var positions = await db.PositionAnalysisJobs.AsNoTracking().Where(j => j.UpdatedAt >= since).ToListAsync(stoppingToken);
                foreach (var job in positions)
                    await hub.Clients.Group("owner:" + job.OwnerId).SendAsync("jobProgress", new { kind = "position", job.Id, job.GameId, job.Version, state = PositionAnalysisEndpoints.View(job) }, stoppingToken);
                var insights = await db.InsightSnapshots.AsNoTracking().Where(j => j.UpdatedAt >= since)
                    .Select(j => new { j.Id, j.OwnerId, j.State, j.Version }).ToListAsync(stoppingToken);
                foreach (var job in insights)
                    await hub.Clients.Group("owner:" + job.OwnerId).SendAsync("jobProgress", new { kind = "insights", job.Id, job.Version, state = job.State }, stoppingToken);
                var puzzles = await db.Puzzles.AsNoTracking().Where(j => j.UpdatedAt >= since)
                    .Select(j => new { j.Id, j.OwnerId, j.State, j.Version }).ToListAsync(stoppingToken);
                foreach (var job in puzzles)
                    await hub.Clients.Group("owner:" + job.OwnerId).SendAsync("jobProgress", new { kind = "puzzle", job.Id, job.Version, state = job.State }, stoppingToken);
                cursor = next;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { log.LogWarning(e, "Progress notifications temporarily unavailable; clients reconcile through HTTP"); }
        }
    }
}
