using ChessLens.Core.Imports;
using ChessLens.Core.Analysis;
using ChessLens.Core.Insights;
using ChessLens.Core.Training;
using ChessLens.Core.Coaching;

namespace ChessLens.Worker;

public sealed class ImportWorker(IServiceScopeFactory scopes, ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextLane = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                // Admit reserved batch reviews even while other queues are busy.
                await scope.ServiceProvider.GetRequiredService<ImportBatchProcessor>().RunNext(stoppingToken);
                Func<CancellationToken, Task<bool>>[] lanes = [
                    scope.ServiceProvider.GetRequiredService<ImportProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<ProviderImportProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<AnalysisProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<InsightProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<PuzzleProcessor>().RunNext,
                    scope.ServiceProvider.GetRequiredService<CoachingProcessor>().RunNext
                ];
                var processed = false;
                for (var i = 0; i < lanes.Length; i++)
                {
                    var lane = nextLane;
                    nextLane = (nextLane + 1) % lanes.Length;
                    if (await lanes[lane](stoppingToken)) { processed = true; break; }
                }
                if (!processed) await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                logger.LogError(e, "Background import failed; durable state will be recovered");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
