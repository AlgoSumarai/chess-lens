namespace ChessLens.Core.Games;

public sealed class SourceGame
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Fingerprint { get; set; }
    public string Provider { get; set; } = "pgn";
    public string? ExternalId { get; set; }
    public string? Url { get; set; }
    public required string White { get; set; }
    public required string Black { get; set; }
    public int? WhiteRating { get; set; }
    public int? BlackRating { get; set; }
    public required string Result { get; set; }
    public string? Termination { get; set; }
    public string? TimeControl { get; set; }
    public string? Opening { get; set; }
    public string? Eco { get; set; }
    public DateTimeOffset? PlayedAt { get; set; }
    public required string RawPgn { get; set; }
    public required string HeadersJson { get; set; }
    public required string InitialFen { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<GameMove> Moves { get; set; } = [];
}

public sealed class GameMove
{
    public Guid GameId { get; set; }
    public int Ply { get; set; }
    public required string San { get; set; }
    public required string Uci { get; set; }
    public required string FenBefore { get; set; }
    public required string FenAfter { get; set; }
    public required string Side { get; set; }
    public double? ClockSeconds { get; set; }
}

public sealed class UserGame
{
    public required string OwnerId { get; set; }
    public Guid GameId { get; set; }
    public SourceGame Game { get; set; } = null!;
    public string? PlayerColour { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
