using ChessLens.Core.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Microsoft.AspNetCore.Builder;
using System.Net;

namespace ChessLens.Tests;

public sealed class DatabaseFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string databaseName = "chesslens_test_" + Guid.NewGuid().ToString("N");
    private string adminConnection = "";
    public string Connection { get; private set; } = "";
    private int clientNumber;

    public new HttpClient CreateClient()
    {
        var client = base.CreateClient();
        // TestServer has no physical remote address. Model independent clients
        // so account-rate limits do not accumulate across unrelated test cases.
        client.DefaultRequestHeaders.Add("X-Fixture-Address", $"127.0.1.{Interlocked.Increment(ref clientNumber)}");
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", Connection);
        builder.UseSetting("DataProtectionPath", Path.Combine(Path.GetTempPath(), databaseName));
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, FixtureAddressFilter>());
    }

    public async Task InitializeAsync()
    {
        var supplied = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? throw new InvalidOperationException("Integration tests require a PostgreSQL connection. Run scripts/load-env.ps1, or filter to PgnTests for the fast rules suite.");
        var options = new NpgsqlConnectionStringBuilder(supplied) { Database = "postgres" };
        adminConnection = options.ConnectionString;
        await using (var connection = new NpgsqlConnection(adminConnection))
        {
            await connection.OpenAsync();
            // Name is generated locally from a GUID and contains only [a-z0-9_].
            await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
            await command.ExecuteNonQueryAsync();
        }
        options.Database = databaseName;
        Connection = options.ConnectionString;
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LensDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        if (string.IsNullOrEmpty(adminConnection)) return;
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

internal sealed class FixtureAddressFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, continuation) =>
        {
            if (IPAddress.TryParse(context.Request.Headers["X-Fixture-Address"].ToString(), out var address)) context.Connection.RemoteIpAddress = address;
            return continuation();
        });
        next(app);
    };
}

[CollectionDefinition("database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
