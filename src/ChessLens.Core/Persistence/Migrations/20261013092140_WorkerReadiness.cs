using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessLens.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkerReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkerStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSeen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EngineCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MainLaneAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InteractiveLaneAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Ready = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerStatuses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerStatuses_LastSeen",
                table: "WorkerStatuses",
                column: "LastSeen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkerStatuses");
        }
    }
}
