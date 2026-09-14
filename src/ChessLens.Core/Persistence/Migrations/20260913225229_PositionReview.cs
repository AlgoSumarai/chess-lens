using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessLens.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PositionReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PositionReviewChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionReviewChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PositionReviewChannels_UserGames_OwnerId_GameId",
                        columns: x => new { x.OwnerId, x.GameId },
                        principalTable: "UserGames",
                        principalColumns: new[] { "OwnerId", "GameId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PositionAnalysisJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Ply = table.Column<int>(type: "integer", nullable: false),
                    InitialFen = table.Column<string>(type: "text", nullable: false),
                    HistoryJson = table.Column<string>(type: "text", nullable: false),
                    Fen = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    CacheIdentity = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseToken = table.Column<string>(type: "text", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionAnalysisJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PositionAnalysisJobs_PositionReviewChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "PositionReviewChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PositionAnalysisJobs_ChannelId_Sequence",
                table: "PositionAnalysisJobs",
                columns: new[] { "ChannelId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PositionAnalysisJobs_OwnerId_CreatedAt",
                table: "PositionAnalysisJobs",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PositionAnalysisJobs_State_LeaseUntil",
                table: "PositionAnalysisJobs",
                columns: new[] { "State", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_PositionReviewChannels_OwnerId_GameId",
                table: "PositionReviewChannels",
                columns: new[] { "OwnerId", "GameId" });

            migrationBuilder.CreateIndex(
                name: "IX_PositionReviewChannels_OwnerId_UpdatedAt",
                table: "PositionReviewChannels",
                columns: new[] { "OwnerId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PositionAnalysisJobs");

            migrationBuilder.DropTable(
                name: "PositionReviewChannels");
        }
    }
}
