using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NobodyFalls.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sanctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "banned_until",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pseudo_reset_pending",
                table: "players",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "score_excluded_until",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sanctions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    public_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: true),
                    report_id = table.Column<long>(type: "bigint", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sanctions", x => x.id);
                    table.CheckConstraint("ck_sanctions_kind", "kind in ('warning', 'pseudo_reset', 'score_exclusion', 'ban')");
                    table.CheckConstraint("ck_sanctions_period", "ends_at is null or ends_at > starts_at");
                    table.ForeignKey(
                        name: "fk_sanctions_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sanctions_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_players_banned_until",
                table: "players",
                column: "banned_until",
                filter: "banned_until is not null");

            migrationBuilder.CreateIndex(
                name: "ix_sanctions_player_id_starts_at",
                table: "sanctions",
                columns: new[] { "player_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sanctions_report_id",
                table: "sanctions",
                column: "report_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sanctions");

            migrationBuilder.DropIndex(
                name: "ix_players_banned_until",
                table: "players");

            migrationBuilder.DropColumn(
                name: "banned_until",
                table: "players");

            migrationBuilder.DropColumn(
                name: "pseudo_reset_pending",
                table: "players");

            migrationBuilder.DropColumn(
                name: "score_excluded_until",
                table: "players");
        }
    }
}
