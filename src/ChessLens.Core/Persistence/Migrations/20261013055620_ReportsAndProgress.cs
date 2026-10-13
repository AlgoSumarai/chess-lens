using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessLens.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReportsAndProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Objective",
                table: "Puzzles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DataVersion",
                table: "AspNetUsers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ReportSchedules",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Timezone = table.Column<string>(type: "text", nullable: false),
                    ScanBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CycleWeekFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextCheckAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseToken = table.Column<string>(type: "text", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportSchedules", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_ReportSchedules_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ScopeIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CacheIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InputsJson = table.Column<string>(type: "text", nullable: false),
                    RulesVersion = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_ReportSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportSnapshots_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSchedules_NextCheckAt_LeaseUntil",
                table: "ReportSchedules",
                columns: new[] { "NextCheckAt", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSnapshots_OwnerId_CacheIdentity",
                table: "ReportSnapshots",
                columns: new[] { "OwnerId", "CacheIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportSnapshots_OwnerId_CreatedAt",
                table: "ReportSnapshots",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSnapshots_OwnerId_ScopeIdentity_Revision",
                table: "ReportSnapshots",
                columns: new[] { "OwnerId", "ScopeIdentity", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportSnapshots_State_LeaseUntil",
                table: "ReportSnapshots",
                columns: new[] { "State", "LeaseUntil" });
            migrationBuilder.Sql("""
                CREATE FUNCTION chesslens_time_control_group_v1(control text, pool text) RETURNS text
                LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE AS $body$
                DECLARE effective bigint;
                BEGIN
                  IF pool IN ('correspondence', 'daily') THEN RETURN 'correspondence'; END IF;
                  IF control IS NULL OR length(control) > 100 THEN RETURN 'unknown'; END IF;
                  IF control ~ '^1/[0-9]{5,8}$' THEN
                    IF substring(control from 3)::bigint >= 86400 THEN RETURN 'correspondence'; END IF;
                  END IF;
                  IF control !~ '^[0-9]{1,8}(\+[0-9]{1,4})?$' THEN RETURN 'unknown'; END IF;
                  effective := split_part(control, '+', 1)::bigint;
                  IF position('+' in control) > 0 THEN effective := effective + 40 * split_part(control, '+', 2)::bigint; END IF;
                  IF effective <= 0 THEN RETURN 'unknown';
                  ELSIF effective < 180 THEN RETURN 'bullet';
                  ELSIF effective < 600 THEN RETURN 'blitz';
                  ELSIF effective < 1800 THEN RETURN 'rapid';
                  ELSE RETURN 'classical'; END IF;
                END;
                $body$;
                UPDATE "Puzzles" SET "Objective" = "SolutionJson"::jsonb ->> 'Objective' WHERE "SolutionJson" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION chesslens_time_control_group_v1(text, text);");
            migrationBuilder.DropTable(
                name: "ReportSchedules");

            migrationBuilder.DropTable(
                name: "ReportSnapshots");

            migrationBuilder.DropColumn(
                name: "Objective",
                table: "Puzzles");

            migrationBuilder.DropColumn(
                name: "DataVersion",
                table: "AspNetUsers");
        }
    }
}
