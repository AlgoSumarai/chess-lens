using System.Diagnostics;
using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Operations;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Worker;

public sealed class WorkerHealth(IServiceScopeFactory scopes, EnginePool engine, WorkerActivity activity, ILogger<WorkerHealth> logger) : BackgroundService
{
    private readonly Guid instance = Guid.NewGuid();
    private long lastFailedProbe;
    private static string HealthFile => Path.Combine(Path.GetTempPath(), "chesslens-worker-health.json");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Restarting must invalidate a prior container process's readiness immediately.
        File.Delete(HealthFile);
        await Task.WhenAll(Heartbeat(stoppingToken), Probe(stoppingToken));
    }

    private async Task Heartbeat(CancellationToken ct)
    {
        var lastCleanup = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(4));
                await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LensDbContext>();
                var now = DateTimeOffset.UtcNow;
                var status = await db.WorkerStatuses.SingleOrDefaultAsync(s => s.Id == instance, deadline.Token);
                if (status is null) { status = new() { Id = instance }; db.WorkerStatuses.Add(status); }
                status.LastSeen = now; status.EngineCheckedAt = engine.LastSuccessfulSearch;
                status.MainLaneAt = activity.Main; status.InteractiveLaneAt = activity.Interactive;
                status.Ready = status.HasRecentProgress(now) && status.EngineCheckedAt?.Ticks > Interlocked.Read(ref lastFailedProbe);
                await db.SaveChangesAsync(deadline.Token);
                var local = JsonSerializer.Serialize(new LocalStatus(Environment.ProcessId, status.Ready, now));
                await File.WriteAllTextAsync(HealthFile + ".tmp", local, deadline.Token);
                File.Move(HealthFile + ".tmp", HealthFile, overwrite: true);
                if (now - lastCleanup >= TimeSpan.FromHours(1))
                {
                    await db.WorkerStatuses.Where(s => s.LastSeen < now.AddDays(-1)).ExecuteDeleteAsync(deadline.Token);
                    lastCleanup = now;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                // Do not include connection strings, SQL values or external payloads.
                logger.LogWarning("Worker {WorkerId} heartbeat unavailable: {ErrorType}", instance, error.GetType().Name);
            }
            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
        File.Delete(HealthFile);
    }

    private async Task Probe(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (engine.LastSuccessfulSearch is null || engine.LastSuccessfulSearch < DateTimeOffset.UtcNow.AddSeconds(-30))
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(150));
                    await engine.Search(new(ChessRules.InitialFen, []), new("readiness-v1", 1000, 1000), null, 20, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                Interlocked.Exchange(ref lastFailedProbe, DateTimeOffset.UtcNow.Ticks);
                logger.LogWarning("Worker {WorkerId} engine readiness unavailable: {ErrorType}", instance, error.GetType().Name);
            }
            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    public static async Task<int> CheckLocal()
    {
        try
        {
            var status = JsonSerializer.Deserialize<LocalStatus>(await File.ReadAllTextAsync(HealthFile));
            if (status is null || !status.Ready || status.LastSeen < DateTimeOffset.UtcNow.AddSeconds(-15)) return 1;
            using var process = Process.GetProcessById(status.ProcessId);
            return process.HasExited ? 1 : 0;
        }
        catch { return 1; }
    }

    private sealed record LocalStatus(int ProcessId, bool Ready, DateTimeOffset LastSeen);
}
