using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class ClientResolverHanded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ResolverHanded",
                table: "Clients",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE Clients SET ResolverHanded = 1 "
                + "WHERE EXISTS (SELECT 1 FROM Resolver WHERE IsEnabled = 1 AND Port = 53) "
                + "AND (TemplateId IS NULL OR TemplateId NOT IN (SELECT Id FROM Templates WHERE StandIns = 0 AND TRIM(Dns) <> ''));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResolverHanded",
                table: "Clients");
        }
    }
}
