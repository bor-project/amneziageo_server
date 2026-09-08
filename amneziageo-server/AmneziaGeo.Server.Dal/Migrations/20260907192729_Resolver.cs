using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class Resolver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Resolver",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Upstreams = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Listen = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NameMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CacheSize = table.Column<int>(type: "INTEGER", nullable: false),
                    MinTtl = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxTtl = table.Column<int>(type: "INTEGER", nullable: false),
                    Intercept = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlockDot = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlockDoh = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resolver", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Resolver");
        }
    }
}
