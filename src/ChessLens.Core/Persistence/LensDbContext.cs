using ChessLens.Core.Accounts;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Imports;
using ChessLens.Core.Insights;
using ChessLens.Core.Training;
using ChessLens.Core.Coaching;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Core.Persistence;

public sealed class LensDbContext(DbContextOptions<LensDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<SourceGame> Games => Set<SourceGame>();
    public DbSet<GameMove> Moves => Set<GameMove>();
    public DbSet<UserGame> UserGames => Set<UserGame>();
    public DbSet<ImportedProfile> ImportedProfiles => Set<ImportedProfile>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportJobGame> ImportJobGames => Set<ImportJobGame>();
    public DbSet<ProviderThrottle> ProviderThrottles => Set<ProviderThrottle>();
    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();
    public DbSet<ImportAnalysisBatch> ImportAnalysisBatches => Set<ImportAnalysisBatch>();
    public DbSet<MoveAnalysis> MoveAnalyses => Set<MoveAnalysis>();
    public DbSet<PositionReviewChannel> PositionReviewChannels => Set<PositionReviewChannel>();
    public DbSet<PositionAnalysisJob> PositionAnalysisJobs => Set<PositionAnalysisJob>();
    public DbSet<InsightSnapshot> InsightSnapshots => Set<InsightSnapshot>();
    public DbSet<Puzzle> Puzzles => Set<Puzzle>();
    public DbSet<PuzzleAttempt> PuzzleAttempts => Set<PuzzleAttempt>();
    public DbSet<CoachingJob> CoachingJobs => Set<CoachingJob>();
    public DbSet<CoachingUsage> CoachingUsages => Set<CoachingUsage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<SourceGame>(e =>
        {
            e.HasIndex(g => g.Fingerprint).IsUnique();
            e.Property(g => g.Fingerprint).HasMaxLength(64);
            e.HasIndex(g => new { g.Provider, g.ExternalId }).IsUnique();
            e.HasIndex(g => g.PlayedAt);
            e.HasMany(g => g.Moves).WithOne().HasForeignKey(m => m.GameId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<GameMove>().HasKey(m => new { m.GameId, m.Ply });
        b.Entity<UserGame>(e =>
        {
            e.HasKey(g => new { g.OwnerId, g.GameId });
            e.HasOne(g => g.Game).WithMany().HasForeignKey(g => g.GameId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(g => g.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(g => new { g.OwnerId, g.AddedAt });
        });
        b.Entity<ImportedProfile>(e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.OwnerId, p.Provider, p.Username }).IsUnique();
        });
        b.Entity<ImportJob>(e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(j => j.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(j => new { j.OwnerId, j.CreatedAt });
            e.HasIndex(j => new { j.State, j.LeaseUntil });
            e.Property(j => j.Version).IsConcurrencyToken();
        });
        b.Entity<ImportJobGame>(e =>
        {
            e.HasKey(g => new { g.JobId, g.GameId });
            e.HasOne<ImportJob>().WithMany().HasForeignKey(g => g.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<UserGame>().WithMany().HasForeignKey(g => new { g.OwnerId, g.GameId }).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(g => new { g.JobId, g.Ordinal });
        });
        b.Entity<ProviderThrottle>().HasKey(t => t.Provider);
        b.Entity<AnalysisRun>(e =>
        {
            e.HasOne<ImportAnalysisBatch>().WithMany().HasForeignKey(j => j.BatchId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<UserGame>().WithMany().HasForeignKey(j => new { j.OwnerId, j.GameId }).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(j => j.Moves).WithOne().HasForeignKey(m => m.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(j => new { j.OwnerId, j.GameId, j.CreatedAt });
            e.HasIndex(j => new { j.State, j.LeaseUntil });
            e.Property(j => j.Version).IsConcurrencyToken();
        });
        b.Entity<ImportAnalysisBatch>(e =>
        {
            e.HasOne<ImportJob>().WithMany().HasForeignKey(b => b.ImportJobId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(b => new { b.OwnerId, b.ImportJobId, b.Profile }).IsUnique();
            e.HasIndex(b => new { b.State, b.UpdatedAt });
        });
        b.Entity<MoveAnalysis>(e =>
        {
            e.HasKey(m => new { m.RunId, m.Ply });
            e.HasIndex(m => m.CacheIdentity);
        });
        b.Entity<PositionReviewChannel>(e =>
        {
            e.HasOne<UserGame>().WithMany().HasForeignKey(j => new { j.OwnerId, j.GameId }).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(j => new { j.OwnerId, j.UpdatedAt });
        });
        b.Entity<PositionAnalysisJob>(e =>
        {
            e.HasOne<PositionReviewChannel>().WithMany().HasForeignKey(j => j.ChannelId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(j => new { j.ChannelId, j.Sequence }).IsUnique();
            e.HasIndex(j => new { j.OwnerId, j.CreatedAt });
            e.HasIndex(j => new { j.State, j.LeaseUntil });
        });
        b.Entity<InsightSnapshot>(e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(j => j.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(j => new { j.OwnerId, j.CreatedAt });
            e.HasIndex(j => new { j.State, j.LeaseUntil });
        });
        b.Entity<Puzzle>(e =>
        {
            e.HasOne<UserGame>().WithMany().HasForeignKey(j => new { j.OwnerId, j.GameId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<MoveAnalysis>().WithMany().HasForeignKey(j => new { j.RunId, j.Ply }).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(j => new { j.OwnerId, j.RunId, j.Ply, j.ValidationVersion }).IsUnique();
            e.HasIndex(j => new { j.OwnerId, j.ReviewDueAt });
            e.HasIndex(j => new { j.State, j.LeaseUntil });
        });
        b.Entity<PuzzleAttempt>(e =>
        {
            e.HasOne<Puzzle>().WithMany().HasForeignKey(a => a.PuzzleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.OwnerId, a.PuzzleId }).IsUnique().HasFilter("\"State\" = 'active'");
            e.HasIndex(a => new { a.OwnerId, a.StartedAt });
        });
        b.Entity<CoachingJob>(e =>
        {
            e.HasOne<UserGame>().WithMany().HasForeignKey(j => new { j.OwnerId, j.GameId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<MoveAnalysis>().WithMany().HasForeignKey(j => new { j.RunId, j.Ply }).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(j => new { j.OwnerId, j.CacheIdentity }).IsUnique();
            e.HasIndex(j => new { j.OwnerId, j.CreatedAt });
            e.HasIndex(j => new { j.State, j.LeaseUntil });
            e.Property(j => j.CacheIdentity).HasMaxLength(64);
        });
        b.Entity<CoachingUsage>(e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(u => u.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CoachingJob>().WithMany().HasForeignKey(u => u.JobId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(u => new { u.OwnerId, u.CreatedAt });
            e.HasIndex(u => u.CreatedAt);
        });
    }
}
