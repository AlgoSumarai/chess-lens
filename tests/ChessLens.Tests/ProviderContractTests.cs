using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ChessLens.Core.Imports;

namespace ChessLens.Tests;

/// <summary>Labelled provider contract fixtures. These tests make no external network requests.</summary>
public sealed class ProviderContractTests
{
    internal static readonly ProviderImportRequest Request = new("chesscom", "learner", new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), 5);
    internal static string ChessGame(string id = "12345", string pgn = PgnTests.Game, string rules = "chess") => JsonSerializer.Serialize(new
    {
        url = "https://www.chess.com/game/live/" + id, pgn, rules, end_time = new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
        time_control = "300+2", time_class = "blitz", rated = true,
        white = new { username = "Learner", rating = 1234, result = "checkmated" }, black = new { username = "Opponent", rating = 1300, result = "win" }
    });
    internal static string ChessMonth(params string[] games) => "{\"games\":[" + string.Join(',', games) + "]}";
    internal static string LichessGame(string id = "abcd1234", string pgn = PgnTests.Game) => JsonSerializer.Serialize(new
    {
        id, variant = "standard", status = "mate", pgn, createdAt = Request.From.AddDays(10).ToUnixTimeMilliseconds(),
        lastMoveAt = Request.From.AddDays(10).AddMinutes(5).ToUnixTimeMilliseconds(), speed = "blitz", rated = true,
        players = new { white = new { user = new { name = "Learner" }, rating = 1200 }, black = new { user = new { name = "Opponent" }, rating = 1250 } },
        clock = new { initial = 300, increment = 2 }, clocks = new[] { 30000, 30100, 29000, 29800 },
        opening = new { name = "Fixture opening", eco = "A00" }
    });

    [Theory]
    [InlineData("https://127.0.0.1/games")]
    [InlineData("https://api.chess.com@127.0.0.1/pub/player/learner/games/archives")]
    [InlineData("https://api.chess.com/pub/player/learner/games")]
    [InlineData("https://api.chess.com/pub/player/learner/games/2026/09?url=http://localhost")]
    [InlineData("https://lichess.org/api/user/learner/current-game")]
    [InlineData("https://lichess.org/api/games/user/learner?players=https://localhost")]
    public void Unsupported_or_live_endpoints_are_rejected(string url) => Assert.Throws<ArgumentException>(() => ProviderContracts.RequireSupportedEndpoint(new Uri(url)));

    [Theory]
    [InlineData("../admin")]
    [InlineData("learner?max=10000")]
    [InlineData("https://localhost")]
    [InlineData("learner%2farchives")]
    public void Usernames_cannot_control_network_destinations(string username) => Assert.Throws<ArgumentException>(() => (Request with { Username = username }).Validate());

    [Fact]
    public void Archive_urls_are_dates_only_and_never_fetched_as_supplied()
    {
        var json = JsonSerializer.Serialize(new { archives = new[] {
            "https://api.chess.com/pub/player/learner/games/2026/09", "https://api.chess.com/pub/player/learner/games/2026/08",
            "http://api.chess.com/pub/player/learner/games/2026/09", "https://localhost/pub/player/learner/games/2026/09",
            "https://api.chess.com/pub/player/other/games/2026/09", "https://api.chess.com/pub/player/learner/games/2026/09?redirect=http://localhost" } });
        Assert.Equal(["2026/09"], ProviderContracts.ParseArchives(json, Request));
        ProviderContracts.RequireSupportedEndpoint(ProviderContracts.Month(Request, "2026/09"));
        ProviderContracts.RequireSupportedEndpoint(ProviderContracts.Lichess(Request with { Provider = "lichess" }));
    }

    [Fact]
    public void Completed_chess_archive_preserves_source_identity_metadata_and_mixed_failures()
    {
        var games = ProviderContracts.ParseChessMonth(ChessMonth(ChessGame(), ChessGame("22", rules: "chess960"), "{\"pgn\":\"missing date\"}"), Request, 3);
        Assert.Equal(3, games.Length);
        var game = games[0].Parse(new PgnReader(), "chesscom");
        Assert.Equal("live:12345", game.ExternalId); Assert.Equal(1234, game.WhiteRating); Assert.Equal("blitz", game.RatingPool);
        Assert.Equal("white", PgnReader.Identify(game, "learner")); Assert.Equal("checkmated", game.Termination);
        Assert.Null(game.Opening); Assert.Null(game.Moves[0].ClockSeconds); Assert.Null(game.StartedAt);
        Assert.NotNull(game.FinishedAt); Assert.True(game.Rated);
        Assert.Contains(games, g => g.Error?.Contains("Unsupported variant") == true);
        Assert.Contains(games, g => g.Error?.Contains("completion date") == true);
    }

    [Fact]
    public void Ndjson_handles_missing_fields_clocks_variants_and_malformed_lines_independently()
    {
        var games = ProviderContracts.ParseLichess(LichessGame() + "\nnot-json\n[]\n{\"variant\":\"antichess\"}\n" + JsonSerializer.Serialize(new { pgn = PgnTests.Game }), Request with { Provider = "lichess" });
        Assert.Equal(5, games.Length);
        var game = games[0].Parse(new PgnReader(), "lichess");
        Assert.Equal("abcd1234", game.ExternalId); Assert.Equal("https://lichess.org/abcd1234", game.Url);
        Assert.Equal(300, game.Moves[0].ClockSeconds); Assert.Equal("300+2", game.TimeControl); Assert.Equal("Fixture opening", game.Opening);
        Assert.NotNull(game.StartedAt); Assert.NotNull(game.FinishedAt);
        Assert.All(games.Skip(1).Take(3), record => Assert.NotNull(record.Error));
        var missing = games[4].Parse(new PgnReader(), "lichess");
        Assert.Null(missing.WhiteRating); Assert.Null(missing.PlayedAt); Assert.Null(missing.RatingPool); Assert.Null(missing.Moves[0].ClockSeconds);
    }

    [Fact]
    public async Task Http_cache_revalidates_etag_and_last_modified_then_respects_max_age()
    {
        var calls = 0;
        using var transport = new ProviderHttpClient(new Handler(request =>
        {
            calls++;
            if (calls == 1)
            {
                var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("cached fixture") };
                result.Headers.ETag = new EntityTagHeaderValue("\"fixture-v1\""); result.Content.Headers.LastModified = Request.From;
                result.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.Zero }; return result;
            }
            Assert.Equal("\"fixture-v1\"", request.Headers.IfNoneMatch.Single().Tag); Assert.Equal(Request.From, request.Headers.IfModifiedSince);
            var unchanged = new HttpResponseMessage(HttpStatusCode.NotModified);
            unchanged.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromHours(1) }; return unchanged;
        }));
        Assert.Equal("cached fixture", await transport.Get(ProviderContracts.Archives(Request), CancellationToken.None));
        Assert.Equal("cached fixture", await transport.Get(ProviderContracts.Archives(Request), CancellationToken.None));
        Assert.Equal("cached fixture", await transport.Get(ProviderContracts.Archives(Request), CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Provider_rate_limits_and_outages_are_explicit_bounded_retries(int status)
    {
        using var transport = new ProviderHttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status); response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2)); return response;
        }));
        var retry = await Assert.ThrowsAsync<ProviderRetryException>(() => transport.Get(ProviderContracts.Lichess(Request with { Provider = "lichess" }), CancellationToken.None));
        Assert.True(retry.Delay >= TimeSpan.FromSeconds(status == 429 ? 60 : 5));
    }

    [Fact]
    public async Task Redirects_oversize_responses_and_cancellation_never_trigger_unbounded_work()
    {
        using var redirect = new ProviderHttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.MovedPermanently) { Headers = { Location = new Uri("https://localhost/") } }));
        await Assert.ThrowsAsync<ArgumentException>(() => redirect.Get(ProviderContracts.Archives(Request), CancellationToken.None));
        using var oversized = new ProviderHttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fixture") };
            response.Content.Headers.ContentLength = ProviderHttpClient.MaximumResponseBytes + 1; return response;
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => oversized.Get(ProviderContracts.Archives(Request), CancellationToken.None));
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => redirect.Get(ProviderContracts.Archives(Request), stopped.Token));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
