using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations
{
    /// <inheritdoc />
    public partial class Obfuscation31 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "H4",
                table: "Outbounds",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "H3",
                table: "Outbounds",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "H2",
                table: "Outbounds",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "H1",
                table: "Outbounds",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "ContentPaddingAddition",
                table: "Outbounds",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HeaderProtectionKey",
                table: "Outbounds",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "KeepaliveTimeout",
                table: "Outbounds",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MaxHandshakeAttempts",
                table: "Outbounds",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RejectAfterTime",
                table: "Outbounds",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RekeyAfterTime",
                table: "Outbounds",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RekeyTimeout",
                table: "Outbounds",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "H4",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "H3",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "H2",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "H1",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "ContentPaddingAddition",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HeaderProtectionKey",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "KeepaliveTimeout",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MaxHandshakeAttempts",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RejectAfterTime",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RekeyAfterTime",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RekeyTimeout",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentPaddingAddition",
                table: "Outbounds");

            migrationBuilder.DropColumn(
                name: "HeaderProtectionKey",
                table: "Outbounds");

            migrationBuilder.DropColumn(
                name: "KeepaliveTimeout",
                table: "Outbounds");

            migrationBuilder.DropColumn(
                name: "MaxHandshakeAttempts",
                table: "Outbounds");

            migrationBuilder.DropColumn(
                name: "RejectAfterTime",
                table: "Outbounds");

            migrationBuilder.DropColumn(
                name: "RekeyAfterTime",
                table: "Outbounds");

            migrationBuilder.DropColumn(
                name: "RekeyTimeout",
                table: "Outbounds");

            migrationBuilder.DropColumn(
                name: "ContentPaddingAddition",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "HeaderProtectionKey",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "KeepaliveTimeout",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "MaxHandshakeAttempts",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "RejectAfterTime",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "RekeyAfterTime",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "RekeyTimeout",
                table: "Configs");

            migrationBuilder.AlterColumn<long>(
                name: "H4",
                table: "Outbounds",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "H3",
                table: "Outbounds",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "H2",
                table: "Outbounds",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "H1",
                table: "Outbounds",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "H4",
                table: "Configs",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "H3",
                table: "Configs",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "H2",
                table: "Configs",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "H1",
                table: "Configs",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }
    }
}
