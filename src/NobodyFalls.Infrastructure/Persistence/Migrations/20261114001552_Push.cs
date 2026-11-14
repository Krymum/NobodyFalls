using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NobodyFalls.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Push : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_outbox_pending",
                table: "notification_outbox");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_until",
                table: "notification_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "friend_reminder_at",
                table: "game_days",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "storm_announced_at",
                table: "game_days",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    friend_below = table.Column<bool>(type: "boolean", nullable: false),
                    friend_fell = table.Column<bool>(type: "boolean", nullable: false),
                    saved_me = table.Column<bool>(type: "boolean", nullable: false),
                    fell = table.Column<bool>(type: "boolean", nullable: false),
                    storm = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_preferences", x => x.player_id);
                    table.ForeignKey(
                        name: "fk_notification_preferences_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    endpoint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    p256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    auth = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_subscriptions", x => x.endpoint);
                    table.ForeignKey(
                        name: "fk_push_subscriptions_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vapid_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    public_key = table.Column<string>(type: "text", nullable: false),
                    private_key = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vapid_keys", x => x.id);
                    table.CheckConstraint("ck_vapid_keys_single_row", "id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_pending",
                table: "notification_outbox",
                column: "id",
                filter: "sent_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_player_id",
                table: "push_subscriptions",
                column: "player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_preferences");

            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropTable(
                name: "vapid_keys");

            migrationBuilder.DropIndex(
                name: "ix_notification_outbox_pending",
                table: "notification_outbox");

            migrationBuilder.DropColumn(
                name: "locked_until",
                table: "notification_outbox");

            migrationBuilder.DropColumn(
                name: "friend_reminder_at",
                table: "game_days");

            migrationBuilder.DropColumn(
                name: "storm_announced_at",
                table: "game_days");

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_pending",
                table: "notification_outbox",
                column: "created_at",
                filter: "sent_at is null");
        }
    }
}
