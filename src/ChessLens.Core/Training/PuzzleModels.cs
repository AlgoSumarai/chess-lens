namespace ChessLens.Core.Training;

public sealed class Puzzle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public Guid GameId { get; set; }
    public Guid RunId { get; set; }
    public int Ply { get; set; }
    public string ValidationVersion { get; set; } = PuzzleValidator.Version;
    public string State { get; set; } = "queued";
    public string? SolutionJson { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public long Version { get; set; }
    public int ReviewStreak { get; set; }
    public DateTimeOffset? ReviewDueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PuzzleAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public Guid PuzzleId { get; set; }
    public string State { get; set; } = "active";
    public string HistoryJson { get; set; } = "[]";
    public string JournalJson { get; set; } = "[]";
    public int HintsUsed { get; set; }
    public int NodeHintsUsed { get; set; }
    public long Version { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public int? SolveSeconds { get; set; }
    public DateTimeOffset? ReviewDueAt { get; set; }
}
public sealed record PuzzleMoveEntry(string Move, bool Accepted, int HintsUsed, DateTimeOffset At);

public static class PuzzleReview
{
    public static (int Streak, DateTimeOffset Due) Schedule(string outcome, int hints, int priorStreak, DateTimeOffset now)
    {
        if (outcome is "failed" or "revealed" or "expired") return (0, now.AddHours(4));
        if (outcome != "solved") throw new ArgumentException("Only finished attempts can change review scheduling.");
        if (hints > 0) return (0, now.AddDays(1));
        var days = new[] { 1, 3, 7, 14, 30 };
        return (Math.Min(priorStreak + 1, days.Length), now.AddDays(days[Math.Clamp(priorStreak, 0, days.Length - 1)]));
    }
    public static PuzzleNode? Node(PuzzleSolution solution, string[] history)
    {
        PuzzleNode? node = solution.Root;
        for (var index = 0; index < history.Length;)
        {
            var played = history[index++];
            var choice = node?.Choices.SingleOrDefault(c => c.Move == played) ?? throw new InvalidOperationException("Saved puzzle history is inconsistent.");
            if (choice.Reply is not null && (index >= history.Length || history[index++] != choice.Reply))
                throw new InvalidOperationException("Saved defensive reply is inconsistent.");
            node = choice.Next;
        }
        return node;
    }
}
