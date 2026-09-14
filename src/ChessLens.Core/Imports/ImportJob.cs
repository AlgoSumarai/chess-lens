namespace ChessLens.Core.Imports;

public sealed class ImportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public string Source { get; set; } = "pgn";
    public string State { get; set; } = "queued";
    public required string PayloadJson { get; set; }
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public int Processed { get; set; }
    public int Total { get; set; }
    public string FailuresJson { get; set; } = "[]";
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? LeaseToken { get; set; }
    public long Version { get; set; }
    public bool CancelRequested { get; set; }
}

public sealed record PgnImportRequest(string Pgn, string? PlayerName = null, int MaxGames = 50);
public sealed record ImportFailure(int GameNumber, string Message);
