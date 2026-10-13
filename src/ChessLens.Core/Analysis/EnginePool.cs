using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChessLens.Core.Analysis;

public sealed class EngineOptions
{
    public string Path { get; set; } = "";
    public int Processes { get; set; } = 2;
    public int Threads { get; set; } = 1;
    public int HashMb { get; set; } = 64;
    public int StartupTimeoutSeconds { get; set; } = 120;
    public int QueueTimeoutSeconds { get; set; } = 30;
}

public sealed class EnginePool : IChessEngine, IAsyncDisposable
{
    private readonly EngineOptions options;
    private readonly SemaphoreSlim permits;
    private readonly ConcurrentQueue<Entry> idle = [];
    private readonly ConcurrentDictionary<int, Entry> live = [];
    private readonly CancellationTokenSource stopping = new();
    private readonly EngineSlots? slots;
    private readonly ILogger<EnginePool> logger;
    private readonly Task maintenance;
    private long yieldUntil;
    private long lastSuccessfulSearch;
    private int disposed;
    public int[] ProcessIds => live.Keys.Order().ToArray();
    public DateTimeOffset? LastSuccessfulSearch => Interlocked.Read(ref lastSuccessfulSearch) is var ticks && ticks > 0 ? new(ticks, TimeSpan.Zero) : null;

    // A null slot coordinator is for standalone engine protocol tests only.
    // The application worker always supplies its required database coordinator.
    public EnginePool(IOptions<EngineOptions> configuration, EngineSlots? slots = null, ILogger<EnginePool>? logger = null)
    {
        options = configuration.Value;
        if (options.Processes is < 1 or > EngineSlots.MaximumProcesses || options.Threads is < 1 or > 4 || options.HashMb is < 16 or > 256)
            throw new ArgumentException("Choose 1–2 engine processes, 1–4 threads and 16–256 MiB hash per process.");
        if (options.QueueTimeoutSeconds is < 5 or > 120 || options.StartupTimeoutSeconds is < 5 or > 120)
            throw new ArgumentException("Engine queue/startup timeouts must be between 5 and 120 seconds.");
        this.slots = slots; this.logger = logger ?? NullLogger<EnginePool>.Instance;
        permits = new(options.Processes, options.Processes);
        maintenance = Maintain();
    }

    public async Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
        using var queue = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        queue.CancelAfter(TimeSpan.FromSeconds(options.QueueTimeoutSeconds));
        await permits.WaitAsync(queue.Token);
        Entry? entry = null;
        try
        {
            var remainingYield = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), Interlocked.Read(ref yieldUntil));
            if (remainingYield > TimeSpan.Zero) await Task.Delay(remainingYield, queue.Token);
            if (idle.TryDequeue(out entry) && !entry.Reusable)
            {
                await Retire(entry); entry = null;
            }
            if (entry is null)
            {
                var lease = slots is null ? null : await slots.Acquire(queue.Token);
                try
                {
                    using var startup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, lease?.Lost ?? CancellationToken.None);
                    var engine = await UciEngine.Start(options.Path, options.Threads, options.HashMb, startup.Token, options.StartupTimeoutSeconds);
                    entry = new(engine, lease);
                    live.TryAdd(engine.ProcessId, entry);
                    logger.LogInformation("Engine process {EngineProcessId} started with {Threads} threads and {HashMiB} MiB hash", engine.ProcessId, options.Threads, options.HashMb);
                }
                catch { if (lease is not null) await lease.DisposeAsync(); throw; }
            }
            using var search = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, entry.Lease?.Lost ?? CancellationToken.None);
            var result = await entry.Engine.Search(position, budget, searchMoves, skill, search.Token);
            search.Token.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref lastSuccessfulSearch, DateTimeOffset.UtcNow.Ticks);
            return result;
        }
        finally
        {
            try
            {
                if (entry is not null)
                {
                    if (entry.Reusable && !stopping.IsCancellationRequested)
                    {
                        entry.LastUsed = Stopwatch.GetTimestamp(); idle.Enqueue(entry);
                    }
                    else await Retire(entry);
                }
            }
            finally { permits.Release(); }
        }
    }

    private async Task Maintain()
    {
        try
        {
            while (true)
            {
                await Task.Delay(500, stopping.Token);
                for (var remaining = idle.Count; remaining > 0 && permits.Wait(0); remaining--)
                {
                    try
                    {
                        if (!idle.TryDequeue(out var entry)) break;
                        if (!entry.Reusable || Stopwatch.GetElapsedTime(entry.LastUsed) >= TimeSpan.FromSeconds(5)) await Retire(entry);
                        else idle.Enqueue(entry);
                    }
                    finally { permits.Release(); }
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    private async Task Retire(Entry entry)
    {
        var processId = entry.Engine.ProcessId;
        // Release the database slot only after the OS has reaped the child.
        await entry.Engine.DisposeAsync();
        live.TryRemove(processId, out _);
        if (entry.Lease?.YieldRequested == true)
            Interlocked.Exchange(ref yieldUntil, Stopwatch.GetTimestamp() + Stopwatch.Frequency / 4);
        if (entry.Lease is not null) await entry.Lease.DisposeAsync();
        logger.LogInformation("Engine process {EngineProcessId} retired", processId);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await stopping.CancelAsync();
        await maintenance;
        for (var i = 0; i < options.Processes; i++) await permits.WaitAsync();
        while (idle.TryDequeue(out var entry)) await Retire(entry);
        permits.Dispose(); stopping.Dispose();
    }

    private sealed class Entry(UciEngine engine, EngineSlotLease? lease)
    {
        public UciEngine Engine { get; } = engine;
        public EngineSlotLease? Lease { get; } = lease;
        public long LastUsed { get; set; } = Stopwatch.GetTimestamp();
        public bool Reusable => Engine.IsHealthy && !(Lease?.Lost.IsCancellationRequested ?? false) && !(Lease?.YieldRequested ?? false);
    }
}
