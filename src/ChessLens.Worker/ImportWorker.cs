using ChessLens.Core.Imports;
using ChessLens.Core.Analysis;
using ChessLens.Core.Insights;
using ChessLens.Core.Training;
using ChessLens.Core.Coaching;
using ChessLens.Core.Reports;

namespace ChessLens.Worker;

public sealed class ImportWorker(IServiceScopeFactory scopes, WorkerActivity activity, ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextLane = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                activity.TouchMain();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromMinutes(15));
                await using var scope = scopes.CreateAsyncScope();
                // Admit reserved batch reviews even while other queues are busy.
                await scope.ServiceProvider.GetRequiredService<ImportBatchProcessor>().RunNext(deadline.Token);
                Func<CancellationToken, Task<bool>>[] lanes = [
                    scope.ServiceProvider.GetRequiredService<ImportProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<ProviderImportProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<AnalysisProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<InsightProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<PuzzleProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<CoachingProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<ReportProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<ReportScheduler>().RunNext
                ];
                var processed = false;
                for (var i = 0; i < lanes.Length; i++)
                {
                    var lane = nextLane;
                    nextLane = (nextLane + 1) % lanes.Length;
                    if (await lanes[lane](deadline.Token)) { processed = true; break; }
                }
                if (!processed) await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                logger.LogError("Background work interrupted; durable state will be recovered: {ErrorType} {StackTrace}", e.GetType().Name, e.StackTrace);
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
