using ChessLens.Core.Insights;

namespace ChessLens.Core.Reports;

public sealed record ProgressFilter(DateOnly FromDate, DateOnly ThroughDate, string? Source = null, string? Colour = null,
    string? Opening = null, bool UnknownOpening = false, string? TimeControl = null, string? TimeControlGroup = null, int MaximumGames = 50)
{
    public void Validate()
    {
        ReportCalendar.Progress(FromDate, ThroughDate);
        if (Source is not (null or "pgn" or "chesscom" or "lichess") || Colour is not (null or "white" or "black") ||
            Opening?.Length > 2048 || UnknownOpening && Opening is not null || TimeControl?.Length > 100 ||
            TimeControlGroup is not (null or "bullet" or "blitz" or "rapid" or "classical" or "correspondence" or "unknown") || MaximumGames is < 1 or > 200)
            throw new ArgumentException("Choose valid progress filters and 1–200 games per period.");
    }
}

public sealed class ReportSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public required string Kind { get; set; }
    public required string ScopeIdentity { get; set; }
    public required string CacheIdentity { get; set; }
    public required string InputsJson { get; set; }
    public string RulesVersion { get; set; } = ReportRules.Version;
    public int Revision { get; set; }
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

public sealed class ReportSchedule
{
    public required string OwnerId { get; set; }
    public required string Timezone { get; set; }
    public DateTimeOffset? ScanBefore { get; set; }
    public DateTimeOffset? CycleWeekFrom { get; set; }
    public DateTimeOffset NextCheckAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
}

public sealed record ReportAnalysisReference(Guid RunId, string Engine, string Profile, string AnalysisVersion, string SettingsJson,
    string ClassificationSettingsJson, DateTimeOffset CompletedAt, bool SettingsKnown);
public sealed record ReportGameReference(Guid GameId, string? Colour, string Source, string? RatingPool, bool? Rated, string? Opening,
    string? TimeControl, string TimeControlGroup, string Result, DateTimeOffset? PlayedAt, int? Rating, int Plies, ReportAnalysisReference? Analysis);
public sealed record TrainingObservation(string Kind, Guid Id, Guid GameId, string Outcome, bool Assisted, string Version, string Objective, DateTimeOffset FinishedAt);
public sealed record ReportSelection(int MatchingGames, int UndatedGamesExcluded, ReportGameReference[] Games, int TrainingCount,
    bool TrainingCapped, TrainingObservation[] Training, Guid[] DuePuzzles);
public sealed record ReportInputs(ReportPeriod Period, ReportPeriod PreviousPeriod, ProgressFilter? Filter, DateTimeOffset CapturedAt,
    long DataVersion, string DetectorVersion, InsightOptions DetectorOptions, ReportSelection Current, ReportSelection Previous, int MaximumGames);
public sealed record ResultCounts(int Wins, int Draws, int Losses, int Unknown);
public sealed record ReportCoverage(int SelectedGames, int ReviewedGames, int MissingIdentityGames, int UnreviewedGames, int UndatedGames,
    int PlayerDecisions, int CompleteDecisions, int StableDecisions, int ProvisionalDecisions, int RejectedDecisions, bool SelectionCapped,
    int ContextGames, int ContextReviewedGames);
public sealed record PeriodComparison(string Status, string Explanation, int? CurrentErrors = null, int? CurrentEligible = null,
    int? PreviousErrors = null, int? PreviousEligible = null, double? RateDifference = null);
public sealed record RatingObservation(Guid GameId, DateTimeOffset PlayedAt, int Rating, string? TimeControl, bool? Rated);
public sealed record WeaknessRate(string Category, int Occurrences, int EligibleDecisions, int EligibleGames, PeriodComparison Comparison);
public sealed record ReportGroup(string Identity, string Source, string RatingPool, string Colour, string Opening, string TimeControlGroup,
    bool? Rated, string[] ExactTimeControls, ReportAnalysisReference Analysis, Guid[] GameIds, ResultCounts Results, ReportCoverage Coverage,
    InsightSummary Summary, PeriodComparison Comparison, WeaknessRate[] WeaknessRates, RatingObservation[] Ratings);
public sealed record TrainingSummary(string Kind, string Version, string Objective, int Attempts, int SuccessfulUnassisted,
    int SuccessfulAssisted, int NeedsPractice, int CancelledOrRestarted, int Ungraded);
public sealed record ReportResult(string RulesVersion, ReportPeriod Period, ReportPeriod PreviousPeriod, DateTimeOffset CapturedAt,
    int MatchingGames, ReportCoverage Coverage, ResultCounts Results, ReportGroup[] Groups, TrainingSummary[] Training,
    int TrainingCount, bool TrainingCapped, string[] PracticePlan, Guid[] DuePuzzles, int UndatedGamesExcluded);
