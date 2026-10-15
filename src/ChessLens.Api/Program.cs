using System.Threading.RateLimiting;
using ChessLens.Api;
using ChessLens.Core.Accounts;
using ChessLens.Core.Persistence;
using ChessLens.Core.Insights;
using ChessLens.Core.Coaching;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

if (args.Contains("--healthcheck"))
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        using var response = await client.GetAsync("http://127.0.0.1:8080/health/ready");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch { Environment.ExitCode = 1; }
    return;
}
var builder = WebApplication.CreateBuilder(args);
var migrate = args.Contains("--migrate");
// The migration command never serves HTTP and needs only database configuration.
var hostingSecurity = migrate ? new HostingSecurity(false, [], false) : HostingSecurity.Configure(builder);
builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.TimestampFormat = "O"; });
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 12 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.RespectNullableAnnotations = true;
    o.SerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.AddDbContext<LensDbContext>(o => o.UseNpgsql(DatabaseConfiguration.ConnectionString(builder.Configuration)));
builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 12;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<LensDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = hostingSecurity.RequireHttps ? "__Host-chesslens.session" : "chesslens.session";
    o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.Path = "/";
    o.Cookie.SecurePolicy = hostingSecurity.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddDataProtection().SetApplicationName("ChessLens").PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtectionPath"] ?? ".local/keys"));
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.HttpOnly = true; o.Cookie.Path = "/";
    if (hostingSecurity.RequireHttps) o.Cookie.Name = "__Host-chesslens.csrf";
    o.Cookie.SecurePolicy = hostingSecurity.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.Configure<InsightOptions>(builder.Configuration.GetSection("Insights"));
builder.Services.Configure<CoachingOptions>(builder.Configuration.GetSection("Coaching"));
builder.Services.AddExceptionHandler<ApiErrors>();
builder.Services.AddSignalR(o => { o.MaximumReceiveMessageSize = 4096; o.EnableDetailedErrors = false; });
builder.Services.AddHostedService<ProgressBroadcaster>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("accounts", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("api", c => RateLimitPartition.GetFixedWindowLimiter(c.User.Identity?.Name ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
if (migrate)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LensDbContext>().Database.MigrateAsync();
    return;
}
app.UseExceptionHandler();
if (hostingSecurity.HasTrustedProxy) app.UseForwardedHeaders();
app.Use(async (ctx, next) =>
{
    using var scope = app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = ctx.TraceIdentifier });
    ctx.Response.Headers["X-Correlation-ID"] = ctx.TraceIdentifier;
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Cache-Control"] = "no-store";
    await next();
});
if (hostingSecurity.RequireHttps)
{
    app.Use(async (ctx, next) =>
    {
        if (!ctx.Request.IsHttps && !ctx.Request.Path.StartsWithSegments("/health"))
        {
            await Results.Problem(statusCode: 400, title: "HTTPS is required", detail: "Open the secure application address.").ExecuteAsync(ctx);
            return;
        }
        await next();
    });
}
app.UseAuthentication();
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Headers.Origin is { Count: > 0 } origins)
    {
        if (!hostingSecurity.AllowedOrigins.Contains(origins.ToString(), StringComparer.Ordinal)) { ctx.Response.StatusCode = 403; return; }
    }
    await next();
});
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
    {
        try { await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx); }
        catch (AntiforgeryValidationException) { await Results.Problem(statusCode: 400, title: "Session check failed", detail: "Refresh the page and try again.").ExecuteAsync(ctx); return; }
    }
    await next();
});
app.MapHealth();
app.MapAccounts();
app.MapDemo();
app.MapAnalysis();
app.MapPositionAnalysis();
app.MapInsights();
app.MapPuzzles();
app.MapHub<ProgressHub>("/hubs/progress").RequireAuthorization();
app.MapGames();
app.MapImportBatches();
app.MapCoaching();
app.MapPractice();
app.MapReports();
app.Run();

public partial class Program;
