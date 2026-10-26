using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <summary>Renewal reminders (owner's request, 2026-10-26): who last sent a WhatsApp renewal reminder for a package, and when.</summary>
    public partial class RenewalReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RenewalReminderAt",
                table: "CustomerPackages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenewalReminderBy",
                table: "CustomerPackages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RenewalReminderAt",
                table: "CustomerPackages");

            migrationBuilder.DropColumn(
                name: "RenewalReminderBy",
                table: "CustomerPackages");
        }
    }
}
