using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JuDesk.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class HashSetupCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Codes were stored in clear before this migration; a fresh one is issued at the next start.
            migrationBuilder.Sql("DELETE FROM \"SetupCodes\";");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "SetupCodes");

            migrationBuilder.AddColumn<string>(
                name: "CodeHash",
                table: "SetupCodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FailedAttempts",
                table: "SetupCodes",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodeHash",
                table: "SetupCodes");

            migrationBuilder.DropColumn(
                name: "FailedAttempts",
                table: "SetupCodes");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "SetupCodes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");
        }
    }
}
