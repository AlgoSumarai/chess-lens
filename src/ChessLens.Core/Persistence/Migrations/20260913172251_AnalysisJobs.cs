using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessLens.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    Profile = table.Column<string>(type: "text", nullable: false),
                    AnalysisVersion = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    EngineVersion = table.Column<string>(type: "text", nullable: true),
                    SettingsJson = table.Column<string>(type: "text", nullable: true),
                    CompletedPlies = table.Column<int>(type: "integer", nullable: false),
                    TotalPlies = table.Column<int>(type: "integer", nullable: false),
                    ElapsedMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    PeakEngineMemoryBytes = table.Column<long>(type: "bigint", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseToken = table.Column<string>(type: "text", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelRequested = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalysisRuns_UserGames_OwnerId_GameId",
                        columns: x => new { x.OwnerId, x.GameId },
                        principalTable: "UserGames",
                        principalColumns: new[] { "OwnerId", "GameId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MoveAnalyses",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ply = table.Column<int>(type: "integer", nullable: false),
                    FenBefore = table.Column<string>(type: "text", nullable: false),
                    PlayedMove = table.Column<string>(type: "text", nullable: false),
                    Side = table.Column<string>(type: "text", nullable: false),
                    BestMove = table.Column<string>(type: "text", nullable: false),
                    BestScoreKind = table.Column<string>(type: "text", nullable: false),
                    BestScoreValue = table.Column<int>(type: "integer", nullable: false),
                    PlayedScoreKind = table.Column<string>(type: "text", nullable: false),
                    PlayedScoreValue = table.Column<int>(type: "integer", nullable: false),
                    CentipawnLoss = table.Column<int>(type: "integer", nullable: true),
                    MateTransition = table.Column<string>(type: "text", nullable: true),
                    Classification = table.Column<string>(type: "text", nullable: false),
                    Provisional = table.Column<bool>(type: "boolean", nullable: false),
                    BestSearchJson = table.Column<string>(type: "text", nullable: false),
                    PlayedSearchJson = table.Column<string>(type: "text", nullable: false),
                    BudgetJson = table.Column<string>(type: "text", nullable: false),
                    CacheIdentity = table.Column<string>(type: "text", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    Nodes = table.Column<long>(type: "bigint", nullable: false),
                    Complete = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoveAnalyses", x => new { x.RunId, x.Ply });
                    table.ForeignKey(
                        name: "FK_MoveAnalyses_AnalysisRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "AnalysisRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_OwnerId_GameId_CreatedAt",
                table: "AnalysisRuns",
                columns: new[] { "OwnerId", "GameId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_State_LeaseUntil",
                table: "AnalysisRuns",
                columns: new[] { "State", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_MoveAnalyses_CacheIdentity",
                table: "MoveAnalyses",
                column: "CacheIdentity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MoveAnalyses");

            migrationBuilder.DropTable(
                name: "AnalysisRuns");
        }
    }
}
