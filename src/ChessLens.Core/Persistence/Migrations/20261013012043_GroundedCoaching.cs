using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessLens.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GroundedCoaching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseModelCoaching",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CoachingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ply = table.Column<int>(type: "integer", nullable: false),
                    CacheIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ModelRequested = table.Column<bool>(type: "boolean", nullable: false),
                    ModelAttempted = table.Column<bool>(type: "boolean", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: true),
                    PromptVersion = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseToken = table.Column<string>(type: "text", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachingJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoachingJobs_MoveAnalyses_RunId_Ply",
                        columns: x => new { x.RunId, x.Ply },
                        principalTable: "MoveAnalyses",
                        principalColumns: new[] { "RunId", "Ply" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoachingJobs_UserGames_OwnerId_GameId",
                        columns: x => new { x.OwnerId, x.GameId },
                        principalTable: "UserGames",
                        principalColumns: new[] { "OwnerId", "GameId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoachingUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReservedMicroDollars = table.Column<long>(type: "bigint", nullable: false),
                    MaximumInputBytes = table.Column<int>(type: "integer", nullable: false),
                    MaximumOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachingUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoachingUsages_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoachingUsages_CoachingJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "CoachingJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachingJobs_OwnerId_CacheIdentity",
                table: "CoachingJobs",
                columns: new[] { "OwnerId", "CacheIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoachingJobs_OwnerId_CreatedAt",
                table: "CoachingJobs",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachingJobs_OwnerId_GameId",
                table: "CoachingJobs",
                columns: new[] { "OwnerId", "GameId" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachingJobs_RunId_Ply",
                table: "CoachingJobs",
                columns: new[] { "RunId", "Ply" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachingJobs_State_LeaseUntil",
                table: "CoachingJobs",
                columns: new[] { "State", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_CoachingUsages_CreatedAt",
                table: "CoachingUsages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CoachingUsages_JobId",
                table: "CoachingUsages",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_CoachingUsages_OwnerId_CreatedAt",
                table: "CoachingUsages",
                columns: new[] { "OwnerId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoachingUsages");

            migrationBuilder.DropTable(
                name: "CoachingJobs");

            migrationBuilder.DropColumn(
                name: "UseModelCoaching",
                table: "AspNetUsers");
        }
    }
}
