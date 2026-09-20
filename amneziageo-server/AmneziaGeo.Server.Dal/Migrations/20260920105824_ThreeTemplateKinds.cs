using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class ThreeTemplateKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TemplateId",
                table: "Proxy",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TemplateId",
                table: "Configs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InterfaceTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ListenPort = table.Column<int>(type: "INTEGER", nullable: false),
                    Subnet = table.Column<string>(type: "TEXT", nullable: false),
                    Dns = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedIps = table.Column<string>(type: "TEXT", nullable: false),
                    Mtu = table.Column<int>(type: "INTEGER", nullable: false),
                    Keepalive = table.Column<int>(type: "INTEGER", nullable: false),
                    OfflineAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    Blocked = table.Column<string>(type: "TEXT", nullable: false),
                    ClientTemplateId = table.Column<long>(type: "INTEGER", nullable: true),
                    Jc = table.Column<int>(type: "INTEGER", nullable: false),
                    Jmin = table.Column<int>(type: "INTEGER", nullable: false),
                    Jmax = table.Column<int>(type: "INTEGER", nullable: false),
                    S1 = table.Column<int>(type: "INTEGER", nullable: false),
                    S2 = table.Column<int>(type: "INTEGER", nullable: false),
                    S3 = table.Column<int>(type: "INTEGER", nullable: false),
                    S4 = table.Column<int>(type: "INTEGER", nullable: false),
                    H1 = table.Column<string>(type: "TEXT", nullable: false),
                    H2 = table.Column<string>(type: "TEXT", nullable: false),
                    H3 = table.Column<string>(type: "TEXT", nullable: false),
                    H4 = table.Column<string>(type: "TEXT", nullable: false),
                    I1 = table.Column<string>(type: "TEXT", nullable: true),
                    I2 = table.Column<string>(type: "TEXT", nullable: true),
                    I3 = table.Column<string>(type: "TEXT", nullable: true),
                    I4 = table.Column<string>(type: "TEXT", nullable: true),
                    I5 = table.Column<string>(type: "TEXT", nullable: true),
                    HeaderProtectionKey = table.Column<string>(type: "TEXT", nullable: false),
                    ContentPaddingAddition = table.Column<string>(type: "TEXT", nullable: false),
                    RekeyAfterTime = table.Column<string>(type: "TEXT", nullable: false),
                    RekeyTimeout = table.Column<string>(type: "TEXT", nullable: false),
                    RejectAfterTime = table.Column<string>(type: "TEXT", nullable: false),
                    KeepaliveTimeout = table.Column<string>(type: "TEXT", nullable: false),
                    MaxHandshakeAttempts = table.Column<string>(type: "TEXT", nullable: false),
                    RandomTrailers = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisableCookies = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterfaceTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProxyTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Opened = table.Column<bool>(type: "INTEGER", nullable: false),
                    MakePath = table.Column<bool>(type: "INTEGER", nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Sources = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Proxy_TemplateId",
                table: "Proxy",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Configs_TemplateId",
                table: "Configs",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_InterfaceTemplates_ClientTemplateId",
                table: "InterfaceTemplates",
                column: "ClientTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_InterfaceTemplates_Name",
                table: "InterfaceTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProxyTemplates_Name",
                table: "ProxyTemplates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterfaceTemplates");

            migrationBuilder.DropTable(
                name: "ProxyTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Proxy_TemplateId",
                table: "Proxy");

            migrationBuilder.DropIndex(
                name: "IX_Configs_TemplateId",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "Proxy");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "Configs");
        }
    }
}
