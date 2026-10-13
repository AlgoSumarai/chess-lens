using ChessLens.Core.Games;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Core.Imports;

/// <summary>Called inside the import job's locked transaction. Source identity is globally serialized.</summary>
public static class ImportStore
{
    public static async Task Save(LensDbContext db, ImportJob job, SourceGame parsed, string? player, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({parsed.Fingerprint}, 0))", ct);
        var existing = await db.Games.Include(g => g.Moves).SingleOrDefaultAsync(g => g.Fingerprint == parsed.Fingerprint, ct);
        if (existing is not null && (existing.InitialFen != parsed.InitialFen || existing.Result != parsed.Result ||
            !existing.Moves.OrderBy(m => m.Ply).Select(m => m.Uci).SequenceEqual(parsed.Moves.OrderBy(m => m.Ply).Select(m => m.Uci))))
            throw new ArgumentException("This source identifier now contains different moves or a different result. The original game was retained; this record was excluded.");
        var game = existing ?? parsed;
        if (existing is null) db.Games.Add(game);
        if (await db.UserGames.AnyAsync(g => g.OwnerId == job.OwnerId && g.GameId == game.Id, ct)) job.Duplicates++;
        else
        {
            db.UserGames.Add(new UserGame { OwnerId = job.OwnerId, GameId = game.Id, PlayerColour = PgnReader.Identify(game, player) });
            job.Imported++;
        }
        if (!await db.ImportJobGames.AnyAsync(g => g.JobId == job.Id && g.GameId == game.Id, ct))
            db.ImportJobGames.Add(new ImportJobGame { JobId = job.Id, OwnerId = job.OwnerId, GameId = game.Id, Ordinal = job.Processed + 1 });
    }
}
