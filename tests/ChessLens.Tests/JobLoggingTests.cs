using System.Text.Json;
using ChessLens.Core.Operations;
using Microsoft.Extensions.Logging;

namespace ChessLens.Tests;

public sealed class JobLoggingTests
{
    [Fact]
    public void Operational_logs_correlate_jobs_and_elapsed_time_without_external_exception_payloads()
    {
        var logger = new CaptureLogger(); var id = Guid.NewGuid();
        using (var execution = new JobExecution(logger, "provider-import", id))
            execution.Interrupted(new HttpRequestException("PRIVATE-PGN-ANNOTATION AND A PROVIDER CREDENTIAL FIXTURE"));
        var text = string.Join('\n', logger.Entries);
        Assert.Contains(id.ToString(), text); Assert.Contains("ExecutionId", text); Assert.Contains("provider-import", text);
        Assert.Contains("HttpRequestException", text); Assert.Contains("ElapsedMilliseconds", text);
        Assert.DoesNotContain("PRIVATE-PGN-ANNOTATION", text); Assert.DoesNotContain("CREDENTIAL", text);
        Assert.Equal(0, logger.ActiveScopes);
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<string> Entries { get; } = [];
        public int ActiveScopes { get; private set; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            Entries.Add(JsonSerializer.Serialize(state)); ActiveScopes++;
            return new Scope(() => ActiveScopes--);
        }
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(JsonSerializer.Serialize(state)); Entries.Add(formatter(state, exception));
            if (exception is not null) Entries.Add(exception.ToString());
        }
        private sealed class Scope(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }
}
