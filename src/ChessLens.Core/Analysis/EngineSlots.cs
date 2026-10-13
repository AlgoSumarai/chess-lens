using Npgsql;

namespace ChessLens.Core.Analysis;

/// <summary>Database-wide process slots. A physical session lives as long as its engine process.</summary>
public sealed class EngineSlots(string connectionString)
{
    public const int MaximumProcesses = 2;
    public const string ApplicationName = "ChessLens engine slot";

    public async Task<EngineSlotLease> Acquire(CancellationToken ct)
    {
        // Never return session locks to a connection pool. Closing this physical
        // session (including on worker death) releases every lock it owns.
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false, Multiplexing = false, Timeout = 5, CommandTimeout = 2,
            CancellationTimeout = 1000, ApplicationName = ApplicationName
        };
        var connection = new NpgsqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            var waiting = false;
            while (true)
            {
                for (var slot = 0; slot < MaximumProcesses; slot++)
                {
                    await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@key, 0))", connection);
                    command.Parameters.AddWithValue("key", $"chesslens-engine-slot-{slot}");
                    if (await command.ExecuteScalarAsync(ct) is true)
                    {
                        if (waiting)
                        {
                            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock_shared(hashtextextended('chesslens-engine-demand', 0))", connection);
                            await release.ExecuteScalarAsync(ct);
                        }
                        return new EngineSlotLease(connection);
                    }
                }
                if (!waiting)
                {
                    await using var demand = new NpgsqlCommand("SELECT pg_advisory_lock_shared(hashtextextended('chesslens-engine-demand', 0))", connection);
                    await demand.ExecuteScalarAsync(ct);
                    waiting = true;
                }
                await Task.Delay(Random.Shared.Next(75, 126), ct);
            }
        }
        catch { await connection.DisposeAsync(); throw; }
    }
}

/// <summary>Loss cancels active searches; the pool kills the child before disposing a healthy lease.</summary>
public sealed class EngineSlotLease : IAsyncDisposable
{
    private readonly NpgsqlConnection connection;
    private readonly CancellationTokenSource stop = new();
    private readonly CancellationTokenSource lost = new();
    private readonly Task heartbeat;
    private volatile bool yieldRequested;
    public CancellationToken Lost => lost.Token;
    public bool YieldRequested => yieldRequested;

    internal EngineSlotLease(NpgsqlConnection connection)
    {
        this.connection = connection;
        heartbeat = Monitor();
    }

    private async Task Monitor()
    {
        try
        {
            while (true)
            {
                await Task.Delay(500, stop.Token);
                // Waiters hold a shared demand lock. Probe/release its exclusive
                // counterpart in one statement, without waiting or retaining it.
                await using var command = new NpgsqlCommand("""
                    SELECT CASE WHEN pg_try_advisory_lock(hashtextextended('chesslens-engine-demand', 0))
                    THEN pg_advisory_unlock(hashtextextended('chesslens-engine-demand', 0)) ELSE false END
                    """, connection);
                if (await command.ExecuteScalarAsync(stop.Token) is false) yieldRequested = true;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch { await lost.CancelAsync(); }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await heartbeat;
        await connection.DisposeAsync();
        stop.Dispose(); lost.Dispose();
    }
}
