using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace ChessLens.Api;

public sealed record HostingSecurity(bool RequireHttps, string[] AllowedOrigins, bool HasTrustedProxy)
{
    public static HostingSecurity Configure(WebApplicationBuilder builder)
    {
        var requireHttps = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing");
        var configured = builder.Configuration["AllowedOrigins"];
        if (requireHttps && string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Configure exact HTTPS AllowedOrigins for production.");
        var origins = (configured ?? "http://localhost:8088,http://127.0.0.1:8088,http://127.0.0.1:5173,http://localhost:5173")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value =>
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
                    (requireHttps && uri.Scheme != "https") || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" ||
                    uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.Host.Contains('*'))
                    throw new InvalidOperationException("AllowedOrigins must contain exact HTTP(S) origins; production requires HTTPS.");
                return uri.GetLeftPart(UriPartial.Authority);
            }).Distinct(StringComparer.Ordinal).ToArray();
        if (origins.Length == 0) throw new InvalidOperationException("Configure at least one allowed browser origin.");
        var hosts = builder.Configuration["AllowedHosts"]?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (requireHttps && (hosts.Length == 0 || hosts.Any(h => h.Contains('*'))))
            throw new InvalidOperationException("Configure explicit AllowedHosts for production.");
        var proxies = (builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
            .Select(value => IPAddress.TryParse(value, out var ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any)
                ? ip : throw new InvalidOperationException("KnownProxies must contain explicit proxy IP addresses.")).ToArray();
        if (proxies.Length > 0)
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                options.RequireHeaderSymmetry = true;
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var ip in proxies) options.KnownProxies.Add(ip);
            });
        return new HostingSecurity(requireHttps, origins, proxies.Length > 0);
    }
}
