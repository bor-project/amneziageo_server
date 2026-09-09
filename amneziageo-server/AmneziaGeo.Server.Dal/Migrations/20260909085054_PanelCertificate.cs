using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class PanelCertificate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Domain",
                table: "Panel");

            migrationBuilder.AddColumn<string>(
                name: "Certificate",
                table: "Panel",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CertificateKey",
                table: "Panel",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Certificate",
                table: "Panel");

            migrationBuilder.DropColumn(
                name: "CertificateKey",
                table: "Panel");

            migrationBuilder.AddColumn<string>(
                name: "Domain",
                table: "Panel",
                type: "TEXT",
                maxLength: 253,
                nullable: false,
                defaultValue: "");
        }
    }
}
