using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class PresetIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "RoutingPresets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Uid",
                table: "RoutingPresets",
                type: "TEXT",
                maxLength: 36,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE RoutingPresets SET Uid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || "
                + "substr(hex(randomblob(2)), 2) || '-' || substr('89ab', 1 + abs(random() % 4), 1) || "
                + "substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))) WHERE Uid = ''");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingPresets_Uid",
                table: "RoutingPresets",
                column: "Uid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoutingPresets_Uid",
                table: "RoutingPresets");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "RoutingPresets");

            migrationBuilder.DropColumn(
                name: "Uid",
                table: "RoutingPresets");
        }
    }
}
