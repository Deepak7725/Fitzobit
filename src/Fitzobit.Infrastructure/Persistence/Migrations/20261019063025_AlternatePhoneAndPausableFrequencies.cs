using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// An optional alternative phone number per customer, and a "Can pause" switch per frequency. Only Monthly is switched on
    /// (owner's decision, 2026-10-19); Weekly, Trail and any other frequency start switched off.
    /// </summary>
    public partial class AlternatePhoneAndPausableFrequencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanPause",
                table: "DictionaryEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AlternatePhoneNumber",
                table: "Customers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.Sql("""UPDATE "DictionaryEntries" SET "CanPause" = TRUE WHERE "DictionaryType" = 'Frequency' AND "Code" = 'MONTHLY';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanPause",
                table: "DictionaryEntries");

            migrationBuilder.DropColumn(
                name: "AlternatePhoneNumber",
                table: "Customers");
        }
    }
}
