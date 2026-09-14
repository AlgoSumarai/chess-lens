using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChessLens.Core.Games;

namespace ChessLens.Core.Analysis;

/// <summary>A single supervised UCI process; callers must serialize searches.</summary>
public sealed partial class UciEngine : IChessEngine, IAsyncDisposable
{
    private readonly Process process;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task stderr;
    private readonly Dictionary<string, string> options = [];
    private readonly int threads;
    private readonly int hashMb;
    // Per-process cache: changing the binary or worker options necessarily starts
    // an empty cache. Both entry count and serialized result bytes are bounded.
    private readonly Dictionary<string, (SearchResult Result, DateTimeOffset Expires, int Bytes)> cache = [];
    private readonly Queue<string> cacheOrder = new();
    private int cacheBytes;
    private string name = "Unknown engine";
    private bool healthy = true;
    public bool IsHealthy => healthy && !process.HasExited;
    public int ProcessId => process.Id;

    private UciEngine(Process process, int threads, int hashMb)
    {
        this.process = process; this.threads = threads; this.hashMb = hashMb;
        stderr = DrainError(lifetime.Token);
    }

    public static async Task<UciEngine> Start(string executable, int threads, int hashMb, CancellationToken ct, int startupTimeoutSeconds = 120)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new InvalidOperationException("Configure an absolute path to a Stockfish executable.");
        if (threads is < 1 or > 4 || hashMb is < 16 or > 512) throw new ArgumentException("Engine threads/hash exceed worker bounds.");
        if (startupTimeoutSeconds is < 5 or > 120) throw new ArgumentException("Engine startup timeout must be between 5 and 120 seconds.");
        var process = new Process { StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)!
        } };
        if (!process.Start()) throw new InvalidOperationException("Stockfish could not start.");
        var engine = new UciEngine(process, threads, hashMb);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(startupTimeoutSeconds));
            await engine.Send("uci", deadline.Token);
            for (var count = 0; count < 200; count++)
            {
                var line = await engine.Read(deadline.Token);
                if (line == "uciok") break;
                if (line.StartsWith("id name ", StringComparison.Ordinal)) engine.name = line[8..];
                var match = OptionRegex().Match(line);
                if (match.Success) engine.options[match.Groups[1].Value] = line;
                if (count == 199) throw new InvalidOperationException("Engine did not acknowledge the UCI handshake.");
            }
            await engine.Option("Threads", threads.ToString(CultureInfo.InvariantCulture), deadline.Token);
            await engine.Option("Hash", hashMb.ToString(CultureInfo.InvariantCulture), deadline.Token);
            await engine.Option("Ponder", "false", deadline.Token);
            await engine.Option("UCI_Chess960", "false", deadline.Token);
            await engine.Ready(deadline.Token);
            return engine;
        }
        catch { await engine.DisposeAsync(); throw; }
    }

    public async Task<SearchResult> Search(EnginePosition position, SearchBudget budget, string[]? searchMoves, int skill, CancellationToken ct)
    {
        budget.Validate();
        if (skill is < 0 or > 20 || position.History.Length > 1100) throw new ArgumentException("Invalid engine request.");
        var board = ChessRules.Replay(position.InitialFen, position.History);
        var legal = ChessRules.View(board).LegalMoves;
        if (searchMoves is not null && (searchMoves.Length == 0 || searchMoves.Any(m => !legal.Contains(m))))
            throw new ArgumentException("Restricted engine moves must be legal in this position.");
        // Canonicalize FEN through the rules library before writing a protocol command.
        var initialFen = ChessRules.Replay(position.InitialFen, []).ToFen();
        ct.ThrowIfCancellationRequested();
        if (!IsHealthy) throw new InvalidOperationException("Stockfish is no longer running.");
        var settings = JsonSerializer.Serialize(new { adapterVersion = "uci-batches-v2", threads, hashMb, skill, multiPv = budget.MultiPv, options });
        var cacheKey = position.CacheIdentity(name, settings, budget, searchMoves);
        while (cacheOrder.TryPeek(out var oldestKey) && cache[oldestKey].Expires <= DateTimeOffset.UtcNow)
        {
            cacheOrder.Dequeue(); cacheBytes -= cache[oldestKey].Bytes; cache.Remove(oldestKey);
        }
        if (cache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
            return Copy(cached.Result) with { CacheHit = true };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(budget.MaxMilliseconds + 10_000));
        var clock = Stopwatch.StartNew();
        try
        {
            await Option("MultiPV", budget.MultiPv.ToString(CultureInfo.InvariantCulture), deadline.Token);
            await Option("Skill Level", skill.ToString(CultureInfo.InvariantCulture), deadline.Token);
            await Option("UCI_LimitStrength", "false", deadline.Token);
            await Send("ucinewgame", deadline.Token); // Comparable searches have no inherited transposition table advantage.
            await Ready(deadline.Token);
            var root = initialFen == ChessRules.InitialFen ? "startpos" : "fen " + initialFen;
            await Send("position " + root + (position.History.Length > 0 ? " moves " + string.Join(' ', position.History) : ""), deadline.Token);
            await Send($"go nodes {budget.Nodes} movetime {budget.MaxMilliseconds}" + (searchMoves is { Length: > 0 } ? " searchmoves " + string.Join(' ', searchMoves) : ""), deadline.Token);
            var expectedLines = Math.Min(budget.MultiPv, searchMoves?.Length ?? legal.Length);
            var iterations = new UciIterations(expectedLines);
            for (var count = 0; count < 20_000; count++)
            {
                var line = await Read(deadline.Token);
                if (line.StartsWith("bestmove ", StringComparison.Ordinal))
                {
                    var bestMove = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
                    if (bestMove is not "(none)" and not "0000" && !legal.Contains(bestMove)) throw new InvalidOperationException("Engine returned an illegal move.");
                    if (searchMoves is not null && !searchMoves.Contains(bestMove)) throw new InvalidOperationException("Engine ignored the restricted move request.");
                    // A node/time cutoff may leave a deeper aspiration bound or
                    // only part of a MultiPV iteration. Prefer the latest complete
                    // exact iteration that supports the engine's final bestmove.
                    var completedIteration = iterations.CompleteFor(bestMove);
                    var lines = completedIteration ?? iterations.Latest;
                    foreach (var variation in lines)
                    {
                        if (variation.Pv.Length > 0 && !legal.Contains(variation.Pv[0])) throw new InvalidOperationException("Engine returned an illegal variation.");
                        ChessRules.Replay(position.InitialFen, position.History.Concat(variation.Pv));
                    }
                    var complete = legal.Length == 0 || completedIteration is not null;
                    process.Refresh();
                    var result = new SearchResult(name, settings, bestMove, lines.OrderBy(l => l.Rank).ToArray(), (int)clock.ElapsedMilliseconds,
                        process.PeakWorkingSet64, complete);
                    if (result.Complete && result.Lines.Count > 0 && result.Lines.All(l => !l.Bound)) Remember(cacheKey, result);
                    return result;
                }
                var parsed = ParseInfo(line);
                if (parsed is not null)
                {
                    iterations.Add(parsed);
                }
            }
            throw new InvalidOperationException("Engine exceeded the protocol output limit.");
        }
        catch
        {
            healthy = false;
            // Killing the complete process tree covers engine crashes, cancellation and protocol deadlines.
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private void Remember(string key, SearchResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result).Length;
        if (bytes > 1_048_576) return;
        // Expired entries can remain in the FIFO until eviction; never enqueue a
        // duplicate key, which could otherwise evict a newer value accidentally.
        if (cache.ContainsKey(key)) return;
        while (cache.Count >= 64 || cacheBytes + bytes > 1_048_576)
        {
            var oldest = cacheOrder.Dequeue();
            cacheBytes -= cache[oldest].Bytes;
            cache.Remove(oldest);
        }
        cache.Add(key, (Copy(result), DateTimeOffset.UtcNow.AddMinutes(10), bytes));
        cacheOrder.Enqueue(key);
        cacheBytes += bytes;
    }
    private static SearchResult Copy(SearchResult result) => result with
    {
        Lines = result.Lines.Select(l => l with { Pv = (string[])l.Pv.Clone() }).ToArray()
    };

    public static SearchLine? ParseInfo(string line)
    {
        if (!line.StartsWith("info depth ", StringComparison.Ordinal)) return null;
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int At(string key) => Array.IndexOf(words, key);
        long Value(string key, long fallback = 0) => At(key) is var index && index >= 0 && index + 1 < words.Length && long.TryParse(words[index + 1], out var n) ? n : fallback;
        var score = At("score"); var pv = At("pv");
        if (score < 0 || score + 2 >= words.Length || words[score + 1] is not ("cp" or "mate") || !int.TryParse(words[score + 2], out var value)) return null;
        return new((int)Value("multipv", 1), (int)Value("depth"), Value("nodes"), (int)Value("time"), new(words[score + 1], value),
            pv < 0 ? [] : words[(pv + 1)..], At("lowerbound") >= 0 || At("upperbound") >= 0);
    }

    private async Task Option(string key, string value, CancellationToken ct)
    {
        if (!options.ContainsKey(key)) throw new InvalidOperationException($"Engine does not support required UCI option: {key}.");
        await Send($"setoption name {key} value {value}", ct);
    }
    private async Task Ready(CancellationToken ct)
    {
        await Send("isready", ct);
        for (var i = 0; i < 100; i++) if (await Read(ct) == "readyok") return;
        throw new InvalidOperationException("Engine did not acknowledge readiness.");
    }
    private async Task Send(string command, CancellationToken ct)
    {
        if (command.Contains('\n') || command.Contains('\r')) throw new ArgumentException("Invalid protocol command.");
        await process.StandardInput.WriteLineAsync(command.AsMemory(), ct);
        await process.StandardInput.FlushAsync(ct);
    }
    private async Task<string> Read(CancellationToken ct)
    {
        var line = await process.StandardOutput.ReadLineAsync(ct) ?? throw new InvalidOperationException("Stockfish exited unexpectedly.");
        if (line.Length > 100_000 || line.Contains("CRITICAL ERROR", StringComparison.Ordinal)) throw new InvalidOperationException("Stockfish rejected the supplied position or command.");
        return line;
    }
    private async Task DrainError(CancellationToken ct)
    {
        try { while (await process.StandardError.ReadLineAsync(ct) is not null) { } }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        healthy = false;
        lifetime.Cancel();
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        await stderr;
        process.Dispose(); lifetime.Dispose();
    }
    [GeneratedRegex(@"^option name (.+) type (?:spin|check|string|button|combo)\b")]
    private static partial Regex OptionRegex();
}
