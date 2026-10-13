using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessLens.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportBatchAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "AnalysisRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportAnalysisBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Profile = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    RunIdsJson = table.Column<string>(type: "text", nullable: false),
                    ReusedReviews = table.Column<int>(type: "integer", nullable: false),
                    CancelRequested = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportAnalysisBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportAnalysisBatches_ImportJobs_ImportJobId",
                        column: x => x.ImportJobId,
                        principalTable: "ImportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_BatchId",
                table: "AnalysisRuns",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportAnalysisBatches_ImportJobId",
                table: "ImportAnalysisBatches",
                column: "ImportJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportAnalysisBatches_OwnerId_ImportJobId_Profile",
                table: "ImportAnalysisBatches",
                columns: new[] { "OwnerId", "ImportJobId", "Profile" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportAnalysisBatches_State_UpdatedAt",
                table: "ImportAnalysisBatches",
                columns: new[] { "State", "UpdatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_AnalysisRuns_ImportAnalysisBatches_BatchId",
                table: "AnalysisRuns",
                column: "BatchId",
                principalTable: "ImportAnalysisBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnalysisRuns_ImportAnalysisBatches_BatchId",
                table: "AnalysisRuns");

            migrationBuilder.DropTable(
                name: "ImportAnalysisBatches");

            migrationBuilder.DropIndex(
                name: "IX_AnalysisRuns_BatchId",
                table: "AnalysisRuns");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "AnalysisRuns");
        }
    }
}
