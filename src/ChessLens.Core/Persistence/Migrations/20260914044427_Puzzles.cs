using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessLens.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Puzzles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Puzzles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ply = table.Column<int>(type: "integer", nullable: false),
                    ValidationVersion = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    SolutionJson = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseToken = table.Column<string>(type: "text", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ReviewStreak = table.Column<int>(type: "integer", nullable: false),
                    ReviewDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Puzzles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Puzzles_MoveAnalyses_RunId_Ply",
                        columns: x => new { x.RunId, x.Ply },
                        principalTable: "MoveAnalyses",
                        principalColumns: new[] { "RunId", "Ply" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Puzzles_UserGames_OwnerId_GameId",
                        columns: x => new { x.OwnerId, x.GameId },
                        principalTable: "UserGames",
                        principalColumns: new[] { "OwnerId", "GameId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PuzzleAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    PuzzleId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    HistoryJson = table.Column<string>(type: "text", nullable: false),
                    JournalJson = table.Column<string>(type: "text", nullable: false),
                    HintsUsed = table.Column<int>(type: "integer", nullable: false),
                    NodeHintsUsed = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SolveSeconds = table.Column<int>(type: "integer", nullable: true),
                    ReviewDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuzzleAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PuzzleAttempts_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PuzzleAttempts_Puzzles_PuzzleId",
                        column: x => x.PuzzleId,
                        principalTable: "Puzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleAttempts_OwnerId_PuzzleId",
                table: "PuzzleAttempts",
                columns: new[] { "OwnerId", "PuzzleId" },
                unique: true,
                filter: "\"State\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleAttempts_OwnerId_StartedAt",
                table: "PuzzleAttempts",
                columns: new[] { "OwnerId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleAttempts_PuzzleId",
                table: "PuzzleAttempts",
                column: "PuzzleId");

            migrationBuilder.CreateIndex(
                name: "IX_Puzzles_OwnerId_GameId",
                table: "Puzzles",
                columns: new[] { "OwnerId", "GameId" });

            migrationBuilder.CreateIndex(
                name: "IX_Puzzles_OwnerId_ReviewDueAt",
                table: "Puzzles",
                columns: new[] { "OwnerId", "ReviewDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Puzzles_OwnerId_RunId_Ply_ValidationVersion",
                table: "Puzzles",
                columns: new[] { "OwnerId", "RunId", "Ply", "ValidationVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Puzzles_RunId_Ply",
                table: "Puzzles",
                columns: new[] { "RunId", "Ply" });

            migrationBuilder.CreateIndex(
                name: "IX_Puzzles_State_LeaseUntil",
                table: "Puzzles",
                columns: new[] { "State", "LeaseUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PuzzleAttempts");

            migrationBuilder.DropTable(
                name: "Puzzles");
        }
    }
}
