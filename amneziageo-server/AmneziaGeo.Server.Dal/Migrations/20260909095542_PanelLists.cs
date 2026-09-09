using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class PanelLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ListenIp",
                table: "Panel",
                newName: "Listen");

            migrationBuilder.RenameColumn(
                name: "ListenDomain",
                table: "Panel",
                newName: "Domains");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Listen",
                table: "Panel",
                newName: "ListenIp");

            migrationBuilder.RenameColumn(
                name: "Domains",
                table: "Panel",
                newName: "ListenDomain");
        }
    }
}
