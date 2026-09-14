using System.Text.Json;
using ChessLens.Core.Games;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessLens.Core.Analysis;

public sealed class AnalysisProcessor(LensDbContext db, IChessEngine engine, IOptions<AnalysisOptions>? options = null)
{
    private const long MaxGameMilliseconds = 600_000;
    private readonly AnalysisOptions configuredThresholds = options?.Value ?? new();
    public async Task<bool> RunNext(CancellationToken ct)
    {
        configuredThresholds.Validate();
        var token = Guid.NewGuid().ToString("N");
        Guid id;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.AnalysisRuns.Where(j => j.State == "running" && j.LeaseUntil < DateTimeOffset.UtcNow && j.Attempts >= 4)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "failed").SetProperty(j => j.Error, "Analysis stopped after four interrupted attempts.")
                    .SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            var rows = await db.AnalysisRuns.FromSqlRaw("""
                SELECT * FROM "AnalysisRuns" WHERE ("State" = 'queued' OR ("State" = 'running' AND "LeaseUntil" < now()))
                AND "Attempts" < 4 ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            var run = rows.SingleOrDefault();
            if (run is null) { await tx.CommitAsync(ct); return false; }
            id = run.Id;
            // A resumed run retains its original classifier even if the worker
            // configuration changes. Pre-snapshot reviews used the v1 defaults.
            run.ClassificationSettingsJson ??= JsonSerializer.Serialize(run.CompletedPlies > 0 ? new AnalysisOptions() : configuredThresholds);
            run.State = "running"; run.Attempts++; run.LeaseToken = token; run.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(5); run.Error = null;
            Touch(run);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        try
        {
            var run = await db.AnalysisRuns.AsNoTracking().SingleAsync(r => r.Id == id, ct);
            var thresholds = JsonSerializer.Deserialize<AnalysisOptions>(run.ClassificationSettingsJson!)!;
            thresholds.Validate();
            var game = await db.Games.AsNoTracking().Include(g => g.Moves).SingleAsync(g => g.Id == run.GameId, ct);
            var moves = game.Moves.OrderBy(m => m.Ply).ToArray();
            while (true)
            {
                db.ChangeTracker.Clear();
                run = await db.AnalysisRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
                if (run is null || run.LeaseToken != token || run.State != "running") return true;
                if (run.CancelRequested || run.CompletedPlies >= moves.Length)
                {
                    await db.AnalysisRuns.Where(r => r.Id == id && r.LeaseToken == token && r.State == "running")
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, run.CancelRequested ? "cancelled" : "completed")
                            .SetProperty(r => r.LeaseUntil, (DateTimeOffset?)null).SetProperty(r => r.Version, r => r.Version + 1)
                            .SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), ct);
                    return true;
                }
                if (run.ElapsedMilliseconds >= MaxGameMilliseconds) throw new ArgumentException("This game reached its ten-minute engine budget. Partial results remain available.");
                var move = moves[run.CompletedPlies];
                var position = new EnginePosition(game.InitialFen, moves.Take(run.CompletedPlies).Select(m => m.Uci).ToArray());
                var budget = run.Profile == "deep" ? SearchBudget.Deep : SearchBudget.Quick;
                var best = await engine.Search(position, budget, null, 20, ct);
                if (run.EngineVersion is not null && (run.EngineVersion != best.Engine || run.SettingsJson != best.SettingsJson))
                    throw new ArgumentException("Engine version or settings changed during this review. Start a new review to keep results comparable.");
                var played = best.BestMove == move.Uci ? best : await engine.Search(position, budget, [move.Uci], 20, ct);
                var (bestLine, playedLine) = ValidLines(position, best, played);
                var classification = thresholds.Compare(bestLine.Score, playedLine.Score, run.Profile == "quick");
                long elapsed = WorkTime(best) + (ReferenceEquals(best, played) ? 0 : WorkTime(played));
                object? verification = null;
                if (classification.Label is "inaccuracy" or "mistake" or "blunder" or "unresolved" || bestLine.Bound || playedLine.Bound)
                {
                    var first = classification;
                    var firstBestMove = best.BestMove;
                    var firstBestScore = bestLine.Score;
                    var firstPlayedScore = playedLine.Score;
                    // Verify suspicious results with a second, deeper and independently cleared search pair.
                    budget = SearchBudget.Deep with { Nodes = run.Profile == "deep" ? 500_000 : 250_000 };
                    best = await engine.Search(position, budget, null, 20, ct);
                    played = best.BestMove == move.Uci ? best : await engine.Search(position, budget, [move.Uci], 20, ct);
                    (bestLine, playedLine) = ValidLines(position, best, played);
                    elapsed += WorkTime(best) + (ReferenceEquals(best, played) ? 0 : WorkTime(played));
                    classification = thresholds.Compare(bestLine.Score, playedLine.Score, false);
                    var stableClassification = first.Label == classification.Label && first.MateTransition == classification.MateTransition;
                    var stableLoss = first.CentipawnLoss is null && classification.CentipawnLoss is null ||
                        first.CentipawnLoss is { } a && classification.CentipawnLoss is { } b && Math.Abs(a - b) <= 75;
                    var stableBestScore = StableScore(firstBestScore, bestLine.Score);
                    var stablePlayedScore = StableScore(firstPlayedScore, playedLine.Score);
                    // Equivalent alternatives may exchange rank without changing
                    // whether the played move loses material or allows mate.
                    var currentStabilityRule = run.AnalysisVersion == "chesslens-analysis-v3";
                    var stable = stableClassification && stableLoss && (currentStabilityRule
                        ? stableBestScore && stablePlayedScore : firstBestMove == best.BestMove);
                    verification = new { version = currentStabilityRule ? "score-stability-v1" : "legacy-best-move-stability", firstBestMove, firstBestScore, firstPlayedScore,
                        finalBestMove = best.BestMove, finalBestScore = bestLine.Score, finalPlayedScore = playedLine.Score,
                        stableClassification, stableLoss, stableBestScore, stablePlayedScore };
                    classification = classification with { NeedsVerification = classification.NeedsVerification || !stable };
                }
                var analysis = new MoveAnalysis
                {
                    RunId = id, Ply = move.Ply, FenBefore = move.FenBefore, PlayedMove = move.Uci, Side = move.Side, BestMove = best.BestMove,
                    BestScoreKind = bestLine.Score.Kind, BestScoreValue = bestLine.Score.Value,
                    PlayedScoreKind = playedLine.Score.Kind, PlayedScoreValue = playedLine.Score.Value,
                    CentipawnLoss = classification.CentipawnLoss, MateTransition = classification.MateTransition, Classification = classification.Label,
                    Provisional = classification.NeedsVerification || bestLine.Bound || playedLine.Bound || !best.Complete || !played.Complete,
                    BestSearchJson = JsonSerializer.Serialize(best), PlayedSearchJson = JsonSerializer.Serialize(played),
                    BudgetJson = JsonSerializer.Serialize(new { budget.Profile, budget.Nodes, budget.MaxMilliseconds, budget.MultiPv, verification }),
                    CacheIdentity = position.CacheIdentity(best.Engine, best.SettingsJson, budget, [move.Uci]),
                    Depth = Math.Min(bestLine.Depth, playedLine.Depth), Nodes = bestLine.Nodes + (ReferenceEquals(best, played) ? 0 : playedLine.Nodes),
                    Complete = best.Complete && played.Complete
                };
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var current = await db.AnalysisRuns.FromSqlInterpolated($"SELECT * FROM \"AnalysisRuns\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
                if (current is null || current.LeaseToken != token || current.State != "running") return true;
                if (current.CancelRequested) { await tx.RollbackAsync(ct); continue; }
                if (current.CompletedPlies != run.CompletedPlies) throw new InvalidOperationException("Analysis checkpoint changed unexpectedly.");
                db.MoveAnalyses.Add(analysis);
                current.CompletedPlies++; current.ElapsedMilliseconds += elapsed; current.EngineVersion = best.Engine; current.SettingsJson = best.SettingsJson;
                current.PeakEngineMemoryBytes = Math.Max(current.PeakEngineMemoryBytes, Math.Max(best.PeakMemoryBytes, played.PeakMemoryBytes));
                current.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(5); Touch(current);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            db.ChangeTracker.Clear();
            var run = await db.AnalysisRuns.SingleOrDefaultAsync(r => r.Id == id && r.LeaseToken == token, ct);
            if (run is not null)
            {
                run.State = e is ArgumentException || run.Attempts >= 4 ? "failed" : "queued";
                run.Error = e is ArgumentException ? e.Message : "Engine analysis interrupted. Retrying within the configured limit.";
                run.LeaseUntil = null; Touch(run); await db.SaveChangesAsync(ct);
            }
            if (e is not ArgumentException) throw;
        }
        return true;
    }

    private static (SearchLine Best, SearchLine Played) ValidLines(EnginePosition position, SearchResult best, SearchResult played)
    {
        var b = best.Lines.SingleOrDefault(l => l.Rank == 1) ?? throw new InvalidOperationException("Engine returned no main variation.");
        var p = played.Lines.SingleOrDefault(l => l.Rank == 1) ?? throw new InvalidOperationException("Engine returned no played continuation.");
        foreach (var line in new[] { b, p }) ChessRules.Replay(position.InitialFen, position.History.Concat(line.Pv));
        return (b, p);
    }
    private static void Touch(AnalysisRun run) { run.Version++; run.UpdatedAt = DateTimeOffset.UtcNow; }
    private static long WorkTime(SearchResult result) => result.CacheHit ? 0 : result.ElapsedMilliseconds;
    private static bool StableScore(EngineScore first, EngineScore final) => first.Kind == final.Kind &&
        (first.Kind == "cp" ? Math.Abs((long)first.Value - final.Value) <= 75 : first.Kind == "mate" && Math.Sign(first.Value) == Math.Sign(final.Value));
}
