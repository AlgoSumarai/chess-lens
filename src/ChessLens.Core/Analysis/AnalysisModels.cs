namespace ChessLens.Core.Analysis;

public sealed class AnalysisRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public Guid GameId { get; set; }
    public string Profile { get; set; } = "quick";
    public string AnalysisVersion { get; set; } = "chesslens-analysis-v3";
    public string? ClassificationSettingsJson { get; set; }
    public string State { get; set; } = "queued";
    public string? EngineVersion { get; set; }
    public string? SettingsJson { get; set; }
    public int CompletedPlies { get; set; }
    public int TotalPlies { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public long PeakEngineMemoryBytes { get; set; }
    public int Attempts { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public bool CancelRequested { get; set; }
    public string? Error { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<MoveAnalysis> Moves { get; set; } = [];
}

public sealed class MoveAnalysis
{
    public Guid RunId { get; set; }
    public int Ply { get; set; }
    public required string FenBefore { get; set; }
    public required string PlayedMove { get; set; }
    public required string Side { get; set; }
    public required string BestMove { get; set; }
    public required string BestScoreKind { get; set; }
    public int BestScoreValue { get; set; }
    public required string PlayedScoreKind { get; set; }
    public int PlayedScoreValue { get; set; }
    public int? CentipawnLoss { get; set; }
    public string? MateTransition { get; set; }
    public required string Classification { get; set; }
    public bool Provisional { get; set; }
    public required string BestSearchJson { get; set; }
    public required string PlayedSearchJson { get; set; }
    public required string BudgetJson { get; set; }
    public required string CacheIdentity { get; set; }
    public int Depth { get; set; }
    public long Nodes { get; set; }
    public bool Complete { get; set; }
}
