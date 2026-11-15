using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class RoutingDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultKey",
                table: "RoutingPresets",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultKey",
                table: "AddressGroups",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutingPresets_DefaultKey",
                table: "RoutingPresets",
                column: "DefaultKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AddressGroups_DefaultKey",
                table: "AddressGroups",
                column: "DefaultKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoutingPresets_DefaultKey",
                table: "RoutingPresets");

            migrationBuilder.DropIndex(
                name: "IX_AddressGroups_DefaultKey",
                table: "AddressGroups");

            migrationBuilder.DropColumn(
                name: "DefaultKey",
                table: "RoutingPresets");

            migrationBuilder.DropColumn(
                name: "DefaultKey",
                table: "AddressGroups");
        }
    }
}
