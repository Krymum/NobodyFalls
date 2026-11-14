using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NobodyFalls.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Staff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "session_version",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_roles = table.Column<List<string>>(type: "text[]", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    prev_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staff_members",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    roles = table.Column<List<string>>(type: "text[]", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_members", x => x.player_id);
                    table.CheckConstraint("ck_staff_members_roles", "cardinality(roles) > 0 and roles <@ array['admin', 'moderator', 'support']::text[]");
                    table.ForeignKey(
                        name: "fk_staff_members_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "totp_credentials",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_step = table.Column<long>(type: "bigint", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_totp_credentials", x => x.player_id);
                    table.ForeignKey(
                        name: "fk_totp_credentials_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "totp_recovery_codes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salt = table.Column<byte[]>(type: "bytea", nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_totp_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_totp_recovery_codes_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_action",
                table: "audit_log",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor_id",
                table: "audit_log",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_at",
                table: "audit_log",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_target_type_target_id",
                table: "audit_log",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_totp_recovery_codes_player_id",
                table: "totp_recovery_codes",
                column: "player_id");

            // ADR 0020: the digest of an entry covers the previous entry's hash and every
            // field, encoded as a JSON array (unambiguous, independent of the time zone).
            migrationBuilder.Sql(
                """
                create function audit_digest(
                    prev bytea, at timestamptz, actor_id uuid, actor_roles text[], action text,
                    target_type text, target_id text, before jsonb, after jsonb,
                    reason text, ip text, request_id text)
                returns bytea language sql as $$
                    select sha256(convert_to(jsonb_build_array(
                        encode(prev, 'hex'), (extract(epoch from at) * 1000000)::bigint, actor_id, actor_roles,
                        action, target_type, target_id, before, after, reason, ip, request_id)::text, 'UTF8'))
                $$;

                create function audit_log_append_only() returns trigger language plpgsql as $$
                begin
                    raise exception 'audit_log is append-only' using errcode = 'insufficient_privilege';
                end
                $$;

                create trigger audit_log_no_update_or_delete before update or delete on audit_log
                    for each row execute function audit_log_append_only();
                create trigger audit_log_no_truncate before truncate on audit_log
                    for each statement execute function audit_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop trigger audit_log_no_truncate on audit_log;
                drop trigger audit_log_no_update_or_delete on audit_log;
                drop function audit_log_append_only();
                drop function audit_digest(bytea, timestamptz, uuid, text[], text, text, text, jsonb, jsonb, text, text, text);
                """);

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "staff_members");

            migrationBuilder.DropTable(
                name: "totp_credentials");

            migrationBuilder.DropTable(
                name: "totp_recovery_codes");

            migrationBuilder.DropColumn(
                name: "session_version",
                table: "players");
        }
    }
}
