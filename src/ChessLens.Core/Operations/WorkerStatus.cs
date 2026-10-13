namespace ChessLens.Core.Operations;

/// <summary>Operational metadata only; contains no owner, game or request data.</summary>
public sealed class WorkerStatus
{
    public Guid Id { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public DateTimeOffset? EngineCheckedAt { get; set; }
    public DateTimeOffset MainLaneAt { get; set; }
    public DateTimeOffset InteractiveLaneAt { get; set; }
    public bool Ready { get; set; }

    public bool HasRecentProgress(DateTimeOffset now) =>
        EngineCheckedAt >= now.AddMinutes(-3) && MainLaneAt >= now.AddMinutes(-16) && InteractiveLaneAt >= now.AddMinutes(-4);
}
