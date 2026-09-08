using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GvrLicense.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyUsersAndLicenseOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "max_devices",
                table: "license",
                newName: "max_users");

            migrationBuilder.AddColumn<string>(
                name: "feature_overrides",
                table: "license",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "company_user_id",
                table: "device",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "company_user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_user", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_user_customer_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_company_user_id",
                table: "device",
                column: "company_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_user_customer_id_email",
                table: "company_user",
                columns: new[] { "customer_id", "email" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_device_company_user_company_user_id",
                table: "device",
                column: "company_user_id",
                principalTable: "company_user",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_device_company_user_company_user_id",
                table: "device");

            migrationBuilder.DropTable(
                name: "company_user");

            migrationBuilder.DropIndex(
                name: "IX_device_company_user_id",
                table: "device");

            migrationBuilder.DropColumn(
                name: "feature_overrides",
                table: "license");

            migrationBuilder.DropColumn(
                name: "company_user_id",
                table: "device");

            migrationBuilder.RenameColumn(
                name: "max_users",
                table: "license",
                newName: "max_devices");
        }
    }
}
