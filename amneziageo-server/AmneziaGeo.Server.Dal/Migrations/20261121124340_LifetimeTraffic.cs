using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class LifetimeTraffic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrafficResetRx",
                table: "Clients",
                type: "TEXT",
                nullable: false,
                defaultValue: "0");

            migrationBuilder.AddColumn<string>(
                name: "TrafficResetTx",
                table: "Clients",
                type: "TEXT",
                nullable: false,
                defaultValue: "0");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrafficResetUtc",
                table: "Clients",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrafficResetRx",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "TrafficResetTx",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "TrafficResetUtc",
                table: "Clients");
        }
    }
}
