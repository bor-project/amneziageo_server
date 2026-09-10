using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class ProxyKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Proxy_Port",
                table: "Proxy");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Proxy",
                type: "TEXT",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Target",
                table: "Proxy",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE Proxy SET Kind = 'ws' WHERE Kind = ''");

            migrationBuilder.CreateIndex(
                name: "IX_Proxy_Kind_Port",
                table: "Proxy",
                columns: new[] { "Kind", "Port" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Proxy_Kind_Port",
                table: "Proxy");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Proxy");

            migrationBuilder.DropColumn(
                name: "Target",
                table: "Proxy");

            migrationBuilder.CreateIndex(
                name: "IX_Proxy_Port",
                table: "Proxy",
                column: "Port",
                unique: true);
        }
    }
}
