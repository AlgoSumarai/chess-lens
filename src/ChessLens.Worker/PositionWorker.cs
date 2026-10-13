using ChessLens.Core.Analysis;
using ChessLens.Core.Training;

namespace ChessLens.Worker;

/// <summary>A bounded interactive lane shares the same engine pool with game reviews.</summary>
public sealed class PositionWorker(IServiceScopeFactory scopes, WorkerActivity activity, ILogger<PositionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var practiceFirst = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                activity.TouchInteractive();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromMinutes(3));
                await using var scope = scopes.CreateAsyncScope();
                var position = scope.ServiceProvider.GetRequiredService<PositionAnalysisProcessor>();
                var practice = scope.ServiceProvider.GetRequiredService<PracticeProcessor>();
                Func<CancellationToken, Task<bool>> first = practiceFirst ? practice.RunNext : position.RunNext;
                Func<CancellationToken, Task<bool>> second = practiceFirst ? position.RunNext : practice.RunNext;
                practiceFirst = !practiceFirst;
                if (!await first(deadline.Token) && !await second(deadline.Token))
                    await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                logger.LogError("Interactive work interrupted; durable state will be recovered: {ErrorType} {StackTrace}", e.GetType().Name, e.StackTrace);
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
