using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NobodyFalls.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dev_clock",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    offset_ms = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dev_clock", x => x.id);
                    table.CheckConstraint("ck_dev_clock_single_row", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "game_settings",
                columns: table => new
                {
                    version = table.Column<int>(type: "integer", nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_settings", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "players",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pseudo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pseudo_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    joined_day = table.Column<DateOnly>(type: "date", nullable: true),
                    invited_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invite_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    guardian_score = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    falls_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_fall_day = table.Column<DateOnly>(type: "date", nullable: true),
                    returns_on_day = table.Column<DateOnly>(type: "date", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    map_x = table.Column<float>(type: "real", nullable: true),
                    map_y = table.Column<float>(type: "real", nullable: true),
                    tile_x = table.Column<int>(type: "integer", nullable: true),
                    tile_y = table.Column<int>(type: "integer", nullable: true),
                    map_name_visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "friends")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_players", x => x.id);
                    table.CheckConstraint("ck_players_kind", "kind in ('human', 'watcher')");
                    table.CheckConstraint("ck_players_map_name_visibility", "map_name_visibility in ('friends', 'everyone', 'nobody')");
                    table.CheckConstraint("ck_players_status", "status in ('waitlisted', 'active', 'fallen')");
                    table.ForeignKey(
                        name: "fk_players_players_invited_by_id",
                        column: x => x.invited_by_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    opening_day = table.Column<DateOnly>(type: "date", nullable: false),
                    last_day = table.Column<DateOnly>(type: "date", nullable: true),
                    deaths = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seasons", x => x.id);
                    table.CheckConstraint("ck_seasons_dates", "last_day is null or last_day >= opening_day");
                });

            migrationBuilder.CreateTable(
                name: "skin_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rarity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    render_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    colors = table.Column<List<string>>(type: "text[]", nullable: false),
                    unlock_metric = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    unlock_threshold = table.Column<int>(type: "integer", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skin_items", x => x.id);
                    table.CheckConstraint("ck_skin_items_condition", "(unlock_metric is null) = (unlock_threshold is null)");
                });

            migrationBuilder.CreateTable(
                name: "skin_tones",
                columns: table => new
                {
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skin_tones", x => x.color);
                });

            migrationBuilder.CreateTable(
                name: "storms",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storms", x => x.day);
                });

            migrationBuilder.CreateTable(
                name: "blocks",
                columns: table => new
                {
                    blocker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blocks", x => new { x.blocker_id, x.blocked_id });
                    table.CheckConstraint("ck_blocks_not_self", "blocker_id <> blocked_id");
                    table.ForeignKey(
                        name: "fk_blocks_players_blocked_id",
                        column: x => x.blocked_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_blocks_players_blocker_id",
                        column: x => x.blocker_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "friendships",
                columns: table => new
                {
                    player_low = table.Column<Guid>(type: "uuid", nullable: false),
                    player_high = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_friendships", x => new { x.player_low, x.player_high });
                    table.CheckConstraint("ck_friendships_order", "player_low < player_high");
                    table.CheckConstraint("ck_friendships_source", "source in ('request', 'invite')");
                    table.ForeignKey(
                        name: "fk_friendships_players_player_high",
                        column: x => x.player_high,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_friendships_players_player_low",
                        column: x => x.player_low,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hold_pairs",
                columns: table => new
                {
                    player_low = table.Column<Guid>(type: "uuid", nullable: false),
                    player_high = table.Column<Guid>(type: "uuid", nullable: false),
                    nights = table.Column<int>(type: "integer", nullable: false),
                    last_day = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hold_pairs", x => new { x.player_low, x.player_high });
                    table.CheckConstraint("ck_hold_pairs_order", "player_low < player_high");
                    table.ForeignKey(
                        name: "fk_hold_pairs_players_player_high",
                        column: x => x.player_high,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hold_pairs_players_player_low",
                        column: x => x.player_low,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "life_debts",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    saved_id = table.Column<Guid>(type: "uuid", nullable: false),
                    saver_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_life_debts", x => new { x.day, x.saved_id, x.saver_id });
                    table.ForeignKey(
                        name: "fk_life_debts_players_saved_id",
                        column: x => x.saved_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_life_debts_players_saver_id",
                        column: x => x.saver_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_outbox", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_outbox_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_stats",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rescues = table.Column<int>(type: "integer", nullable: false),
                    strangers_held = table.Column<int>(type: "integer", nullable: false),
                    nights_survived = table.Column<int>(type: "integer", nullable: false),
                    storms_survived = table.Column<int>(type: "integer", nullable: false),
                    updated_day = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_stats", x => x.player_id);
                    table.ForeignKey(
                        name: "fk_player_stats_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_days",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    season_id = table.Column<int>(type: "integer", nullable: false),
                    night_number = table.Column<int>(type: "integer", nullable: false),
                    base_required = table.Column<int>(type: "integer", nullable: false),
                    storm = table.Column<bool>(type: "boolean", nullable: false),
                    alive_at_start = table.Column<int>(type: "integer", nullable: false),
                    watcher_count = table.Column<int>(type: "integer", nullable: false),
                    settings_version = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    watcher_pass_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    falls = table.Column<int>(type: "integer", nullable: true),
                    streak_after = table.Column<int>(type: "integer", nullable: true),
                    deaths_all_time_after = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_days", x => x.day);
                    table.ForeignKey(
                        name: "fk_game_days_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "player_falls",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    season_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_falls", x => new { x.player_id, x.day });
                    table.ForeignKey(
                        name: "fk_player_falls_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_player_falls_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "season_scores",
                columns: table => new
                {
                    season_id = table.Column<int>(type: "integer", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_season_scores", x => new { x.season_id, x.player_id });
                    table.ForeignKey(
                        name: "fk_season_scores_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_season_scores_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "player_skin_unlocks",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<string>(type: "character varying(64)", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_skin_unlocks", x => new { x.player_id, x.item_id });
                    table.ForeignKey(
                        name: "fk_player_skin_unlocks_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_player_skin_unlocks_skin_items_item_id",
                        column: x => x.item_id,
                        principalTable: "skin_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hands",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    giver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<short>(type: "smallint", nullable: false),
                    given_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    giver_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    giver_was_friend = table.Column<bool>(type: "boolean", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: true),
                    reciprocal = table.Column<bool>(type: "boolean", nullable: true),
                    last_rampart = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hands", x => x.id);
                    table.CheckConstraint("ck_hands_giver_kind", "giver_kind in ('human', 'watcher')");
                    table.CheckConstraint("ck_hands_not_self", "giver_id <> receiver_id");
                    table.CheckConstraint("ck_hands_slot", "slot between 1 and 10");
                    table.ForeignKey(
                        name: "fk_hands_game_days_day",
                        column: x => x.day,
                        principalTable: "game_days",
                        principalColumn: "day",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hands_players_giver_id",
                        column: x => x.giver_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hands_players_receiver_id",
                        column: x => x.receiver_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_days",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required = table.Column<int>(type: "integer", nullable: false),
                    immune = table.Column<bool>(type: "boolean", nullable: false),
                    received = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    score_gained = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_days", x => new { x.day, x.player_id });
                    table.CheckConstraint("ck_player_days_counts", "required >= 0 and received >= 0");
                    table.CheckConstraint("ck_player_days_outcome", "outcome is null or outcome in ('survived', 'fell', 'immune')");
                    table.ForeignKey(
                        name: "fk_player_days_game_days_day",
                        column: x => x.day,
                        principalTable: "game_days",
                        principalColumn: "day",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_player_days_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "game_settings",
                columns: new[] { "version", "comment", "created_at", "created_by", "document" },
                values: new object[] { 1, "Defaults from docs/GAME_DESIGN.md", new DateTimeOffset(new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "{\"handsPerDay\":3,\"baseRequired\":2,\"lowPopulationRequired\":1,\"lowPopulationBelow\":50,\"fragileExtra\":1,\"stormExtra\":1,\"stormNotice\":\"06:00:00\",\"returnCutoff\":\"06:00:00\",\"lastRampartWindow\":\"01:00:00\",\"pointsFriend\":1,\"pointsStranger\":2,\"lastRampartBonus\":2,\"watchers\":{\"maxCount\":10,\"disappearAtPopulation\":200,\"passAt\":\"22:00:00\",\"handsPerWatcher\":3},\"friendLimit\":500,\"suggestionMinNights\":2,\"friendReminderAt\":\"21:00:00\"}" });

            migrationBuilder.InsertData(
                table: "skin_tones",
                columns: new[] { "color", "sort" },
                values: new object[,]
                {
                    { "#5E4536", 4 },
                    { "#8D6E5A", 3 },
                    { "#B49A86", 2 },
                    { "#C9C3B9", 0 },
                    { "#E0D3C4", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_blocks_blocked_id",
                table: "blocks",
                column: "blocked_id");

            migrationBuilder.CreateIndex(
                name: "ix_friendships_player_high",
                table: "friendships",
                column: "player_high");

            migrationBuilder.CreateIndex(
                name: "ix_game_days_resolved_at",
                table: "game_days",
                column: "resolved_at",
                filter: "resolved_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_game_days_season_id",
                table: "game_days",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ix_hands_day_receiver_id_given_at",
                table: "hands",
                columns: new[] { "day", "receiver_id", "given_at" });

            migrationBuilder.CreateIndex(
                name: "ix_hands_receiver_id",
                table: "hands",
                column: "receiver_id");

            migrationBuilder.CreateIndex(
                name: "ux_hands_giver_day_slot",
                table: "hands",
                columns: new[] { "giver_id", "day", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_hands_giver_receiver_day",
                table: "hands",
                columns: new[] { "giver_id", "receiver_id", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hold_pairs_player_high",
                table: "hold_pairs",
                column: "player_high");

            migrationBuilder.CreateIndex(
                name: "ix_life_debts_saved_id",
                table: "life_debts",
                column: "saved_id");

            migrationBuilder.CreateIndex(
                name: "ix_life_debts_saver_id",
                table: "life_debts",
                column: "saver_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_dedupe_key",
                table: "notification_outbox",
                column: "dedupe_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_pending",
                table: "notification_outbox",
                column: "created_at",
                filter: "sent_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_player_id",
                table: "notification_outbox",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_days_edge",
                table: "player_days",
                column: "day",
                filter: "received < required");

            migrationBuilder.CreateIndex(
                name: "ix_player_days_player_id",
                table: "player_days",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_falls_season_id",
                table: "player_falls",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_skin_unlocks_item_id",
                table: "player_skin_unlocks",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_stats_updated_day",
                table: "player_stats",
                column: "updated_day");

            migrationBuilder.CreateIndex(
                name: "ix_players_invite_code",
                table: "players",
                column: "invite_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_players_invited_by_id",
                table: "players",
                column: "invited_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_players_kind_status",
                table: "players",
                columns: new[] { "kind", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_players_pseudo_key",
                table: "players",
                column: "pseudo_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_players_tile_x_tile_y",
                table: "players",
                columns: new[] { "tile_x", "tile_y" });

            migrationBuilder.CreateIndex(
                name: "ix_season_scores_player_id",
                table: "season_scores",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_season_scores_season_id_points",
                table: "season_scores",
                columns: new[] { "season_id", "points" });

            migrationBuilder.CreateIndex(
                name: "ix_seasons_number",
                table: "seasons",
                column: "number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blocks");

            migrationBuilder.DropTable(
                name: "dev_clock");

            migrationBuilder.DropTable(
                name: "friendships");

            migrationBuilder.DropTable(
                name: "game_settings");

            migrationBuilder.DropTable(
                name: "hands");

            migrationBuilder.DropTable(
                name: "hold_pairs");

            migrationBuilder.DropTable(
                name: "life_debts");

            migrationBuilder.DropTable(
                name: "notification_outbox");

            migrationBuilder.DropTable(
                name: "player_days");

            migrationBuilder.DropTable(
                name: "player_falls");

            migrationBuilder.DropTable(
                name: "player_skin_unlocks");

            migrationBuilder.DropTable(
                name: "player_stats");

            migrationBuilder.DropTable(
                name: "season_scores");

            migrationBuilder.DropTable(
                name: "skin_tones");

            migrationBuilder.DropTable(
                name: "storms");

            migrationBuilder.DropTable(
                name: "game_days");

            migrationBuilder.DropTable(
                name: "skin_items");

            migrationBuilder.DropTable(
                name: "players");

            migrationBuilder.DropTable(
                name: "seasons");
        }
    }
}
