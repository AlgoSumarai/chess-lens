using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ChessLens.Core.Imports;

public interface IProviderHttpClient
{
    Task<string> Get(Uri endpoint, CancellationToken ct);
}

public sealed class ProviderRetryException(string message, TimeSpan delay) : Exception(message)
{
    public TimeSpan Delay { get; } = delay;
}

/// <summary>Fixed public endpoints only. The processor also serializes requests across worker processes.</summary>
public sealed class ProviderHttpClient : IProviderHttpClient, IDisposable
{
    public const int MaximumResponseBytes = 20 * 1024 * 1024;
    private readonly HttpClient client;
    private readonly SemaphoreSlim serial = new(1);
    private readonly Dictionary<string, CachedResponse> cache = new();
    private const int MaximumCacheEntries = 8;
    private const long MaximumCacheBytes = 64L * 1024 * 1024;
    private sealed record CachedResponse(string Body, string? Etag, DateTimeOffset? Modified, DateTimeOffset Expires, int Bytes);

    public ProviderHttpClient() : this(new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        MaxConnectionsPerServer = 1, PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { }
    public ProviderHttpClient(HttpMessageHandler handler) => client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<string> Get(Uri endpoint, CancellationToken ct)
    {
        ProviderContracts.RequireSupportedEndpoint(endpoint);
        await serial.WaitAsync(ct);
        try
        {
            cache.TryGetValue(endpoint.AbsoluteUri, out var saved);
            if (saved is not null && saved.Expires > DateTimeOffset.UtcNow) return saved.Body;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.UserAgent.ParseAdd("ChessLens/0.1 (completed-game personal analysis)");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(endpoint.Host == "lichess.org" ? "application/x-ndjson" : "application/json"));
            if (saved?.Etag is { } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            if (saved?.Modified is { } modified) request.Headers.IfModifiedSince = modified;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                throw new ArgumentException("That public username or archive is unavailable. Check the username and dates.");
            if ((int)response.StatusCode == 429)
            {
                var after = response.Headers.RetryAfter;
                var delay = after?.Delta ?? (after?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromMinutes(1);
                // Lichess explicitly requires a full minute, even when a shorter Retry-After is supplied.
                throw new ProviderRetryException("The provider asked us to pause. This import will resume automatically.", delay < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay);
            }
            if ((int)response.StatusCode >= 500) throw new ProviderRetryException("The provider is temporarily unavailable. This import will retry automatically.", TimeSpan.FromSeconds(5));
            if (response.StatusCode == HttpStatusCode.NotModified && saved is not null)
            {
                cache[endpoint.AbsoluteUri] = saved with { Expires = Expiry(response.Headers, saved.Expires) };
                return saved.Body;
            }
            if (!response.IsSuccessStatusCode || (int)response.StatusCode is >= 300 and < 400)
                throw new ArgumentException("The provider rejected the completed-game request. No redirected URL was fetched.");
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new ArgumentException("The provider archive exceeds the 20 MB response limit. Choose a smaller date range.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, deadline.Token);
                if (read == 0) break;
                if (bytes.Length + read > MaximumResponseBytes) throw new ArgumentException("The provider archive exceeds the 20 MB response limit. Choose a smaller date range.");
                await bytes.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
            }
            var body = Encoding.UTF8.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
            if (response.Headers.CacheControl?.NoStore != true)
            {
                while (cache.Count >= MaximumCacheEntries || cache.Values.Sum(c => (long)c.Bytes) + bytes.Length > MaximumCacheBytes)
                    cache.Remove(cache.OrderBy(c => c.Value.Expires).First().Key);
                cache[endpoint.AbsoluteUri] = new(body, response.Headers.ETag?.ToString(), response.Content.Headers.LastModified,
                    Expiry(response.Headers, DateTimeOffset.UtcNow), checked((int)bytes.Length));
            }
            else cache.Remove(endpoint.AbsoluteUri);
            return body;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ProviderRetryException("The provider request timed out. This import will retry automatically.", TimeSpan.FromSeconds(5)); }
        catch (HttpRequestException)
        { throw new ProviderRetryException("The provider could not be reached. This import will retry automatically.", TimeSpan.FromSeconds(5)); }
        finally { serial.Release(); }
    }

    private static DateTimeOffset Expiry(HttpResponseHeaders headers, DateTimeOffset fallback)
    {
        if (headers.CacheControl is { NoCache: true }) return DateTimeOffset.UtcNow;
        var remaining = headers.CacheControl?.MaxAge - (headers.Age ?? TimeSpan.Zero);
        return remaining is { } ttl ? DateTimeOffset.UtcNow.Add(ttl < TimeSpan.Zero ? TimeSpan.Zero : ttl) : fallback;
    }
    public void Dispose() { client.Dispose(); serial.Dispose(); }
}
