using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class ProxyNameDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Panel SET ProxyNameTemplate = '{PROXY}-{HOST}-{TRANSPORT}' WHERE ProxyNameTemplate = '{PROXY}'");

            migrationBuilder.DropIndex(
                name: "IX_TransportProxies_Name",
                table: "TransportProxies");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "TransportProxies",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "ProxyNameTemplate",
                table: "Panel",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "{PROXY}-{HOST}-{TRANSPORT}",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 128,
                oldDefaultValue: "{PROXY}");

            migrationBuilder.CreateIndex(
                name: "IX_TransportProxies_Name_Transport",
                table: "TransportProxies",
                columns: new[] { "Name", "Transport" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Panel SET ProxyNameTemplate = '{PROXY}' WHERE ProxyNameTemplate = '{PROXY}-{HOST}-{TRANSPORT}'");

            migrationBuilder.DropIndex(
                name: "IX_TransportProxies_Name_Transport",
                table: "TransportProxies");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "TransportProxies",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldCollation: "NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "ProxyNameTemplate",
                table: "Panel",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "{PROXY}",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 128,
                oldDefaultValue: "{PROXY}-{HOST}-{TRANSPORT}");

            migrationBuilder.CreateIndex(
                name: "IX_TransportProxies_Name",
                table: "TransportProxies",
                column: "Name",
                unique: true);
        }
    }
}
