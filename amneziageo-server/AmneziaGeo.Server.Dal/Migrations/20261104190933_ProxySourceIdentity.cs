using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class ProxySourceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentityKey",
                table: "ProxySources",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "IdentityVersion",
                table: "ProxySources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdentityKey",
                table: "ProxySources");

            migrationBuilder.DropColumn(
                name: "IdentityVersion",
                table: "ProxySources");
        }
    }
}
