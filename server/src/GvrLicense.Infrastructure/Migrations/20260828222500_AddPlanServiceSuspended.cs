using GvrLicense.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GvrLicense.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LicenseDbContext))]
    [Migration("20260828222500_AddPlanServiceSuspended")]
    public partial class AddPlanServiceSuspended : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "service_suspended",
                table: "plan",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "service_suspended",
                table: "plan");
        }
    }
}
