using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class HostKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HostKey",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HostKey",
                table: "AspNetUsers");
        }
    }
}
