using ChessLens.Core.Analysis;

namespace ChessLens.Core.Training;

public sealed class PracticeSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public Guid GameId { get; set; }
    public Guid RunId { get; set; }
    public int Ply { get; set; }
    public Guid? WeaknessSnapshotId { get; set; }
    public required string Side { get; set; }
    public required string StartingPositionJson { get; set; }
    public required string SourceEvidenceJson { get; set; }
    public required string ObjectiveJson { get; set; }
    public string RulesVersion { get; set; } = PracticeRules.Version;
    public Guid CurrentAttemptId { get; set; }
    public string State { get; set; } = "preparing";
    public long Version { get; set; } = 1;
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PracticeAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public int Number { get; set; } = 1;
    public string State { get; set; } = "active";
    public string HistoryJson { get; set; } = "[]";
    public string GradesJson { get; set; } = "[]";
    public string AllGradesJson { get; set; } = "[]";
    public string JournalJson { get; set; } = "[]";
    public string? InitialCheckJson { get; set; }
    public required string ObjectiveJson { get; set; }
    public string OpponentSearchesJson { get; set; } = "[]";
    public string HintEvidenceJson { get; set; } = "[]";
    public string? Hint { get; set; }
    public string? FinalCheckJson { get; set; }
    public string? Conclusion { get; set; }
    public int HintsUsed { get; set; }
    public int UndosUsed { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class PracticeJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid AttemptId { get; set; }
    public long SessionVersion { get; set; }
    public required string Kind { get; set; }
    public string State { get; set; } = "queued";
    public int Attempts { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Account-level admission ledger survives removal of imported games.</summary>
public sealed class ResourceUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public required string Kind { get; set; }
    public int Units { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record PracticeObjective(string Kind, string Title, string Instruction, int Decisions = 3, int MaximumLoss = 100, int MinimumAdvantage = 100);
public sealed record PracticeSourceEvidence(string AnalysisVersion, string? ClassificationSettings, string Classification,
    string PlayedMove, EngineScore BestScore, EngineScore PlayedScore, SearchResult BestSearch, SearchResult PlayedSearch);
public sealed record PracticeJournal(string Action, string? Move, int HistoryLength, DateTimeOffset At);
public sealed record PracticeSearch(EnginePosition Position, SearchBudget Budget, string[]? RestrictedMoves, SearchResult Result);
public sealed record PracticePair(PracticeSearch First, PracticeSearch Final);
public sealed record PracticeGrade(int Decision, string Move, bool Accepted, string Reason, PracticePair Best, PracticePair Played);
public sealed record PracticeTurn(string[] History, PracticeGrade Grade, bool Finished, string? Outcome, string? Terminal, PracticeSearch? Opponent,
    PracticePair? FinalCheck = null, string? Conclusion = null);
