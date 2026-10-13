using System.Diagnostics;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ChessLens.Tests;

[Collection("database")]
[Trait("Category", "Stockfish")]
public sealed class EngineSlotTests(DatabaseFixture fixture)
{
    private static EnginePosition Start => new(ChessRules.InitialFen, []);
    private EnginePool Pool() => new(Options.Create(new EngineOptions
    {
        Path = Environment.GetEnvironmentVariable("Stockfish__Path") ?? throw new InvalidOperationException("Stockfish__Path is required."),
        Processes = 1, Threads = 1, HashMb = 32, QueueTimeoutSeconds = 10
    }), new EngineSlots(fixture.Connection));

    private async Task<int[]> Sessions()
    {
        await using var connection = new NpgsqlConnection(fixture.Connection); await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT pid FROM pg_stat_activity WHERE datname = current_database() AND application_name = @name", connection);
        command.Parameters.AddWithValue("name", EngineSlots.ApplicationName);
        await using var reader = await command.ExecuteReaderAsync(); var ids = new List<int>();
        while (await reader.ReadAsync()) ids.Add(reader.GetInt32(0));
        return ids.ToArray();
    }

    private static async Task Until(Func<Task<bool>> condition, int seconds = 10)
    {
        var clock = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(seconds), "The observed process/database state did not reach its expected bound.");
            await Task.Delay(25);
        }
    }

    private static bool Gone(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }

    [Fact]
    public async Task Independent_pools_share_slots_and_a_waiter_retires_idle_processes_before_replacement()
    {
        var first = Pool(); var second = Pool(); var third = Pool();
        var observed = new HashSet<int>();
        try
        {
            await first.Search(Start, SearchBudget.Quick, null, 20, CancellationToken.None);
            Assert.True((await first.Search(Start, SearchBudget.Quick, null, 20, CancellationToken.None)).CacheHit);
            await second.Search(Start, SearchBudget.Quick, null, 20, CancellationToken.None);
            var initial = first.ProcessIds.Concat(second.ProcessIds).ToArray(); Assert.Equal(2, initial.Length);
            observed.UnionWith(initial);
            Assert.Equal(2, (await Sessions()).Length);
            var waiting = third.Search(Start, SearchBudget.Quick, null, 20, CancellationToken.None);
            await Until(() =>
            {
                var ids = first.ProcessIds.Concat(second.ProcessIds).Concat(third.ProcessIds).ToArray();
                Assert.InRange(ids.Count(pid => !Gone(pid)), 0, 2); observed.UnionWith(ids);
                return Task.FromResult(waiting.IsCompleted);
            });
            Assert.True((await waiting).Complete);
            var replacement = Assert.Single(third.ProcessIds); observed.Add(replacement);
            Assert.DoesNotContain(replacement, initial);
            Assert.Contains(initial, Gone);
        }
        finally { await first.DisposeAsync(); await second.DisposeAsync(); await third.DisposeAsync(); }
        Assert.All(observed, pid => Assert.True(Gone(pid), $"Engine {pid} survived pool disposal."));
        await Until(async () => (await Sessions()).Length == 0);
    }

    [Fact]
    public async Task Cancelled_waiter_releases_its_database_session_without_starting_a_child()
    {
        // Actual session locks deliberately occupy both slots; no fake engine is started.
        var slots = new EngineSlots(fixture.Connection);
        await using var first = await slots.Acquire(CancellationToken.None);
        await using var second = await slots.Acquire(CancellationToken.None);
        await using var pool = Pool(); using var cancellation = new CancellationTokenSource();
        var waiting = pool.Search(Start, SearchBudget.Quick, null, 20, cancellation.Token);
        await Until(async () => (await Sessions()).Length == 3);
        Assert.Empty(pool.ProcessIds); Assert.False(waiting.IsCompleted);
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await Until(async () => (await Sessions()).Length == 2);
        Assert.Empty(pool.ProcessIds);
    }

    [Fact]
    public async Task Terminating_the_live_database_session_cancels_a_busy_child_then_a_new_session_recovers()
    {
        await using var pool = Pool();
        await pool.Search(Start, SearchBudget.Quick, null, 20, CancellationToken.None);
        var pid = Assert.Single(pool.ProcessIds); using var process = Process.GetProcessById(pid);
        var before = process.TotalProcessorTime;
        var busy = pool.Search(Start, new("session-interruption", 5_000_000, 15_000), null, 20, CancellationToken.None);
        await Until(() => { process.Refresh(); return Task.FromResult(process.TotalProcessorTime > before); });
        Assert.False(busy.IsCompleted);
        var session = Assert.Single(await Sessions());
        await using (var connection = new NpgsqlConnection(fixture.Connection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", connection);
            command.Parameters.AddWithValue("pid", session); Assert.Equal(true, await command.ExecuteScalarAsync());
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => busy.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(Gone(pid)); Assert.Empty(pool.ProcessIds);
        var recovered = await pool.Search(Start, SearchBudget.Quick, null, 20, CancellationToken.None);
        Assert.True(recovered.Complete); Assert.False(recovered.CacheHit); Assert.NotEqual(pid, Assert.Single(pool.ProcessIds));
    }

    [Fact]
    public async Task Externally_killed_idle_engine_is_reaped_and_replaced_without_reusing_its_cache()
    {
        await using var pool = Pool();
        await pool.Search(Start, SearchBudget.Quick, null, 20, CancellationToken.None);
        var pid = Assert.Single(pool.ProcessIds);
        using (var process = Process.GetProcessById(pid)) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        var recovered = await pool.Search(Start, SearchBudget.Quick, null, 20, CancellationToken.None);
        Assert.True(recovered.Complete); Assert.False(recovered.CacheHit);
        Assert.NotEqual(pid, Assert.Single(pool.ProcessIds)); Assert.True(Gone(pid));
    }

    [Fact]
    public async Task Pool_shutdown_cancels_active_work_and_releases_processes_and_physical_sessions()
    {
        var pool = Pool();
        try
        {
            await pool.Search(Start, SearchBudget.Quick, null, 20, CancellationToken.None);
            var pid = Assert.Single(pool.ProcessIds); using var process = Process.GetProcessById(pid);
            var before = process.TotalProcessorTime;
            var busy = pool.Search(Start, new("pool-shutdown", 5_000_000, 15_000), null, 20, CancellationToken.None);
            await Until(() => { process.Refresh(); return Task.FromResult(process.TotalProcessorTime > before); });
            await pool.DisposeAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => busy);
            Assert.True(Gone(pid)); Assert.Empty(pool.ProcessIds);
            await Until(async () => (await Sessions()).Length == 0);
        }
        finally { await pool.DisposeAsync(); }
    }
}
