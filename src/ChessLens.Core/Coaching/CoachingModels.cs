namespace ChessLens.Core.Coaching;

public sealed class CoachingOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 10;
    public int MaximumOutputTokens { get; set; } = 512;
    public int DailyOwnerRequests { get; set; } = 20;
    public int DailyGlobalRequests { get; set; } = 100;
    public long DailyOwnerMicroDollars { get; set; }
    public long DailyGlobalMicroDollars { get; set; }
    public int InputMicroDollarsPerToken { get; set; }
    public int OutputMicroDollarsPerToken { get; set; }
    public bool Configured => Enabled && ApiKey.Length > 0 && Model.Length > 0 && DailyOwnerMicroDollars > 0 && DailyGlobalMicroDollars > 0 &&
        InputMicroDollarsPerToken > 0 && OutputMicroDollarsPerToken > 0;
    public void Validate()
    {
        if (TimeoutSeconds is < 1 or > 20 || MaximumOutputTokens is < 128 or > 1024 || DailyOwnerRequests is < 1 or > 100 ||
            DailyGlobalRequests is < 1 or > 1000 || DailyOwnerMicroDollars is < 0 or > 100_000_000 || DailyGlobalMicroDollars is < 0 or > 1_000_000_000 ||
            InputMicroDollarsPerToken is < 0 or > 1000 || OutputMicroDollarsPerToken is < 0 or > 1000 ||
            Model.Length > 80 || Model.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.')))
            throw new ArgumentException("Coaching provider settings exceed their supported request/cost bounds.");
    }
}

public sealed class CoachingJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public Guid GameId { get; set; }
    public Guid RunId { get; set; }
    public int Ply { get; set; }
    public required string CacheIdentity { get; set; }
    public required string EvidenceJson { get; set; }
    public string State { get; set; } = "queued";
    public bool ModelRequested { get; set; }
    public bool ModelAttempted { get; set; }
    public string? Model { get; set; }
    public string PromptVersion { get; set; } = CoachingRules.Version;
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Accounting survives imported-data deletion so deletion cannot reset the paid-work budget.
public sealed class CoachingUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public Guid? JobId { get; set; }
    public long ReservedMicroDollars { get; set; }
    public int MaximumInputBytes { get; set; }
    public int MaximumOutputTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record CoachingRating(string Provider, string? Pool, string? TimeControl, int? Rating, bool PresentationAdjusted);
public sealed record CoachingFact(string Id, string Section, string Text);
public sealed record CoachingLesson(string Id, string Text);
public sealed record CoachingEvidence(string Id, Guid GameId, Guid RunId, int Ply, string Fen, string Side, string PlayedMove,
    string BestMove, string Level, CoachingRating Rating, string[] Line, string[] SanLine, CoachingFact[] Facts, CoachingLesson[] Lessons,
    string AnalysisVersion, string Engine, string SettingsJson, string ClassificationSettingsJson, string PromptVersion);
public sealed record CoachingPlan(string EvidenceId, string Level, string PlayedMove, string[] FactIds, string[] BetterLine, string LessonId);
public sealed record CoachingExplanation(string EvidenceId, Guid GameId, Guid RunId, int Ply, string Fen, string Level, CoachingRating Rating,
    string WhatHappened, string WhyItMattered, string[] BetterLine, string[] SanLine, string Lesson, string Mode, string? FallbackReason,
    string PromptVersion, string? Model, string AnalysisVersion, string Engine, string? ModelProvider = null, string? ModelResponseId = null);
public sealed record CoachingModelRequest(CoachingEvidence Evidence, int MaximumOutputTokens);
public sealed record CoachingModelReply(string Json, string Model, string Provider, string? ResponseId);
public interface ICoachingModel
{
    bool Configured { get; }
    string Name { get; }
    Task<CoachingModelReply?> Explain(CoachingModelRequest request, CancellationToken ct);
}
