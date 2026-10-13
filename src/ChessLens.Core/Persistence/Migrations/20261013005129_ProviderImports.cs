using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessLens.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProviderImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckpointJson",
                table: "ImportJobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "ImportJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "ImportJobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinishedAt",
                table: "Games",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Rated",
                table: "Games",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RatingPool",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "Games",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportJobGames",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportJobGames", x => new { x.JobId, x.GameId });
                    table.ForeignKey(
                        name: "FK_ImportJobGames_ImportJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ImportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImportJobGames_UserGames_OwnerId_GameId",
                        columns: x => new { x.OwnerId, x.GameId },
                        principalTable: "UserGames",
                        principalColumns: new[] { "OwnerId", "GameId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProviderThrottles",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "text", nullable: false),
                    NotBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderThrottles", x => x.Provider);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobGames_JobId_Ordinal",
                table: "ImportJobGames",
                columns: new[] { "JobId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobGames_OwnerId_GameId",
                table: "ImportJobGames",
                columns: new[] { "OwnerId", "GameId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportJobGames");

            migrationBuilder.DropTable(
                name: "ProviderThrottles");

            migrationBuilder.DropColumn(
                name: "CheckpointJson",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "FinishedAt",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Rated",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "RatingPool",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "Games");
        }
    }
}
