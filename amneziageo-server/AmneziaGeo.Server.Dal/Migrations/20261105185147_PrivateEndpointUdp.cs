using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class PrivateEndpointUdp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UdpPrivate",
                table: "Configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UdpPrivate",
                table: "Configs");
        }
    }
}
