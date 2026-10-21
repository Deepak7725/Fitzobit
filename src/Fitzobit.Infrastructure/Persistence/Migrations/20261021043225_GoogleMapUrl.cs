using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <summary>An optional Google Maps link per customer (owner's request, 2026-10-21): stored, shown and exported only.</summary>
    public partial class GoogleMapUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleMapUrl",
                table: "Customers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleMapUrl",
                table: "Customers");
        }
    }
}
