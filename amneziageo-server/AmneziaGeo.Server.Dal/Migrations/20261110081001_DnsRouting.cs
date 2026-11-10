using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class DnsRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_StandIns_Name", table: "StandIns");
            migrationBuilder.AddColumn<string>(name: "Scope", table: "StandIns", type: "TEXT", maxLength: 2048, nullable: false, defaultValue: "");
            migrationBuilder.CreateIndex(name: "IX_StandIns_Name_Scope", table: "StandIns", columns: new[] { "Name", "Scope" }, unique: true);

            migrationBuilder.AddColumn<string>(
                name: "Selection",
                table: "Resolver",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "global");

            migrationBuilder.AddColumn<string>(
                name: "Dns",
                table: "Balancers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Dns",
                table: "AddressGroups",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_StandIns_Name_Scope", table: "StandIns");
            migrationBuilder.DropColumn(name: "Scope", table: "StandIns");
            migrationBuilder.CreateIndex(name: "IX_StandIns_Name", table: "StandIns", column: "Name", unique: true);

            migrationBuilder.DropColumn(
                name: "Selection",
                table: "Resolver");

            migrationBuilder.DropColumn(
                name: "Dns",
                table: "Balancers");

            migrationBuilder.DropColumn(
                name: "Dns",
                table: "AddressGroups");
        }
    }
}
