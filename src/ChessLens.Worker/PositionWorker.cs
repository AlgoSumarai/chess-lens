using ChessLens.Core.Analysis;
using ChessLens.Core.Training;

namespace ChessLens.Worker;

/// <summary>A bounded interactive lane shares the same engine pool with game reviews.</summary>
public sealed class PositionWorker(IServiceScopeFactory scopes, ILogger<PositionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var practiceFirst = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var position = scope.ServiceProvider.GetRequiredService<PositionAnalysisProcessor>();
                var practice = scope.ServiceProvider.GetRequiredService<PracticeProcessor>();
                Func<CancellationToken, Task<bool>> first = practiceFirst ? practice.RunNext : position.RunNext;
                Func<CancellationToken, Task<bool>> second = practiceFirst ? position.RunNext : practice.RunNext;
                practiceFirst = !practiceFirst;
                if (!await first(stoppingToken) && !await second(stoppingToken))
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
