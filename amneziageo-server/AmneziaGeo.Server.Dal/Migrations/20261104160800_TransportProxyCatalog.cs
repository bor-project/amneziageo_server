using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class TransportProxyCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProxyIds",
                table: "Templates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ProxySources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxySources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransportProxies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Transport = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Certificate = table.Column<string>(type: "TEXT", nullable: false),
                    CertificateKey = table.Column<string>(type: "TEXT", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", nullable: false),
                    Pin = table.Column<string>(type: "TEXT", nullable: false),
                    Secret = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportProxies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransportProxies_Name",
                table: "TransportProxies",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProxySources");

            migrationBuilder.DropTable(
                name: "TransportProxies");

            migrationBuilder.DropColumn(
                name: "ProxyIds",
                table: "Templates");
        }
    }
}
