using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInterfaceTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterfaceTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Configs_TemplateId",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "Configs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                    AllowedIps = table.Column<string>(type: "TEXT", nullable: false),
                    Blocked = table.Column<string>(type: "TEXT", nullable: false),
                    ClientTemplateId = table.Column<long>(type: "INTEGER", nullable: true),
                    ContentPaddingAddition = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DisableCookies = table.Column<bool>(type: "INTEGER", nullable: false),
                    Dns = table.Column<string>(type: "TEXT", nullable: false),
                    H1 = table.Column<string>(type: "TEXT", nullable: false),
                    H2 = table.Column<string>(type: "TEXT", nullable: false),
                    H3 = table.Column<string>(type: "TEXT", nullable: false),
                    H4 = table.Column<string>(type: "TEXT", nullable: false),
                    HeaderProtectionKey = table.Column<string>(type: "TEXT", nullable: false),
                    I1 = table.Column<string>(type: "TEXT", nullable: true),
                    I2 = table.Column<string>(type: "TEXT", nullable: true),
                    I3 = table.Column<string>(type: "TEXT", nullable: true),
                    I4 = table.Column<string>(type: "TEXT", nullable: true),
                    I5 = table.Column<string>(type: "TEXT", nullable: true),
                    Jc = table.Column<int>(type: "INTEGER", nullable: false),
                    Jmax = table.Column<int>(type: "INTEGER", nullable: false),
                    Jmin = table.Column<int>(type: "INTEGER", nullable: false),
                    Keepalive = table.Column<int>(type: "INTEGER", nullable: false),
                    KeepaliveTimeout = table.Column<string>(type: "TEXT", nullable: false),
                    ListenPort = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxHandshakeAttempts = table.Column<string>(type: "TEXT", nullable: false),
                    Mtu = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OfflineAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    RandomTrailers = table.Column<bool>(type: "INTEGER", nullable: false),
                    RejectAfterTime = table.Column<string>(type: "TEXT", nullable: false),
                    RekeyAfterTime = table.Column<string>(type: "TEXT", nullable: false),
                    RekeyTimeout = table.Column<string>(type: "TEXT", nullable: false),
                    S1 = table.Column<int>(type: "INTEGER", nullable: false),
                    S2 = table.Column<int>(type: "INTEGER", nullable: false),
                    S3 = table.Column<int>(type: "INTEGER", nullable: false),
                    S4 = table.Column<int>(type: "INTEGER", nullable: false),
                    Subnet = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterfaceTemplates", x => x.Id);
                });

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
        }
    }
}
