using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class Outbounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Outbounds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    PrivateKey = table.Column<string>(type: "TEXT", nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", nullable: false),
                    PeerKey = table.Column<string>(type: "TEXT", nullable: false),
                    PresharedKey = table.Column<string>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: false),
                    Dns = table.Column<string>(type: "TEXT", nullable: false),
                    Mtu = table.Column<int>(type: "INTEGER", nullable: false),
                    Keepalive = table.Column<int>(type: "INTEGER", nullable: false),
                    Mark = table.Column<long>(type: "INTEGER", nullable: false),
                    Table = table.Column<int>(type: "INTEGER", nullable: false),
                    Jc = table.Column<int>(type: "INTEGER", nullable: false),
                    Jmin = table.Column<int>(type: "INTEGER", nullable: false),
                    Jmax = table.Column<int>(type: "INTEGER", nullable: false),
                    S1 = table.Column<int>(type: "INTEGER", nullable: false),
                    S2 = table.Column<int>(type: "INTEGER", nullable: false),
                    S3 = table.Column<int>(type: "INTEGER", nullable: false),
                    S4 = table.Column<int>(type: "INTEGER", nullable: false),
                    H1 = table.Column<long>(type: "INTEGER", nullable: false),
                    H2 = table.Column<long>(type: "INTEGER", nullable: false),
                    H3 = table.Column<long>(type: "INTEGER", nullable: false),
                    H4 = table.Column<long>(type: "INTEGER", nullable: false),
                    I1 = table.Column<string>(type: "TEXT", nullable: true),
                    I2 = table.Column<string>(type: "TEXT", nullable: true),
                    I3 = table.Column<string>(type: "TEXT", nullable: true),
                    I4 = table.Column<string>(type: "TEXT", nullable: true),
                    I5 = table.Column<string>(type: "TEXT", nullable: true),
                    RandomTrailers = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisableCookies = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbounds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Outbounds_Mark",
                table: "Outbounds",
                column: "Mark",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbounds_Name",
                table: "Outbounds",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbounds_Position",
                table: "Outbounds",
                column: "Position");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Outbounds");
        }
    }
}
