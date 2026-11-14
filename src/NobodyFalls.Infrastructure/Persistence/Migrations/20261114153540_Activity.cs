using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NobodyFalls.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Activity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "login_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_agent_family = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_events", x => x.id);
                    table.CheckConstraint("ck_login_events_kind", "kind in ('sign_in', 'visit')");
                    table.ForeignKey(
                        name: "fk_login_events_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_visits",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_visits", x => new { x.player_id, x.day });
                    table.ForeignKey(
                        name: "fk_player_visits_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_login_events_at",
                table: "login_events",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_login_events_device_id",
                table: "login_events",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_login_events_ip",
                table: "login_events",
                column: "ip");

            migrationBuilder.CreateIndex(
                name: "ix_login_events_player_id_day_ip_device_id",
                table: "login_events",
                columns: new[] { "player_id", "day", "ip", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_visits_day",
                table: "player_visits",
                column: "day");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "login_events");

            migrationBuilder.DropTable(
                name: "player_visits");
        }
    }
}
