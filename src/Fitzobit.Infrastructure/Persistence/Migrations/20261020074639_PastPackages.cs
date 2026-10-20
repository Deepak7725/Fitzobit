using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Past packages: when the daily 12 pm run reaches a package's last day it sets ArchivedAt (owner's decision, 2026-10-20: keep finished
    /// packages as read-only history instead of deleting them). Nothing is changed here; the first run moves packages that already ended.
    /// </summary>
    public partial class PastPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "CustomerPackages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_ArchivedAt",
                table: "CustomerPackages",
                column: "ArchivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerPackages_ArchivedAt",
                table: "CustomerPackages");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "CustomerPackages");
        }
    }
}
