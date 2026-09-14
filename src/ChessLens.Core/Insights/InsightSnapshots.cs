using System.Text.Json;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Core.Insights;

public sealed record InsightFilter(DateTimeOffset From, DateTimeOffset To, string? Source = null, string? Colour = null,
    string? Opening = null, string? TimeControl = null, int MaximumGames = 50)
{
    public void Validate()
    {
        if (To <= From || To - From > TimeSpan.FromDays(366) || MaximumGames is < 1 or > 200 ||
            Source is not (null or "pgn" or "chesscom" or "lichess") || Colour is not (null or "white" or "black") ||
            Opening?.Length > 2048 || TimeControl?.Length > 100) throw new ArgumentException("Choose a date range of up to 366 days and 1–200 games with valid filters.");
    }
}
public sealed class InsightSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public required string FilterJson { get; set; }
    public required string OptionsJson { get; set; }
    public string DetectorVersion { get; set; } = InsightRules.Version;
    public string State { get; set; } = "queued";
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed record InsightGroup(string Engine, string Profile, string AnalysisVersion, AnalysisOptions ClassificationSettings, Guid[] RunIds, InsightSummary Summary);
public sealed record InsightResult(InsightFilter Filter, InsightOptions Options, int MatchingGames, int SelectedGames, int MissingIdentityGames,
    int WithoutCompletedAnalysis, int UndatedGamesExcluded, bool SelectionCapped, InsightGroup[] Groups);

public sealed class InsightProcessor(LensDbContext db)
{
    public async Task<bool> RunNext(CancellationToken ct)
    {
        InsightSnapshot job;
        var token = Guid.NewGuid().ToString("N");
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.InsightSnapshots.Where(j => j.State == "running" && j.LeaseUntil < DateTimeOffset.UtcNow && j.Attempts >= 3)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "failed").SetProperty(j => j.Error, "Insight generation stopped after three interrupted attempts.")
                    .SetProperty(j => j.Version, j => j.Version + 1).SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            var rows = await db.InsightSnapshots.FromSqlRaw("""
                SELECT * FROM "InsightSnapshots" WHERE ("State" = 'queued' OR ("State" = 'running' AND "LeaseUntil" < now()))
                AND "Attempts" < 3 ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (rows.Count == 0) { await tx.CommitAsync(ct); return false; }
            job = rows[0]; job.State = "running"; job.Attempts++; job.LeaseToken = token; job.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(3);
            job.Version++; job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            var filter = JsonSerializer.Deserialize<InsightFilter>(job.FilterJson)!; filter.Validate();
            var options = JsonSerializer.Deserialize<InsightOptions>(job.OptionsJson)!; options.Validate();
            var result = await Generate(job.OwnerId, filter, options, deadline.Token);
            await db.InsightSnapshots.Where(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "completed").SetProperty(j => j.ResultJson, JsonSerializer.Serialize(result, (JsonSerializerOptions?)null))
                    .SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null).SetProperty(j => j.Version, j => j.Version + 1)
                    .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var permanent = error is ArgumentException or OperationCanceledException || job.Attempts >= 3;
            await db.InsightSnapshots.Where(j => j.Id == job.Id && j.LeaseToken == token && j.State == "running")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, permanent ? "failed" : "queued")
                    .SetProperty(j => j.Error, error is ArgumentException ? error.Message : "Insight generation interrupted. Try a smaller selection if its processing budget was reached.")
                    .SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null).SetProperty(j => j.Version, j => j.Version + 1)
                    .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow), ct);
            if (!permanent) throw;
        }
        return true;
    }

    private async Task<InsightResult> Generate(string owner, InsightFilter filter, InsightOptions options, CancellationToken ct)
    {
        var source = db.UserGames.AsNoTracking().Where(g => g.OwnerId == owner);
        if (filter.Source is not null) source = source.Where(g => g.Game.Provider == filter.Source);
        if (filter.Colour is not null) source = source.Where(g => g.PlayerColour == filter.Colour);
        if (filter.Opening is not null) source = source.Where(g => g.Game.Opening == filter.Opening);
        if (filter.TimeControl is not null) source = source.Where(g => g.Game.TimeControl == filter.TimeControl);
        var undated = await source.CountAsync(g => g.Game.PlayedAt == null, ct);
        var dated = source.Where(g => g.Game.PlayedAt >= filter.From && g.Game.PlayedAt < filter.To);
        var count = await dated.CountAsync(ct);
        var selection = dated.OrderByDescending(g => g.Game.PlayedAt).ThenBy(g => g.GameId).Take(filter.MaximumGames);
        if (await selection.SumAsync(g => g.Game.Moves.Count, ct) > 20_000) throw new ArgumentException("Choose fewer games: an insight snapshot is limited to 20,000 source half-moves.");
        // Raw PGN/comments are not needed for rules-based analytics and can be
        // megabytes per game. Project only the evidence inputs within the cap.
        var selected = await selection.Select(g => new UserGame { OwnerId = g.OwnerId, GameId = g.GameId, PlayerColour = g.PlayerColour,
            Game = new SourceGame { Id = g.GameId, Fingerprint = g.Game.Fingerprint, White = g.Game.White, Black = g.Game.Black,
                Result = g.Game.Result, Provider = g.Game.Provider, InitialFen = g.Game.InitialFen, PlayedAt = g.Game.PlayedAt,
                Opening = g.Game.Opening, TimeControl = g.Game.TimeControl, RawPgn = "", HeadersJson = "{}", Moves = g.Game.Moves.ToList() } }).ToListAsync(ct);
        var identities = selected.Where(g => g.PlayerColour is "white" or "black").ToArray();
        var ids = identities.Select(g => g.GameId).ToArray();
        var latest = await db.AnalysisRuns.Where(r => r.OwnerId == owner && ids.Contains(r.GameId) && r.State == "completed")
            .GroupBy(r => r.GameId).Select(g => g.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Select(r => r.Id).First()).ToArrayAsync(ct);
        var runs = await db.AnalysisRuns.AsNoTracking().Where(r => latest.Contains(r.Id)).Include(r => r.Moves).ToListAsync(ct);
        var groups = runs.GroupBy(r => JsonSerializer.Serialize(new { r.EngineVersion, r.Profile, r.AnalysisVersion, r.SettingsJson,
                classification = r.ClassificationSettingsJson ?? JsonSerializer.Serialize(new AnalysisOptions()) }))
            .Select(group =>
            {
                var first = group.First();
                var games = group.Select(run => { var association = identities.Single(g => g.GameId == run.GameId); return new InsightGame(association.Game, association.PlayerColour!, run); }).ToArray();
                return new InsightGroup(first.EngineVersion ?? "Engine version unavailable", first.Profile, first.AnalysisVersion,
                    JsonSerializer.Deserialize<AnalysisOptions>(first.ClassificationSettingsJson ?? "{}")!, group.Select(r => r.Id).ToArray(), InsightRules.Build(games, options, ct));
            }).OrderByDescending(g => g.Summary.AnalysedGames).ToArray();
        return new(filter, options, count, selected.Count, selected.Count - identities.Length, identities.Length - runs.Count, undated, count > selected.Count, groups);
    }
}
