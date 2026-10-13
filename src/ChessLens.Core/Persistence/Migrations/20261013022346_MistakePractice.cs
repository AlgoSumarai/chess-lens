using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessLens.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MistakePractice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PracticeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ply = table.Column<int>(type: "integer", nullable: false),
                    WeaknessSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Side = table.Column<string>(type: "text", nullable: false),
                    StartingPositionJson = table.Column<string>(type: "text", nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "text", nullable: false),
                    ObjectiveJson = table.Column<string>(type: "text", nullable: false),
                    RulesVersion = table.Column<string>(type: "text", nullable: false),
                    CurrentAttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeSessions_InsightSnapshots_WeaknessSnapshotId",
                        column: x => x.WeaknessSnapshotId,
                        principalTable: "InsightSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PracticeSessions_MoveAnalyses_RunId_Ply",
                        columns: x => new { x.RunId, x.Ply },
                        principalTable: "MoveAnalyses",
                        principalColumns: new[] { "RunId", "Ply" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PracticeSessions_UserGames_OwnerId_GameId",
                        columns: x => new { x.OwnerId, x.GameId },
                        principalTable: "UserGames",
                        principalColumns: new[] { "OwnerId", "GameId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Units = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceUsages_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PracticeAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    HistoryJson = table.Column<string>(type: "text", nullable: false),
                    GradesJson = table.Column<string>(type: "text", nullable: false),
                    AllGradesJson = table.Column<string>(type: "text", nullable: false),
                    JournalJson = table.Column<string>(type: "text", nullable: false),
                    InitialCheckJson = table.Column<string>(type: "text", nullable: true),
                    ObjectiveJson = table.Column<string>(type: "text", nullable: false),
                    OpponentSearchesJson = table.Column<string>(type: "text", nullable: false),
                    HintEvidenceJson = table.Column<string>(type: "text", nullable: false),
                    Hint = table.Column<string>(type: "text", nullable: true),
                    FinalCheckJson = table.Column<string>(type: "text", nullable: true),
                    Conclusion = table.Column<string>(type: "text", nullable: true),
                    HintsUsed = table.Column<int>(type: "integer", nullable: false),
                    UndosUsed = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeAttempts_PracticeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "PracticeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PracticeJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionVersion = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseToken = table.Column<string>(type: "text", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeJobs_PracticeAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "PracticeAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PracticeJobs_PracticeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "PracticeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeAttempts_SessionId_Number",
                table: "PracticeAttempts",
                columns: new[] { "SessionId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeAttempts_StartedAt",
                table: "PracticeAttempts",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeJobs_AttemptId",
                table: "PracticeJobs",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeJobs_SessionId_SessionVersion",
                table: "PracticeJobs",
                columns: new[] { "SessionId", "SessionVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeJobs_State_LeaseUntil",
                table: "PracticeJobs",
                columns: new[] { "State", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_OwnerId_GameId",
                table: "PracticeSessions",
                columns: new[] { "OwnerId", "GameId" });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_OwnerId_RunId_Ply_RulesVersion",
                table: "PracticeSessions",
                columns: new[] { "OwnerId", "RunId", "Ply", "RulesVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_OwnerId_UpdatedAt",
                table: "PracticeSessions",
                columns: new[] { "OwnerId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_RunId_Ply",
                table: "PracticeSessions",
                columns: new[] { "RunId", "Ply" });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_WeaknessSnapshotId",
                table: "PracticeSessions",
                column: "WeaknessSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceUsages_OwnerId_Kind_CreatedAt",
                table: "ResourceUsages",
                columns: new[] { "OwnerId", "Kind", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PracticeJobs");

            migrationBuilder.DropTable(
                name: "ResourceUsages");

            migrationBuilder.DropTable(
                name: "PracticeAttempts");

            migrationBuilder.DropTable(
                name: "PracticeSessions");
        }
    }
}
