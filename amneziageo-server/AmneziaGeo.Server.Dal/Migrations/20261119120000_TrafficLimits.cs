using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class TrafficLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LimitsJson",
                table: "Templates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LimitsJson",
                table: "Clients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OverrideLimits",
                table: "Clients",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TrafficPeriods",
                columns: table => new
                {
                    ClientId = table.Column<long>(type: "INTEGER", nullable: false),
                    Period = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndsUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Rx = table.Column<long>(type: "INTEGER", nullable: false),
                    Tx = table.Column<long>(type: "INTEGER", nullable: false),
                    SeenRx = table.Column<long>(type: "INTEGER", nullable: false),
                    SeenTx = table.Column<long>(type: "INTEGER", nullable: false),
                    Generation = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ObservedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrafficPeriods", x => new { x.ClientId, x.Period });
                    table.ForeignKey(
                        name: "FK_TrafficPeriods_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TrafficPeriods");
            migrationBuilder.DropColumn(name: "LimitsJson", table: "Templates");
            migrationBuilder.DropColumn(name: "LimitsJson", table: "Clients");
            migrationBuilder.DropColumn(name: "OverrideLimits", table: "Clients");
        }
    }
}
