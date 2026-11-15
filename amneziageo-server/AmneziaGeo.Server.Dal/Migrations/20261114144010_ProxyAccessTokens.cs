using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class ProxyAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AccessVersion",
                table: "TransportProxies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "AllowAnonymous",
                table: "TransportProxies",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TokensJson",
                table: "TransportProxies",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessVersion",
                table: "TransportProxies");

            migrationBuilder.DropColumn(
                name: "AllowAnonymous",
                table: "TransportProxies");

            migrationBuilder.DropColumn(
                name: "TokensJson",
                table: "TransportProxies");
        }
    }
}
