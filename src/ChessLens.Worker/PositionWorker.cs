using ChessLens.Core.Analysis;

namespace ChessLens.Worker;

/// <summary>A bounded interactive lane shares the same engine pool with game reviews.</summary>
public sealed class PositionWorker(IServiceScopeFactory scopes, ILogger<PositionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<PositionAnalysisProcessor>().RunNext(stoppingToken))
                    await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                logger.LogError(e, "Interactive position analysis interrupted; durable state will be recovered");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
