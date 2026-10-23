using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class PresetLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE RoutingPresets SET Proxy = replace(Proxy, ', ', char(10)), "
                + "Direct = replace(Direct, ', ', char(10)), Block = replace(Block, ', ', char(10));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE RoutingPresets SET Proxy = replace(Proxy, char(10), ', '), "
                + "Direct = replace(Direct, char(10), ', '), Block = replace(Block, char(10), ', ');");
        }
    }
}
