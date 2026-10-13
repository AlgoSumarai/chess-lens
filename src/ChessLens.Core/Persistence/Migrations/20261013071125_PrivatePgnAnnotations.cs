using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessLens.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrivatePgnAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClockSecondsJson",
                table: "UserGames",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalPgn",
                table: "UserGames",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LegacyPgnAnnotations",
                columns: table => new
                {
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawPgn = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyPgnAnnotations", x => x.GameId);
                    table.ForeignKey(
                        name: "FK_LegacyPgnAnnotations_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            // Preserve exact legacy uploads separately: prior releases did not record their creator.
            // Canonical chess identities, game/run IDs, moves, evaluations and completed snapshots stay intact.
            migrationBuilder.Sql("""
                INSERT INTO "LegacyPgnAnnotations" ("GameId", "RawPgn")
                SELECT "Id", "RawPgn" FROM "Games" WHERE "Provider" = 'pgn';
                UPDATE "Games" g SET "RawPgn" =
                    COALESCE((SELECT string_agg('[' || h.key || ' "' ||
                        replace(replace(h.value, E'\\', E'\\\\'), '"', E'\\"') || '"]', E'\n' ORDER BY lower(h.key))
                        FROM jsonb_each_text(g."HeadersJson"::jsonb) h
                        WHERE lower(h.key) NOT IN ('annotator', 'plycount')), '') || E'\n\n' ||
                    COALESCE((SELECT string_agg(m."San", ' ' ORDER BY m."Ply") FROM "Moves" m WHERE m."GameId" = g."Id"), '') || ' ' || g."Result",
                    "HeadersJson" = COALESCE((SELECT jsonb_object_agg(h.key, h.value)
                        FROM jsonb_each_text(g."HeadersJson"::jsonb) h WHERE lower(h.key) NOT IN ('annotator', 'plycount')), '{}'::jsonb)::text
                WHERE g."Provider" = 'pgn';
                UPDATE "Moves" m SET "ClockSeconds" = NULL FROM "Games" g
                WHERE m."GameId" = g."Id" AND g."Provider" = 'pgn';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Private annotation storage cannot be safely downgraded. Restore a pre-upgrade backup or apply a forward migration.");
        }
    }
}
