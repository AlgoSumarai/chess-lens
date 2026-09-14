using Microsoft.AspNetCore.Identity;

namespace ChessLens.Core.Accounts;

public sealed class AppUser : IdentityUser
{
    public string Timezone { get; set; } = "UTC";
    public string CoachingLevel { get; set; } = "beginner";
    public string PreferredTimeControls { get; set; } = "rapid";
    public bool IsDemo { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ImportedProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerId { get; set; }
    public required string Provider { get; set; }
    public required string Username { get; set; }
    public bool OwnershipVerified { get; set; } // Public imports never set this to true.
}
