using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TempooAnalytics.Api.Data.Migrations
{
    /// <summary>
    /// The core's consolidated initial migration: creates every table of <c>AnalyticsDbContext</c> in one step, generated
    /// from the core model alone. Fresh installs (self-host, community) run this. Databases created by the earlier core
    /// chain (ten migrations, last one <c>20261023234938_AddInAppNotifications</c>, kept as history in the cloud
    /// composition) already have these tables: <see cref="CoreDatabaseMigrator"/> records this migration as applied
    /// without running it. Do not edit it by hand; later schema changes go in new migrations.
    /// </summary>
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Not generated, two details of the schema the old chain left in production that the model does not carry:
            // the (empty) analytics_aggregates schema, which the old chain created for tables it dropped later, and the
            // DEFAULT false of analytics_sites.is_deleted (set below).
            migrationBuilder.EnsureSchema(
                name: "analytics_aggregates");

            migrationBuilder.EnsureSchema(
                name: "jobs");

            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "admin_write_key_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    site_key = table.Column<string>(type: "text", nullable: false),
                    admin_user = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_write_key_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analytics_funnels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_funnels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analytics_sites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    site_key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    write_key = table.Column<string>(type: "text", nullable: true),
                    allowed_origins = table.Column<string[]>(type: "text[]", nullable: true),
                    public_share_token = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_sites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analytics_user_sites",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_user_sites", x => new { x.user_id, x.site_id });
                });

            migrationBuilder.CreateTable(
                name: "analytics_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_admin = table.Column<bool>(type: "boolean", nullable: false),
                    activation_token = table.Column<string>(type: "text", nullable: true),
                    activation_expires = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "export_jobs",
                schema: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    date_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    download_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analytics_feedbacks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_feedbacks", x => x.id);
                    table.ForeignKey(
                        name: "fk_analytics_feedbacks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "analytics_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_jobs",
                schema: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    external_property_id = table.Column<string>(type: "text", nullable: true),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rows_processed = table.Column<long>(type: "bigint", nullable: false),
                    total_rows_expected = table.Column<long>(type: "bigint", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    configuration_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_jobs_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "analytics_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    severity = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_analytics_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "analytics_sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "analytics_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_analytics_feedbacks_user_id",
                table: "analytics_feedbacks",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_analytics_users_email",
                table: "analytics_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_analytics_users_username",
                table: "analytics_users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_created_by_user_id",
                schema: "jobs",
                table: "import_jobs",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_created_at",
                schema: "notifications",
                table: "notifications",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_site_id",
                schema: "notifications",
                table: "notifications",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_created_at_id",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "user_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_type_source_id",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "user_id", "type", "source_id" },
                unique: true,
                filter: "source_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_unread",
                schema: "notifications",
                table: "notifications",
                column: "user_id",
                filter: "read_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_write_key_audit");

            migrationBuilder.DropTable(
                name: "analytics_feedbacks");

            migrationBuilder.DropTable(
                name: "analytics_funnels");

            migrationBuilder.DropTable(
                name: "analytics_user_sites");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "export_jobs",
                schema: "jobs");

            migrationBuilder.DropTable(
                name: "import_jobs",
                schema: "jobs");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "analytics_sites");

            migrationBuilder.DropTable(
                name: "analytics_users");
        }
    }
}
