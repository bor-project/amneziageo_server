using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class RoutingPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Presets",
                table: "Templates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RoutingPresets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Proxy = table.Column<string>(type: "TEXT", nullable: false),
                    Direct = table.Column<string>(type: "TEXT", nullable: false),
                    Block = table.Column<string>(type: "TEXT", nullable: false),
                    AllUdp = table.Column<bool>(type: "INTEGER", nullable: false),
                    Full = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingPresets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingPresets_Name",
                table: "RoutingPresets",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoutingPresets");

            migrationBuilder.DropColumn(
                name: "Presets",
                table: "Templates");
        }
    }
}
