using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class LegacyTransportProxies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LegacyConfigId",
                table: "TransportProxies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransportProxies_LegacyConfigId",
                table: "TransportProxies",
                column: "LegacyConfigId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransportProxies_LegacyConfigId",
                table: "TransportProxies");

            migrationBuilder.DropColumn(
                name: "LegacyConfigId",
                table: "TransportProxies");
        }
    }
}
