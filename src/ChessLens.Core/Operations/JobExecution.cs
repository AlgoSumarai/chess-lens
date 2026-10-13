using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ChessLens.Core.Operations;

/// <summary>Correlates one processing slice without recording source content, people or credentials.</summary>
public sealed class JobExecution : IDisposable
{
    private readonly ILogger? logger;
    private readonly IDisposable? scope;
    private readonly long started = Stopwatch.GetTimestamp();
    public JobExecution(ILogger? logger, string kind, Guid id)
    {
        this.logger = logger;
        scope = logger?.BeginScope(new Dictionary<string, object> { ["JobKind"] = kind, ["JobId"] = id, ["ExecutionId"] = Guid.NewGuid() });
        logger?.LogInformation("Job processing slice started");
    }
    public void Interrupted(Exception error) => logger?.LogWarning("Job processing slice interrupted: {ErrorType}", error.GetType().Name);
    public void Dispose()
    {
        // Finished means this invocation returned, not that the durable job succeeded.
        logger?.LogInformation("Job processing slice finished after {ElapsedMilliseconds} ms", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        scope?.Dispose();
    }
}
