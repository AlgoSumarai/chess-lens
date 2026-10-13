namespace ChessLens.Core.Reports;

/// <summary>Keep a recent prefix of each period within one bounded aggregation workload.</summary>
public static class ReportEvidenceBudget
{
    public const int MaximumPlies = 20_000;

    public static (ReportSelection Current, ReportSelection Previous) Apply(ReportSelection current, ReportSelection previous)
    {
        if (current.Games.Sum(g => (long)g.Plies) + previous.Games.Sum(g => (long)g.Plies) <= MaximumPlies) return (current, previous);
        var chosen = new[] { new List<ReportGameReference>(), new List<ReportGameReference>() };
        var candidates = new[] { current.Games, previous.Games };
        var indexes = new[] { 0, 0 }; var stopped = new[] { false, false }; var remaining = MaximumPlies;
        while (!stopped.All(s => s))
            for (var side = 0; side < 2; side++)
            {
                if (stopped[side]) continue;
                if (indexes[side] == candidates[side].Length || candidates[side][indexes[side]].Plies > remaining)
                { stopped[side] = true; continue; }
                var next = candidates[side][indexes[side]++]; chosen[side].Add(next); remaining -= next.Plies;
            }
        if (chosen[0].Count == 0 && current.Games.Length > 0)
            throw new ArgumentException("A source game exceeds the report's 20,000-half-move evidence bound.");
        // Matching totals and training stay intact: consumers disclose that the game evidence was capped.
        return (current with { Games = chosen[0].ToArray() }, previous with { Games = chosen[1].ToArray() });
    }
}
