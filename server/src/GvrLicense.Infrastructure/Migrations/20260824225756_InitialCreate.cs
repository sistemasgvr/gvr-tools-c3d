using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GvrLicense.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    support_email = table.Column<string>(type: "text", nullable: false),
                    terms_of_service_url = table.Column<string>(type: "text", nullable: false),
                    privacy_policy_url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    license_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    details_json = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_name = table.Column<string>(type: "text", nullable: false),
                    contact_name = table.Column<string>(type: "text", nullable: false),
                    contact_email = table.Column<string>(type: "text", nullable: false),
                    payment_notes = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    features = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "release",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    checksum = table.Column<string>(type: "text", nullable: false),
                    artifact_location = table.Column<string>(type: "text", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    signature_base64 = table.Column<string>(type: "text", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usage_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    license_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_code = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "license",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_devices = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license", x => x.id);
                    table.ForeignKey(
                        name: "FK_license_customer_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_license_plan_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    license_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    activated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device", x => x.id);
                    table.ForeignKey(
                        name: "FK_device_license_license_id",
                        column: x => x.license_id,
                        principalTable: "license",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usage_counter",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    license_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_code = table.Column<string>(type: "text", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    quota_limit = table.Column<int>(type: "integer", nullable: false),
                    consumed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_counter", x => x.id);
                    table.ForeignKey(
                        name: "FK_usage_counter_license_license_id",
                        column: x => x.license_id,
                        principalTable: "license",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_license_id_fingerprint",
                table: "device",
                columns: new[] { "license_id", "fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_customer_id",
                table: "license",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_license_key",
                table: "license",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_plan_id",
                table: "license",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_code",
                table: "plan",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usage_counter_license_id_feature_code_period",
                table: "usage_counter",
                columns: new[] { "license_id", "feature_code", "period" },
                unique: true);

            // Fuente de verdad de este SQL: Sql/ConsumeQuota.sql y Sql/AuditLogTrigger.sql (ver
            // Sql/README.md). Se pega literal aquí -- no se lee de disco -- para que la migración
            // no dependa de la carpeta de trabajo en tiempo de aplicar (docs/LICENSING_PLAN.md,
            // "Dónde vive la lógica: app (EF Core) vs Postgres (función/trigger)").
            migrationBuilder.Sql("""
                create or replace function consume_quota(
                    p_license_id uuid,
                    p_feature text,
                    p_amount int
                ) returns int as $$
                    update usage_counter
                    set consumed = consumed + p_amount
                    where license_id = p_license_id
                      and feature_code = p_feature
                      and period = date_trunc('month', now() at time zone 'utc')::date
                      and (quota_limit = -1 or consumed + p_amount <= quota_limit)
                    returning case when quota_limit = -1 then -1 else quota_limit - consumed end;
                $$ language sql;
                """);

            migrationBuilder.Sql("""
                create or replace function audit_license_status_change() returns trigger as $$
                begin
                    if new.status is distinct from old.status then
                        insert into audit_log (id, license_id, actor, action, details_json, occurred_at_utc)
                        values (
                            gen_random_uuid(),
                            new.id,
                            coalesce(current_setting('gvr.actor', true), 'system'),
                            'license_status_changed',
                            jsonb_build_object('from', old.status, 'to', new.status),
                            now()
                        );
                    end if;
                    return new;
                end;
                $$ language plpgsql;

                drop trigger if exists trg_audit_license_status on license;

                create trigger trg_audit_license_status
                    after update on license
                    for each row
                    execute function audit_license_status_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop trigger if exists trg_audit_license_status on license;");
            migrationBuilder.Sql("drop function if exists audit_license_status_change();");
            migrationBuilder.Sql("drop function if exists consume_quota(uuid, text, int);");

            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "device");

            migrationBuilder.DropTable(
                name: "release");

            migrationBuilder.DropTable(
                name: "usage_counter");

            migrationBuilder.DropTable(
                name: "usage_event");

            migrationBuilder.DropTable(
                name: "license");

            migrationBuilder.DropTable(
                name: "customer");

            migrationBuilder.DropTable(
                name: "plan");
        }
    }
}
