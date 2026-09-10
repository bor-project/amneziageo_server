using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class Subscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubscriptionId",
                table: "Clients",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Subscription",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Listen = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Domains = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Certificate = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CertificateKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    UpdateHours = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscription", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_SubscriptionId",
                table: "Clients",
                column: "SubscriptionId");

            migrationBuilder.Sql("update Clients set SubscriptionId = lower(hex(randomblob(8))) where SubscriptionId = ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Subscription");

            migrationBuilder.DropIndex(
                name: "IX_Clients_SubscriptionId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "SubscriptionId",
                table: "Clients");
        }
    }
}
