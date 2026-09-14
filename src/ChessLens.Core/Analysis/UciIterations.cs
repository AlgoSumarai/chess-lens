namespace ChessLens.Core.Analysis;

/// <summary>Collects complete, ordered UCI MultiPV batches without mixing later partial updates.</summary>
public sealed class UciIterations(int expectedLines)
{
    private readonly Dictionary<int, SearchLine> latest = [];
    private readonly List<SearchLine> pending = [];
    private readonly List<SearchLine[]> completed = [];
    public IReadOnlyList<SearchLine> Latest => latest.Values.OrderBy(l => l.Rank).ToArray();

    public void Add(SearchLine line)
    {
        if (line.Rank < 1 || line.Rank > Math.Max(1, expectedLines)) throw new InvalidOperationException("Engine returned an unexpected MultiPV rank.");
        latest[line.Rank] = line;
        if (line.Rank == 1) pending.Clear();
        if (line.Rank != pending.Count + 1 || pending.Count > 0 && pending[0].Depth != line.Depth)
        { pending.Clear(); return; }
        pending.Add(line);
        if (pending.Count != expectedLines) return;
        if (pending.All(l => !l.Bound && l.Depth > 0 && l.Pv.Length > 0) && pending.Select(l => l.Pv[0]).Distinct().Count() == expectedLines)
        {
            completed.Add(pending.ToArray());
            if (completed.Count > 128) completed.RemoveAt(0);
        }
        pending.Clear();
    }
    public IReadOnlyList<SearchLine>? CompleteFor(string bestMove) => completed
        .Where(lines => lines[0].Pv[0] == bestMove).OrderByDescending(lines => lines[0].Depth).ThenByDescending(lines => lines[0].Nodes).FirstOrDefault();
}
