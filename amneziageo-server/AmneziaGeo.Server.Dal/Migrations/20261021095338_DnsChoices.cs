using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class DnsChoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DnsMode",
                table: "Templates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DnsMode",
                table: "Configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Dns",
                table: "Clients",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DnsMode",
                table: "Clients",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE Templates SET DnsMode = 2 WHERE TRIM(Dns) <> '';");
            migrationBuilder.Sql(
                "UPDATE Configs SET DnsMode = 1 "
                + "WHERE EXISTS (SELECT 1 FROM Resolver WHERE IsEnabled = 1 AND Port = 53) "
                + "AND NOT EXISTS (SELECT 1 FROM Templates WHERE StandIns = 1) "
                + "AND NOT EXISTS ("
                + "WITH RECURSIVE lists(rest) AS ("
                + "SELECT Targets FROM Rules WHERE IsEnabled = 1 "
                + "UNION ALL SELECT Direct FROM RouteBasics "
                + "UNION ALL SELECT Block FROM RouteBasics), "
                + "split(token, rest) AS ("
                + "SELECT '', LOWER(REPLACE(REPLACE(REPLACE(REPLACE(rest, CHAR(10), ','), CHAR(13), ','), CHAR(9), ','), ' ', ',')) || ',' FROM lists "
                + "UNION ALL SELECT SUBSTR(rest, 1, INSTR(rest, ',') - 1), SUBSTR(rest, INSTR(rest, ',') + 1) FROM split WHERE rest <> '') "
                + "SELECT 1 FROM split WHERE token LIKE 'geosite:%' OR token LIKE 'domain:%' OR token LIKE 'keyword:%' "
                + "OR (token <> '' AND INSTR(token, ':') = 0 AND token GLOB '*[a-z]*'));");
            migrationBuilder.Sql("UPDATE Resolver SET IsEnabled = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DnsMode",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "DnsMode",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "Dns",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "DnsMode",
                table: "Clients");
        }
    }
}
