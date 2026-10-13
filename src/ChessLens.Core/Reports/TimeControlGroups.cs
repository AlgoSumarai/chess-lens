using System.Globalization;
using System.Text.RegularExpressions;

namespace ChessLens.Core.Reports;

public static partial class TimeControlGroups
{
    public static string Group(string? control, string? pool)
    {
        if (pool is "correspondence" or "daily") return "correspondence";
        if (control is null || control.Length > 100) return "unknown";
        if (Correspondence().IsMatch(control) && long.Parse(control[2..], CultureInfo.InvariantCulture) >= 86400) return "correspondence";
        if (!Simple().IsMatch(control)) return "unknown";
        var parts = control.Split('+'); var seconds = long.Parse(parts[0], CultureInfo.InvariantCulture) +
            (parts.Length == 2 ? 40 * long.Parse(parts[1], CultureInfo.InvariantCulture) : 0);
        return seconds <= 0 ? "unknown" : seconds < 180 ? "bullet" : seconds < 600 ? "blitz" : seconds < 1800 ? "rapid" : "classical";
    }
    // Versioned PostgreSQL function has the same contract, and is checked against this implementation.
    public static string SqlGroup(string? control, string? pool) => throw new InvalidOperationException("Use the SQL grouping function inside a database query.");
    [GeneratedRegex(@"^[0-9]{1,8}(\+[0-9]{1,4})?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex Simple();
    [GeneratedRegex(@"^1/[0-9]{5,8}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)] private static partial Regex Correspondence();
}
