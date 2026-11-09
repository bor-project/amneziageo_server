using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class ProxyTransportModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Hosted", table: "TransportProxies", type: "INTEGER", nullable: false, defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "HttpMethod",
                table: "TransportProxies",
                type: "TEXT",
                maxLength: 4,
                nullable: false,
                defaultValue: "GET");

            migrationBuilder.AddColumn<int>(
                name: "ProtocolVersion",
                table: "TransportProxies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "UseCertificate",
                table: "TransportProxies",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Hosted", table: "TransportProxies");

            migrationBuilder.DropColumn(
                name: "HttpMethod",
                table: "TransportProxies");

            migrationBuilder.DropColumn(
                name: "ProtocolVersion",
                table: "TransportProxies");

            migrationBuilder.DropColumn(
                name: "UseCertificate",
                table: "TransportProxies");
        }
    }
}
