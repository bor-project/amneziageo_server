using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class DropDnsSwitches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockDoh",
                table: "Resolver");

            migrationBuilder.DropColumn(
                name: "BlockDot",
                table: "Resolver");

            migrationBuilder.DropColumn(
                name: "Intercept",
                table: "Resolver");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BlockDoh",
                table: "Resolver",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "BlockDot",
                table: "Resolver",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Intercept",
                table: "Resolver",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }
    }
}
