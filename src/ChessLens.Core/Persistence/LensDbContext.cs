using ChessLens.Core.Accounts;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Imports;
using ChessLens.Core.Insights;
using ChessLens.Core.Training;
using ChessLens.Core.Coaching;
using ChessLens.Core.Reports;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ChessLens.Core.Persistence;

public sealed class LensDbContext(DbContextOptions<LensDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<SourceGame> Games => Set<SourceGame>();
    public DbSet<GameMove> Moves => Set<GameMove>();
    public DbSet<UserGame> UserGames => Set<UserGame>();
    public DbSet<LegacyPgnAnnotation> LegacyPgnAnnotations => Set<LegacyPgnAnnotation>();
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
    public DbSet<PracticeSession> PracticeSessions => Set<PracticeSession>();
    public DbSet<PracticeAttempt> PracticeAttempts => Set<PracticeAttempt>();
    public DbSet<PracticeJob> PracticeJobs => Set<PracticeJob>();
    public DbSet<ResourceUsage> ResourceUsages => Set<ResourceUsage>();
    public DbSet<ReportSnapshot> ReportSnapshots => Set<ReportSnapshot>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AppUser>().Property(u => u.DataVersion).IsConcurrencyToken();
        b.HasDbFunction(typeof(TimeControlGroups).GetMethod(nameof(TimeControlGroups.SqlGroup))!).HasName("chesslens_time_control_group_v1");
        b.Entity<SourceGame>(e =>
        {
            e.HasIndex(g => g.Fingerprint).IsUnique();
            e.Property(g => g.Fingerprint).HasMaxLength(64);
            e.HasIndex(g => new { g.Provider, g.ExternalId }).IsUnique();
            e.HasIndex(g => g.PlayedAt);
            e.HasMany(g => g.Moves).WithOne().HasForeignKey(m => m.GameId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<GameMove>().HasKey(m => new { m.GameId, m.Ply });
        b.Entity<LegacyPgnAnnotation>(e =>
        {
            e.HasKey(a => a.GameId);
            e.HasOne<SourceGame>().WithOne().HasForeignKey<LegacyPgnAnnotation>(a => a.GameId).OnDelete(DeleteBehavior.Cascade);
        });
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
        b.Entity<PracticeSession>(e =>
        {
            e.HasOne<UserGame>().WithMany().HasForeignKey(s => new { s.OwnerId, s.GameId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<MoveAnalysis>().WithMany().HasForeignKey(s => new { s.RunId, s.Ply }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<InsightSnapshot>().WithMany().HasForeignKey(s => s.WeaknessSnapshotId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => new { s.OwnerId, s.RunId, s.Ply, s.RulesVersion }).IsUnique();
            e.HasIndex(s => new { s.OwnerId, s.UpdatedAt });
            e.Property(s => s.Version).IsConcurrencyToken();
        });
        b.Entity<PracticeAttempt>(e =>
        {
            e.HasOne<PracticeSession>().WithMany().HasForeignKey(a => a.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.SessionId, a.Number }).IsUnique();
            e.HasIndex(a => a.StartedAt);
        });
        b.Entity<PracticeJob>(e =>
        {
            e.HasOne<PracticeSession>().WithMany().HasForeignKey(j => j.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PracticeAttempt>().WithMany().HasForeignKey(j => j.AttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(j => new { j.SessionId, j.SessionVersion }).IsUnique();
            e.HasIndex(j => new { j.State, j.LeaseUntil });
        });
        b.Entity<ResourceUsage>(e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(u => u.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(u => new { u.OwnerId, u.Kind, u.CreatedAt });
        });
        b.Entity<ReportSnapshot>(e =>
        {
            e.HasOne<AppUser>().WithMany().HasForeignKey(r => r.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.OwnerId, r.CacheIdentity }).IsUnique();
            e.HasIndex(r => new { r.OwnerId, r.ScopeIdentity, r.Revision }).IsUnique();
            e.HasIndex(r => new { r.State, r.LeaseUntil });
            e.HasIndex(r => new { r.OwnerId, r.CreatedAt });
            e.Property(r => r.CacheIdentity).HasMaxLength(64);
            e.Property(r => r.ScopeIdentity).HasMaxLength(64);
        });
        b.Entity<ReportSchedule>(e =>
        {
            e.HasKey(s => s.OwnerId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.NextCheckAt, s.LeaseUntil });
        });
    }
}
