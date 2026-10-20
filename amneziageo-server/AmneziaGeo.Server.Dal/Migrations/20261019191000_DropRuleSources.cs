using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class DropRuleSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS RuleSourcesLeft AS SELECT Id, Sources FROM Rules WHERE Sources <> '';");

            migrationBuilder.DropColumn(
                name: "Sources",
                table: "Rules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Sources",
                table: "Rules",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("DROP TABLE IF EXISTS RuleSourcesLeft;");
        }
    }
}
