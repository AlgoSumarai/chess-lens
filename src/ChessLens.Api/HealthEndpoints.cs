using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Api;

public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
        app.MapGet("/health/ready", async (LensDbContext db, CancellationToken ct) =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                if (!await db.Database.CanConnectAsync(deadline.Token) || (await db.Database.GetPendingMigrationsAsync(deadline.Token)).Any())
                    return Unavailable("database");
                var now = DateTimeOffset.UtcNow;
                var ready = await db.WorkerStatuses.AnyAsync(s => s.Ready && s.LastSeen >= now.AddSeconds(-15) &&
                    s.EngineCheckedAt >= now.AddMinutes(-3) && s.MainLaneAt >= now.AddMinutes(-16) && s.InteractiveLaneAt >= now.AddMinutes(-4), deadline.Token);
                return ready ? Results.Ok(new { status = "ready", database = "ready", worker = "ready" }) : Unavailable("worker");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return Unavailable("database"); }
        });
    }

    private static IResult Unavailable(string service) => Results.Json(new { status = "unavailable", service }, statusCode: 503);
}
