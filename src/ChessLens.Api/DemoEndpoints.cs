using System.Reflection;
using System.Text.Json;
using ChessLens.Core.Accounts;
using ChessLens.Core.Imports;
using ChessLens.Core.Persistence;
using Microsoft.AspNetCore.Identity;

namespace ChessLens.Api;

public static class DemoEndpoints
{
    public static void MapDemo(this WebApplication app) => app.MapPost("/api/account/demo", Create).RequireRateLimiting("accounts");

    private static async Task<IResult> Create(UserManager<AppUser> users, SignInManager<AppUser> signIn, LensDbContext db, CancellationToken ct)
    {
        var user = new AppUser { UserName = $"demo-{Guid.NewGuid():N}@demo.invalid", IsDemo = true };
        user.Email = user.UserName;
        var created = await users.CreateAsync(user);
        if (!created.Succeeded) return Results.Problem(statusCode: 500, title: "Unable to open demo workspace");
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ChessLens.Demo.Pgn")!;
        using var reader = new StreamReader(stream);
        var pgn = await reader.ReadToEndAsync(ct);
        db.ImportJobs.Add(new ImportJob { OwnerId = user.Id, PayloadJson = JsonSerializer.Serialize(new PgnImportRequest(pgn, "Learner")) });
        await db.SaveChangesAsync(ct);
        await signIn.SignInAsync(user, false);
        return Results.Ok(new { user.IsDemo });
    }
}
