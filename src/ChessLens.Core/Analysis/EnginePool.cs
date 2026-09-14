using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ChessLens.Core.Analysis;

public sealed class EngineOptions
{
    public string Path { get; set; } = "";
    public int Processes { get; set; } = 2;
    public int Threads { get; set; } = 1;
    public int HashMb { get; set; } = 64;
    public int StartupTimeoutSeconds { get; set; } = 120;
}

public sealed class EnginePool : IChessEngine, IAsyncDisposable
{
    private readonly EngineOptions options;
    private readonly SemaphoreSlim permits;
    private readonly ConcurrentBag<UciEngine> idle = [];
    public EnginePool(IOptions<EngineOptions> configuration)
    {
        options = configuration.Value;
        if (options.Processes is < 1 or > 4) throw new ArgumentException("Choose 1–4 engine processes.");
        permits = new(options.Processes, options.Processes);
    }

    public async Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct)
    {
        await permits.WaitAsync(ct);
        UciEngine? engine = null;
        try
        {
            if (!idle.TryTake(out engine)) engine = await UciEngine.Start(options.Path, options.Threads, options.HashMb, ct, options.StartupTimeoutSeconds);
            return await engine.Search(position, budget, searchMoves, skill, ct);
        }
        finally
        {
            if (engine is not null)
            {
                if (engine.IsHealthy) idle.Add(engine);
                else await engine.DisposeAsync();
            }
            permits.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = 0; i < options.Processes; i++) await permits.WaitAsync();
        while (idle.TryTake(out var engine)) await engine.DisposeAsync();
        permits.Dispose();
    }
}
