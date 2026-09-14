using ChessLens.Core.Imports;
using ChessLens.Core.Analysis;
using ChessLens.Core.Insights;
using ChessLens.Core.Training;

namespace ChessLens.Worker;

public sealed class ImportWorker(IServiceScopeFactory scopes, ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processed = await scope.ServiceProvider.GetRequiredService<ImportProcessor>().RunNext(stoppingToken);
                if (!processed) processed = await scope.ServiceProvider.GetRequiredService<AnalysisProcessor>().RunNext(stoppingToken);
                if (!processed) processed = await scope.ServiceProvider.GetRequiredService<InsightProcessor>().RunNext(stoppingToken);
                if (!processed) processed = await scope.ServiceProvider.GetRequiredService<PuzzleProcessor>().RunNext(stoppingToken);
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
