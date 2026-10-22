using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class DnsFiles : Migration
    {
        private const string Mode = "CASE c.DnsMode WHEN 1 THEN 1 WHEN 2 THEN 4 ELSE 5 END";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilesDns",
                table: "Resolver",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FilesMode",
                table: "Resolver",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.Sql(
                "UPDATE Resolver SET FilesMode = COALESCE(("
                + "SELECT " + Mode + " FROM Configs c "
                + "ORDER BY (SELECT COUNT(*) FROM Clients k WHERE k.ConfigId = c.Id) DESC, c.Id LIMIT 1), 5);");
            migrationBuilder.Sql(
                "UPDATE Clients SET DnsMode = (SELECT " + Mode + " FROM Configs c WHERE c.Id = Clients.ConfigId) "
                + "WHERE DnsMode = 0 "
                + "AND EXISTS (SELECT 1 FROM Configs c WHERE c.Id = Clients.ConfigId) "
                + "AND (TemplateId IS NULL "
                + "OR NOT EXISTS (SELECT 1 FROM Templates t WHERE t.Id = Clients.TemplateId) "
                + "OR EXISTS (SELECT 1 FROM Templates t WHERE t.Id = Clients.TemplateId AND t.DnsMode = 0)) "
                + "AND (SELECT " + Mode + " FROM Configs c WHERE c.Id = Clients.ConfigId) "
                + "<> COALESCE((SELECT FilesMode FROM Resolver ORDER BY Id LIMIT 1), 5);");

            migrationBuilder.DropColumn(
                name: "DnsMode",
                table: "Configs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DnsMode",
                table: "Configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                "UPDATE Configs SET DnsMode = COALESCE(("
                + "SELECT CASE FilesMode WHEN 1 THEN 1 ELSE 0 END FROM Resolver ORDER BY Id LIMIT 1), 0);");

            migrationBuilder.DropColumn(
                name: "FilesDns",
                table: "Resolver");

            migrationBuilder.DropColumn(
                name: "FilesMode",
                table: "Resolver");
        }
    }
}
