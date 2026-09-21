using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class EndpointServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ServicesPort",
                table: "Configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "WebSocket",
                table: "Configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // A websocket proxy on the port of an endpoint turns the websocket of that endpoint on.
            migrationBuilder.Sql(
                "UPDATE Configs SET WebSocket = 1 WHERE ListenPort IN "
                + "(SELECT Port FROM Proxy WHERE IsEnabled = 1 AND Kind = 'ws' AND trim(Path, '/') <> '');");

            // A websocket proxy on a port of its own moves the services of the first endpoint left there.
            migrationBuilder.Sql(
                "UPDATE Configs SET WebSocket = 1, ServicesPort = (SELECT MIN(Port) FROM Proxy WHERE IsEnabled = 1 AND Kind = 'ws' "
                + "AND trim(Path, '/') <> '' AND Port NOT IN (SELECT ListenPort FROM Configs)) "
                + "WHERE Id = (SELECT MIN(Id) FROM Configs WHERE IsEnabled = 1 AND WebSocket = 0) "
                + "AND EXISTS (SELECT 1 FROM Proxy WHERE IsEnabled = 1 AND Kind = 'ws' AND trim(Path, '/') <> '' "
                + "AND Port NOT IN (SELECT ListenPort FROM Configs));");

            migrationBuilder.DropTable(
                name: "Proxy");

            migrationBuilder.DropTable(
                name: "ProxyTemplates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServicesPort",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "WebSocket",
                table: "Configs");

            migrationBuilder.CreateTable(
                name: "Proxy",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Certificate = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CertificateKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    Opened = table.Column<bool>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Sources = table.Column<string>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TemplateId = table.Column<long>(type: "INTEGER", nullable: true),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proxy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProxyTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    MakePath = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Opened = table.Column<bool>(type: "INTEGER", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Sources = table.Column<string>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Proxy_Kind_Port",
                table: "Proxy",
                columns: new[] { "Kind", "Port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Proxy_Name",
                table: "Proxy",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Proxy_TemplateId",
                table: "Proxy",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyTemplates_Name",
                table: "ProxyTemplates",
                column: "Name",
                unique: true);
        }
    }
}
