using GvrLicense.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GvrLicense.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LicenseDbContext))]
    [Migration("20260827180000_AddDeviceLastIpAndSeenCount")]
    public partial class AddDeviceLastIpAndSeenCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_ip",
                table: "device",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "seen_count",
                table: "device",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_ip",
                table: "device");

            migrationBuilder.DropColumn(
                name: "seen_count",
                table: "device");
        }
    }
}
