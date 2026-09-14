using System.Threading.RateLimiting;
using ChessLens.Api;
using ChessLens.Core.Accounts;
using ChessLens.Core.Persistence;
using ChessLens.Core.Insights;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 12 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.RespectNullableAnnotations = true;
    o.SerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.AddDbContext<LensDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("ConnectionStrings__Database is required.")));
builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 12;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<LensDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "chesslens.session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddDataProtection().SetApplicationName("ChessLens").PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtectionPath"] ?? ".local/keys"));
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.SameSite = SameSiteMode.Strict; });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.Configure<InsightOptions>(builder.Configuration.GetSection("Insights"));
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
if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LensDbContext>().Database.MigrateAsync();
    return;
}
app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Cache-Control"] = "no-store";
    await next();
});
app.UseAuthentication();
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Headers.Origin is { Count: > 0 } origins)
    {
        var allowed = (builder.Configuration["AllowedOrigins"] ?? "http://localhost:8088,http://127.0.0.1:8088,http://127.0.0.1:5173,http://localhost:5173").Split(',');
        if (!allowed.Contains(origins.ToString(), StringComparer.Ordinal)) { ctx.Response.StatusCode = 403; return; }
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
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (LensDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) && !(await db.Database.GetPendingMigrationsAsync(ct)).Any()
        ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapAccounts();
app.MapDemo();
app.MapAnalysis();
app.MapPositionAnalysis();
app.MapInsights();
app.MapPuzzles();
app.MapHub<ProgressHub>("/hubs/progress").RequireAuthorization();
app.MapGames();
app.Run();

public partial class Program;
