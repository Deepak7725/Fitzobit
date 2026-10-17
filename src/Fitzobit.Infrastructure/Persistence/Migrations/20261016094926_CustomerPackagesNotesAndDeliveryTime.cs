using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Several packages per customer, dated customer notes, and a preferred delivery time. By the owner's decision (2026-10-16,
    /// "start fresh") the old single package per customer is NOT converted: its columns (preference, meal type, frequency, start/end
    /// date, paused count) and the old CustomerPauses table are dropped, and packages are entered again by hand. Customers, their
    /// locations, groups, history and audit are kept. Down restores the structure only (the deleted values cannot come back).
    /// </summary>
    public partial class CustomerPackagesNotesAndDeliveryTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_DictionaryEntries_FrequencyId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_DictionaryEntries_MealTypeId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_DictionaryEntries_PreferenceId",
                table: "Customers");

            migrationBuilder.DropTable(
                name: "CustomerPauses");

            migrationBuilder.DropIndex(
                name: "IX_Customers_EndDate",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_FrequencyId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_MealTypeId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_PreferenceId",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_Dates",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_PausedCount",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FrequencyId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MealTypeId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PausedCount",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PreferenceId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Customers");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "PreferredDeliveryTime",
                table: "Customers",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    PreferenceId = table.Column<int>(type: "integer", nullable: false),
                    MealTypeId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    FrequencyId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PausedCount = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPackages", x => x.Id);
                    table.CheckConstraint("CK_CustomerPackages_Dates", "\"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("CK_CustomerPackages_PausedCount", "\"PausedCount\" BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_CustomerPackages_Quantity", "\"Quantity\" BETWEEN 1 AND 20");
                    table.ForeignKey(
                        name: "FK_CustomerPackages_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerPackages_DictionaryEntries_FrequencyId",
                        column: x => x.FrequencyId,
                        principalTable: "DictionaryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerPackages_DictionaryEntries_MealTypeId",
                        column: x => x.MealTypeId,
                        principalTable: "DictionaryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerPackages_DictionaryEntries_PreferenceId",
                        column: x => x.PreferenceId,
                        principalTable: "DictionaryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackagePauses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    PausedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NewEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagePauses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackagePauses_CustomerPackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "CustomerPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotes_CustomerId_CreatedAt",
                table: "CustomerNotes",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_CustomerId_StartDate",
                table: "CustomerPackages",
                columns: new[] { "CustomerId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_EndDate",
                table: "CustomerPackages",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_FrequencyId",
                table: "CustomerPackages",
                column: "FrequencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_MealTypeId",
                table: "CustomerPackages",
                column: "MealTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_PreferenceId",
                table: "CustomerPackages",
                column: "PreferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagePauses_PackageId_PausedDate",
                table: "PackagePauses",
                columns: new[] { "PackageId", "PausedDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackagePauses_PausedDate",
                table: "PackagePauses",
                column: "PausedDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerNotes");

            migrationBuilder.DropTable(
                name: "PackagePauses");

            migrationBuilder.DropTable(
                name: "CustomerPackages");

            migrationBuilder.DropColumn(
                name: "PreferredDeliveryTime",
                table: "Customers");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "Customers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FrequencyId",
                table: "Customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MealTypeId",
                table: "Customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PausedCount",
                table: "Customers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreferenceId",
                table: "Customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "Customers",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerPauses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NewEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PausedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPauses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerPauses_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_EndDate",
                table: "Customers",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_FrequencyId",
                table: "Customers",
                column: "FrequencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_MealTypeId",
                table: "Customers",
                column: "MealTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PreferenceId",
                table: "Customers",
                column: "PreferenceId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_Dates",
                table: "Customers",
                sql: "\"StartDate\" IS NULL OR \"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_PausedCount",
                table: "Customers",
                sql: "\"PausedCount\" BETWEEN 0 AND 3");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPauses_CustomerId_PausedDate",
                table: "CustomerPauses",
                columns: new[] { "CustomerId", "PausedDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPauses_PausedDate",
                table: "CustomerPauses",
                column: "PausedDate");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_DictionaryEntries_FrequencyId",
                table: "Customers",
                column: "FrequencyId",
                principalTable: "DictionaryEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_DictionaryEntries_MealTypeId",
                table: "Customers",
                column: "MealTypeId",
                principalTable: "DictionaryEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_DictionaryEntries_PreferenceId",
                table: "Customers",
                column: "PreferenceId",
                principalTable: "DictionaryEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
