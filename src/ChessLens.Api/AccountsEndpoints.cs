using System.Security.Claims;
using ChessLens.Core.Accounts;
using ChessLens.Core.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Api;

public static class AccountsEndpoints
{
    public static string Owner(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    public static void MapAccounts(this WebApplication app)
    {
        var group = app.MapGroup("/api/account");
        group.MapGet("/csrf", (HttpContext ctx, IAntiforgery csrf) => Results.Ok(new { token = csrf.GetAndStoreTokens(ctx).RequestToken }));
        group.MapPost("/register", Register).RequireRateLimiting("accounts");
        group.MapPost("/login", Login).RequireRateLimiting("accounts");
        group.MapPost("/logout", async (SignInManager<AppUser> manager) => { await manager.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
        group.MapGet("/me", async (ClaimsPrincipal user, UserManager<AppUser> users) =>
        {
            var account = await users.FindByIdAsync(user.Owner());
            return account is null ? Results.Unauthorized() : Results.Ok(View(account));
        }).RequireAuthorization();
        group.MapPut("/preferences", Preferences).RequireAuthorization();
        group.MapDelete("/data", DeleteData).RequireAuthorization();
    }

    private static async Task<IResult> Register(Credentials input, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        if (input.Email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(input.Email, out var address) || address.Address != input.Email)
            throw new ArgumentException("Enter a valid email address.");
        if (input.Password.Length > 128) throw new ArgumentException("Passwords may have at most 128 characters.");
        var account = new AppUser { UserName = input.Email, Email = input.Email };
        var result = await users.CreateAsync(account, input.Password);
        if (!result.Succeeded) return Results.ValidationProblem(new Dictionary<string, string[]> { ["account"] = result.Errors.Select(e => e.Code.Contains("Duplicate") ? "Unable to register this account. Try signing in." : e.Description).ToArray() });
        await signIn.SignInAsync(account, false);
        return Results.Ok(View(account));
    }

    private static async Task<IResult> Login(Credentials input, SignInManager<AppUser> signIn)
    {
        if (input.Email.Length > 254 || input.Password.Length > 128) return Results.Unauthorized();
        var result = await signIn.PasswordSignInAsync(input.Email, input.Password, false, true);
        return result.Succeeded ? Results.Ok(new { signedIn = true }) : Results.Problem(statusCode: 401, title: "Sign-in failed", detail: "Check your email and password, or try again later.");
    }

    private static async Task<IResult> Preferences(PreferencesRequest input, ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        if (!new[] { "beginner", "intermediate", "advanced" }.Contains(input.CoachingLevel)) throw new ArgumentException("Choose a coaching level.");
        try { TimeZoneInfo.FindSystemTimeZoneById(input.Timezone); }
        catch (TimeZoneNotFoundException) { throw new ArgumentException("Choose a recognised timezone."); }
        if (!new[] { "bullet", "blitz", "rapid", "classical", "correspondence" }.Contains(input.PreferredTimeControls)) throw new ArgumentException("Choose a time control.");
        var user = (await users.FindByIdAsync(principal.Owner()))!;
        user.Timezone = input.Timezone; user.CoachingLevel = input.CoachingLevel; user.PreferredTimeControls = input.PreferredTimeControls;
        var result = await users.UpdateAsync(user);
        return result.Succeeded ? Results.Ok(View(user)) : Results.Conflict();
    }

    private static async Task<IResult> DeleteData(ClaimsPrincipal user, LensDbContext db, CancellationToken ct)
    {
        var owner = user.Owner();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner}, 0))", ct);
        // Lock jobs before associations so in-flight imports cannot resurrect deleted data.
        await db.ImportJobs.Where(j => j.OwnerId == owner).ExecuteDeleteAsync(ct);
        await db.InsightSnapshots.Where(j => j.OwnerId == owner).ExecuteDeleteAsync(ct);
        await db.UserGames.Where(g => g.OwnerId == owner).ExecuteDeleteAsync(ct);
        await db.ImportedProfiles.Where(p => p.OwnerId == owner).ExecuteDeleteAsync(ct);
        await db.Games.Where(g => !db.UserGames.Any(ug => ug.GameId == g.Id)).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static object View(AppUser u) => new { u.Id, u.Email, u.Timezone, u.CoachingLevel, u.PreferredTimeControls, u.IsDemo };
}

public sealed record Credentials(string Email, string Password);
public sealed record PreferencesRequest(string Timezone, string CoachingLevel, string PreferredTimeControls);
