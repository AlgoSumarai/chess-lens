using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChessLens.Core.Training;

public sealed class PuzzleProcessor(LensDbContext db, IChessEngine engine, ILogger<PuzzleProcessor>? logger = null)
{
    public async Task<bool> RunNext(CancellationToken ct)
    {
        Puzzle job; var token = Guid.NewGuid().ToString("N");
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Puzzles.Where(j => j.State == "running" && j.LeaseUntil < DateTimeOffset.UtcNow && j.Attempts >= 3)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "failed").SetProperty(j => j.Error, "Puzzle validation stopped after three interrupted attempts.")
                    .SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            var rows = await db.Puzzles.FromSqlRaw("""
                SELECT * FROM "Puzzles" WHERE ("State" = 'queued' OR ("State" = 'running' AND "LeaseUntil" < now()))
                AND "Attempts" < 3 ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (rows.Count == 0) { await tx.CommitAsync(ct); return false; }
            job = rows[0]; job.State = "running"; job.Attempts++; job.LeaseToken = token; job.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(5);
            job.Version++; job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(3));
            var run = await db.AnalysisRuns.AsNoTracking().Include(r => r.Moves.Where(m => m.Ply == job.Ply))
                .SingleAsync(r => r.Id == job.RunId && r.OwnerId == job.OwnerId && r.GameId == job.GameId, deadline.Token);
            var association = await db.UserGames.AsNoTracking().Include(g => g.Game).ThenInclude(g => g.Moves)
                .SingleAsync(g => g.OwnerId == job.OwnerId && g.GameId == job.GameId, deadline.Token);
            var move = run.Moves.Single();
            if (run.State != "completed" || !Eligible(move) || association.PlayerColour != move.Side)
                throw new ArgumentException("Select your player and use a completed, verified mistake before generating a puzzle.");
            var sourceMove = association.Game.Moves.Single(m => m.Ply == job.Ply);
            if (sourceMove.FenBefore != move.FenBefore || sourceMove.Uci != move.PlayedMove)
                throw new ArgumentException("The source position does not match the review evidence.");
            var position = new EnginePosition(association.Game.InitialFen, association.Game.Moves.OrderBy(m => m.Ply).Where(m => m.Ply < job.Ply).Select(m => m.Uci).ToArray());
            var result = await new PuzzleValidator(engine).Validate(position, deadline.Token);
            if (result.Diagnostic is not null) logger?.LogWarning("Puzzle {PuzzleId} rejected engine evidence: {Diagnostic}", job.Id, result.Diagnostic);
            if (result.Diagnostic is not null)
            {
                await db.Puzzles.Where(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running")
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, job.Attempts < 3 ? "queued" : "failed")
                        .SetProperty(j => j.Error, job.Attempts < 3 ? "Engine evidence was incomplete. Retrying within the validation limit." : "Three attempts could not produce complete exact engine evidence.")
                        .SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null).SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
                return true;
            }
            var json = result.Solution is null ? null : JsonSerializer.Serialize(result.Solution);
            if (json?.Length > 2_000_000) throw new ArgumentException("The solution exceeded its evidence storage budget.");
            await db.Puzzles.Where(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, result.Solution is null ? "rejected" : "ready")
                    .SetProperty(j => j.SolutionJson, json).SetProperty(j => j.Error, result.Rejection)
                    .SetProperty(j => j.ReviewDueAt, result.Solution == null ? (DateTimeOffset?)null : DateTimeOffset.UtcNow)
                    .SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null).SetProperty(j => j.Version, j => j.Version + 1)
                    .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var permanent = error is ArgumentException or OperationCanceledException || job.Attempts >= 3;
            await db.Puzzles.Where(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, permanent ? "failed" : "queued")
                    .SetProperty(j => j.Error, error is ArgumentException ? error.Message : "Puzzle validation was interrupted or reached its three-minute budget.")
                    .SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null).SetProperty(j => j.Version, j => j.Version + 1)
                    .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            if (!permanent) throw;
        }
        return true;
    }
    public static bool Eligible(MoveAnalysis m) => m.Complete && !m.Provisional &&
        (m.CentipawnLoss >= 100 || m.MateTransition is "missed-forced-mate" or "allowed-forced-mate") &&
        m.Classification is "inaccuracy" or "mistake" or "blunder";
}
