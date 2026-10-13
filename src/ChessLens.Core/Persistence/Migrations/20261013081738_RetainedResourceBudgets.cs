using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessLens.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetainedResourceBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Carry available prior reservations forward; expired usage does not affect a rolling-day limit.
            migrationBuilder.Sql("""
                INSERT INTO "ResourceUsages" ("Id", "OwnerId", "Kind", "Units", "CreatedAt")
                SELECT gen_random_uuid(), "OwnerId", 'import', 1, "CreatedAt" FROM "ImportJobs" WHERE "CreatedAt" > now() - interval '1 day'
                UNION ALL SELECT gen_random_uuid(), "OwnerId", 'analysis-plies', "TotalPlies", "CreatedAt" FROM "AnalysisRuns" WHERE "CreatedAt" > now() - interval '1 day' AND "TotalPlies" > 0
                UNION ALL SELECT gen_random_uuid(), "OwnerId", 'position-review', 1, "CreatedAt" FROM "PositionAnalysisJobs" WHERE "CreatedAt" > now() - interval '1 day'
                UNION ALL SELECT gen_random_uuid(), "OwnerId", 'position-channel', 1, "UpdatedAt" FROM "PositionReviewChannels" WHERE "UpdatedAt" > now() - interval '1 day'
                UNION ALL SELECT gen_random_uuid(), "OwnerId", 'insight', 1, "CreatedAt" FROM "InsightSnapshots" WHERE "CreatedAt" > now() - interval '1 day'
                UNION ALL SELECT gen_random_uuid(), "OwnerId", 'puzzle-validation', 1, "CreatedAt" FROM "Puzzles" WHERE "CreatedAt" > now() - interval '1 day'
                UNION ALL SELECT gen_random_uuid(), "OwnerId", 'puzzle-attempt', 1, "StartedAt" FROM "PuzzleAttempts" WHERE "StartedAt" > now() - interval '1 day'
                UNION ALL SELECT gen_random_uuid(), "OwnerId", 'coaching', 1, "CreatedAt" FROM "CoachingJobs" WHERE "CreatedAt" > now() - interval '1 day';
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Retained resource accounting must not be replayed or discarded. Restore a pre-upgrade backup or apply a forward migration.");
        }
    }
}
