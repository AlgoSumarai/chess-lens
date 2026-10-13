using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChessLens.Core.Games;

namespace ChessLens.Core.Imports;

public sealed record ProviderImportRequest(string Provider, string Username, DateTimeOffset From, DateTimeOffset To, int MaxGames = 50)
{
    public ProviderImportRequest Normalized() => this with { Username = Username.Trim().ToLowerInvariant(), From = From.ToUniversalTime(), To = To.ToUniversalTime() };
    public void Validate()
    {
        if (Provider is not ("chesscom" or "lichess") || !Regex.IsMatch(Username, @"\A[a-zA-Z0-9_-]{2,50}\z", RegexOptions.CultureInvariant) ||
            Provider == "lichess" && Username.Length > 30)
            throw new ArgumentException("Choose Chess.com or Lichess and enter a public username (letters, numbers, hyphens or underscores).");
        if (To <= From || To - From > TimeSpan.FromDays(366) || From < new DateTimeOffset(2013, 1, 2, 0, 0, 0, TimeSpan.Zero) ||
            To > DateTimeOffset.UtcNow.AddDays(1) || MaxGames is < 1 or > 200)
            throw new ArgumentException("Choose a date range of up to 366 days from 2013 onward and 1–200 completed games.");
    }
}

public sealed record ProviderRecord(string? Pgn, string? ExternalId, string? Url, DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null, int? WhiteRating = null, int? BlackRating = null, string? RatingPool = null,
    bool? Rated = null, string? TimeControl = null, string? Termination = null, string? Opening = null, string? Eco = null,
    double?[]? Clocks = null, string? White = null, string? Black = null, string? Error = null)
{
    public SourceGame Parse(PgnReader reader, string provider)
    {
        if (Error is not null) throw new ArgumentException(Error);
        if (string.IsNullOrWhiteSpace(Pgn)) throw new ArgumentException("The provider did not include a completed PGN.");
        var game = reader.Parse(Pgn);
        game.Provider = provider;
        game.ExternalId = ExternalId ?? "pgn:" + game.Fingerprint;
        game.Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(provider + ":" + game.ExternalId)));
        game.Url = Url;
        game.StartedAt = StartedAt;
        game.FinishedAt = FinishedAt;
        game.PlayedAt = StartedAt ?? game.PlayedAt ?? FinishedAt;
        game.WhiteRating = WhiteRating ?? game.WhiteRating;
        game.BlackRating = BlackRating ?? game.BlackRating;
        game.White = White ?? game.White;
        game.Black = Black ?? game.Black;
        game.RatingPool = RatingPool;
        game.Rated = Rated;
        game.TimeControl = TimeControl ?? game.TimeControl;
        game.Termination = Termination ?? game.Termination;
        game.Opening = Opening ?? game.Opening;
        game.Eco = Eco ?? game.Eco;
        if (Clocks is not null)
            for (var i = 0; i < Math.Min(Clocks.Length, game.Moves.Count); i++)
                if (game.Moves[i].ClockSeconds is null && Clocks[i] is { } clock && double.IsFinite(clock) && clock >= 0)
                    game.Moves[i].ClockSeconds = clock;
        return game;
    }
}

public sealed record ProviderCheckpoint(string[] Months, int NextMonth, ProviderRecord[] Records, bool Fetched);

public static partial class ProviderContracts
{
    public static Uri Archives(ProviderImportRequest input) => new($"https://api.chess.com/pub/player/{input.Username}/games/archives");
    public static Uri Month(ProviderImportRequest input, string month)
    {
        if (!MonthRegex().IsMatch(month)) throw new ArgumentException("Invalid provider month.");
        return new($"https://api.chess.com/pub/player/{input.Username}/games/{month}");
    }
    public static Uri Lichess(ProviderImportRequest input) => new($"https://lichess.org/api/games/user/{input.Username}?since={input.From.ToUnixTimeMilliseconds()}&until={input.To.ToUnixTimeMilliseconds() - 1}&max={input.MaxGames}&pgnInJson=true&clocks=true&opening=true&evals=false&ongoing=false&finished=true&sort=dateDesc");

    public static void RequireSupportedEndpoint(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !(uri.Host == "api.chess.com" && ChessEndpointRegex().IsMatch(uri.AbsolutePath) && uri.Query.Length == 0 ||
              uri.Host == "lichess.org" && LichessEndpointRegex().IsMatch(uri.AbsolutePath) && LichessQueryRegex().IsMatch(uri.Query)))
            throw new ArgumentException("Only supported completed-game provider endpoints can be fetched.");
    }

    public static string[] ParseArchives(string json, ProviderImportRequest input)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("archives", out var archives) || archives.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Chess.com returned an invalid archive list.");
        var months = new HashSet<string>();
        foreach (var element in archives.EnumerateArray())
        {
            // Returned URLs are untrusted. Extract dates only; construct every request ourselves.
            if (!Uri.TryCreate(Text(element), UriKind.Absolute, out var uri)) continue;
            var prefix = $"/pub/player/{input.Username}/games/";
            if (uri.Scheme != "https" || uri.Host != "api.chess.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                uri.Query.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var month = uri.AbsolutePath[prefix.Length..];
            if (!MonthRegex().IsMatch(month) || !DateTimeOffset.TryParseExact(month, "yyyy/MM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start)) continue;
            if (start < input.To && start.AddMonths(1) > input.From) months.Add(month);
        }
        return months.OrderDescending(StringComparer.Ordinal).Take(13).ToArray();
    }

    public static ProviderRecord[] ParseChessMonth(string json, ProviderImportRequest input, int remaining)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Chess.com returned an invalid game archive.");
        // Archive month is based on completion; date filtering uses completion for consistency.
        return games.EnumerateArray().Where(g => Timestamp(g, "end_time", false) is not { } end || end >= input.From && end < input.To)
            .OrderByDescending(g => Timestamp(g, "end_time", false)).Take(remaining).Select(g => Timestamp(g, "end_time", false) is null
                ? new ProviderRecord(null, null, null, Error: "The provider omitted the completion date; this record could not be matched to your date range.") : ReadChess(g)).ToArray();
    }

    public static ProviderRecord[] ParseLichess(string ndjson, ProviderImportRequest input)
    {
        var records = new List<ProviderRecord>();
        foreach (var line in ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (records.Count >= input.MaxGames) break;
            try
            {
                using var document = JsonDocument.Parse(line);
                var g = document.RootElement;
                if (g.ValueKind != JsonValueKind.Object) { records.Add(new(null, null, null, Error: "A provider record was not a game object.")); continue; }
                var started = Timestamp(g, "createdAt", true);
                if (started is { } date && (date < input.From || date >= input.To)) continue;
                if (String(g, "status") is "created" or "started") { records.Add(new(null, null, null, Error: "An ongoing game was excluded.")); continue; }
                if (String(g, "variant") is { } variant && variant is not ("standard" or "fromPosition"))
                { records.Add(new(null, null, null, Error: "Unsupported variant: " + variant)); continue; }
                var id = String(g, "id");
                if (id is not null && !GameIdRegex().IsMatch(id)) { records.Add(new(null, null, null, Error: "Invalid Lichess source identifier.")); continue; }
                var white = Child(Child(g, "players"), "white"); var black = Child(Child(g, "players"), "black");
                var opening = Child(g, "opening"); var clock = Child(g, "clock");
                var clocks = g.TryGetProperty("clocks", out var values) && values.ValueKind == JsonValueKind.Array
                    ? values.EnumerateArray().Take(PgnReader.MaxPlies).Select(v => v.TryGetDouble(out var n) && n >= 0 && double.IsFinite(n) ? (double?)(n / 100) : null).ToArray() : null;
                var control = Number(clock, "initial") is { } initial && Number(clock, "increment") is { } increment ? $"{initial}+{increment}" : null;
                records.Add(new(String(g, "pgn"), id, id is null ? null : "https://lichess.org/" + id, started, Timestamp(g, "lastMoveAt", true),
                    Rating(white), Rating(black), String(g, "speed"), Boolean(g, "rated"), control, String(g, "status"),
                    String(opening, "name"), String(opening, "eco"), clocks, String(Child(white, "user"), "name"), String(Child(black, "user"), "name")));
            }
            catch (JsonException) { records.Add(new(null, null, null, Error: "A provider record contained malformed JSON.")); }
        }
        return records.ToArray();
    }

    private static ProviderRecord ReadChess(JsonElement g)
    {
        if (String(g, "rules") is { } rules && rules != "chess") return new(null, null, null, Error: "Unsupported variant: " + rules);
        var url = String(g, "url"); string? id = null;
        if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort &&
            uri.UserInfo.Length == 0 && uri.Host is "www.chess.com" or "chess.com" && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
            ChessGameRegex().Match(uri.AbsolutePath) is { Success: true } match)
        { id = match.Groups[1].Value + ":" + match.Groups[2].Value; url = "https://www.chess.com/game/" + match.Groups[1].Value + "/" + match.Groups[2].Value; }
        else url = null;
        var white = Child(g, "white"); var black = Child(g, "black");
        return new(String(g, "pgn"), id, url, Timestamp(g, "start_time", false), Timestamp(g, "end_time", false),
            Rating(white), Rating(black), String(g, "time_class"), Boolean(g, "rated"), String(g, "time_control"),
            String(g, "termination") ?? (String(white, "result") == "win" ? String(black, "result") : String(white, "result")),
            White: String(white, "username"), Black: String(black, "username"));
    }
    private static JsonElement Child(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child) ? child : default;
    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String && value.GetString() is { Length: <= 2048 } text ? text : null;
    // PGNs may be larger than metadata; their parser supplies the definitive bounds.
    private static string? String(JsonElement g, string key) => Child(g, key) is var value && value.ValueKind == JsonValueKind.String
        ? key == "pgn" ? value.GetString() : Text(value) : null;
    private static long? Number(JsonElement g, string key) => Child(g, key) is var value && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) ? n : null;
    private static int? Rating(JsonElement g) => Number(g, "rating") is > 0 and < 5000 and var n ? (int)n : null;
    private static bool? Boolean(JsonElement g, string key) => Child(g, key).ValueKind is JsonValueKind.True ? true : Child(g, key).ValueKind is JsonValueKind.False ? false : null;
    private static DateTimeOffset? Timestamp(JsonElement g, string key, bool milliseconds)
    {
        if (Number(g, key) is not { } n) return null;
        try { return milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(n) : DateTimeOffset.FromUnixTimeSeconds(n); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
    [GeneratedRegex(@"\A\d{4}/(?:0[1-9]|1[0-2])\z")] private static partial Regex MonthRegex();
    [GeneratedRegex(@"\A/pub/player/[a-zA-Z0-9_-]{2,50}/games/(?:archives|\d{4}/(?:0[1-9]|1[0-2]))\z")] private static partial Regex ChessEndpointRegex();
    [GeneratedRegex(@"\A/api/games/user/[a-zA-Z0-9_-]{2,30}\z")] private static partial Regex LichessEndpointRegex();
    [GeneratedRegex(@"\A\?since=\d+&until=\d+&max=\d{1,3}&pgnInJson=true&clocks=true&opening=true&evals=false&ongoing=false&finished=true&sort=dateDesc\z")] private static partial Regex LichessQueryRegex();
    [GeneratedRegex(@"\A/game/(live|daily)/(\d+)\z")] private static partial Regex ChessGameRegex();
    [GeneratedRegex(@"\A[a-zA-Z0-9]{8}\z")] private static partial Regex GameIdRegex();
}
