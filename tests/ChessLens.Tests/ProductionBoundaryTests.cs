using System.Net;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using ChessLens.Core.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ChessLens.Tests;

[Collection("database")]
public sealed class ProductionBoundaryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Production_migration_command_needs_no_web_host_or_origin_configuration()
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--migrate");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ConnectionStrings__Database"] = fixture.Connection;
        start.Environment.Remove("Database__PasswordFile");
        start.Environment.Remove("AllowedOrigins");
        start.Environment.Remove("AllowedHosts");
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            Assert.True(process.ExitCode == 0, await stderr);
            Assert.DoesNotContain("Now listening", await stdout);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private WebApplicationFactory<Program> Production() => fixture.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("AllowedOrigins", "https://localhost:9443");
        builder.UseSetting("AllowedHosts", "localhost");
        builder.UseSetting("ReverseProxy:KnownProxies:0", "198.51.100.10");
    });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string remote = "198.51.100.10", bool https = false)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(https ? "https://localhost:9443" : "http://localhost"), AllowAutoRedirect = false });
        // This startup filter exists only in TestServer, never in the application.
        client.DefaultRequestHeaders.Add("X-Fixture-Address", remote);
        return client;
    }

    private static void Forward(HttpClient client, string proto = "https", string address = "203.0.113.20")
    {
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", proto);
    }

    [Fact]
    public async Task Only_the_explicit_proxy_can_establish_https_and_secure_csrf_cookies()
    {
        await using var server = Production();
        using var trusted = Client(server); Forward(trusted);
        var response = await trusted.GetAsync("/api/account/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-chesslens.csrf=", cookie);
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; path=/", cookie, StringComparison.OrdinalIgnoreCase);
        using var foreign = Client(server, "198.51.100.11"); Forward(foreign);
        var rejected = await foreign.GetAsync("/api/account/csrf");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.False(rejected.Headers.Contains("Set-Cookie"));
        Assert.Contains("HTTPS is required", await rejected.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await foreign.GetAsync("/health/live")).StatusCode);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("http", "203.0.113.20")]
    [InlineData("https,http", "203.0.113.20")]
    public async Task Missing_plain_or_mismatched_proxy_headers_cannot_create_a_secure_session(string? proto, string? address)
    {
        await using var server = Production(); using var client = Client(server);
        if (proto is not null) Forward(client, proto, address!);
        var response = await client.GetAsync("/api/account/csrf");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Production_sessions_keep_secure_host_prefix_and_csrf_origin_boundaries()
    {
        await using var server = Production(); using var client = Client(server, https: true); Forward(client);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var registration = await client.PostAsJsonAsync("/api/account/register", new { email = $"production-{Guid.NewGuid():N}@test.example", password = "ChessLens.Test2026!" });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var session = Assert.Single(registration.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-chesslens.session="));
        Assert.Contains("; secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=strict", session, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", session, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/account/me")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account/logout", new { })).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", "https://unrelated.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/hubs/progress/negotiate?negotiateVersion=1", null)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://localhost:9443");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/hubs/progress/negotiate?negotiateVersion=1", null)).StatusCode);
        client.DefaultRequestHeaders.Host = "unrelated.test";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/account/me")).StatusCode);
    }

    [Fact]
    public void Mounted_database_password_preserves_special_characters_without_becoming_connection_options()
    {
        var file = Path.GetTempFileName();
        try
        {
            const string password = "fixture;Password=\"other\"=literal";
            File.WriteAllText(file, password + "\n");
            var values = new Dictionary<string, string?> { ["ConnectionStrings:Database"] = "Host=localhost;Database=fixture;Username=fixture", ["Database:PasswordFile"] = file };
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var connection = new NpgsqlConnectionStringBuilder(DatabaseConfiguration.ConnectionString(config));
            Assert.Equal(password, connection.Password); Assert.Equal("fixture", connection.Database);
            config["ConnectionStrings:Database"] += ";Password=inline";
            Assert.Throws<InvalidOperationException>(() => DatabaseConfiguration.ConnectionString(config));
            config["ConnectionStrings:Database"] = values["ConnectionStrings:Database"];
            File.WriteAllText(file, "one\ntwo");
            Assert.Throws<InvalidOperationException>(() => DatabaseConfiguration.ConnectionString(config));
            File.WriteAllText(file, new string('x', 4097));
            Assert.Throws<InvalidOperationException>(() => DatabaseConfiguration.ConnectionString(config));
        }
        finally { File.Delete(file); }
    }
}
