using GvrLicense.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GvrLicense.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LicenseDbContext))]
    [Migration("20260825153400_AddReleaseKindAndFileName")]
    public partial class AddReleaseKindAndFileName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "release",
                type: "text",
                nullable: false,
                defaultValue: "installer");

            migrationBuilder.AddColumn<string>(
                name: "file_name",
                table: "release",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "release");

            migrationBuilder.DropColumn(
                name: "file_name",
                table: "release");
        }
    }
}
