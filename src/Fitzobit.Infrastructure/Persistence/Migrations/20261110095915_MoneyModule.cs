using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Money page (owner and Siva, 2026-11-10): the price list (PackagePrices), the price each package was sold at (CustomerPackages.Price),
    /// costs per box (delivery + petrol ₹30, helper ₹25) and target profits (Monthly 30 %, Weekly 40 %, Trail 40 %) in AppSettings.
    /// Starting prices come from the live Fitzobit WhatsApp Business catalogue (read 2026-11-10): the same price for every preference
    /// (owner: Egg costs the same as Veg); Regular/Medium/Large Fit Box, Oats (= Oats &amp; Muesli) by code, and the owner's own meal types
    /// "Oats with whey protein" (= Protein Oats Bowl) and "Detox drink" by name; no Trail prices. A database without one of these simply
    /// gets no price for it. Every existing package
    /// without a price gets list price × quantity. Settings that already exist are kept.
    /// </summary>
    public partial class MoneyModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "CustomerPackages",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PackagePrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MealTypeId = table.Column<int>(type: "integer", nullable: false),
                    PreferenceId = table.Column<int>(type: "integer", nullable: false),
                    FrequencyId = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagePrices", x => x.Id);
                    table.CheckConstraint("CK_PackagePrices_Price", "\"Price\" > 0");
                    table.ForeignKey(
                        name: "FK_PackagePrices_DictionaryEntries_FrequencyId",
                        column: x => x.FrequencyId,
                        principalTable: "DictionaryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackagePrices_DictionaryEntries_MealTypeId",
                        column: x => x.MealTypeId,
                        principalTable: "DictionaryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackagePrices_DictionaryEntries_PreferenceId",
                        column: x => x.PreferenceId,
                        principalTable: "DictionaryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerPackages_Price",
                table: "CustomerPackages",
                sql: "\"Price\" IS NULL OR \"Price\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_PackagePrices_FrequencyId",
                table: "PackagePrices",
                column: "FrequencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagePrices_MealTypeId_PreferenceId_FrequencyId",
                table: "PackagePrices",
                columns: new[] { "MealTypeId", "PreferenceId", "FrequencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackagePrices_PreferenceId",
                table: "PackagePrices",
                column: "PreferenceId");

            migrationBuilder.Sql("""
                INSERT INTO "PackagePrices" ("MealTypeId", "PreferenceId", "FrequencyId", "Price", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy")
                SELECT m."Id", p."Id", f."Id", CASE f."Code" WHEN 'WEEKLY' THEN v.weekly ELSE v.monthly END, now(), now(), 'system', 'system'
                FROM (VALUES
                        ('code', 'REGULAR', 899, 2999), ('code', 'MEDIUM', 999, 3499), ('code', 'LARGE', 1149, 3999),
                        ('code', 'OATS', 999, 3499),
                        ('name', '%whey%', 1199, 3849), ('name', '%detox%', 333, 1049)
                     ) AS v(kind, meal, weekly, monthly)
                JOIN "DictionaryEntries" m ON m."DictionaryType" = 'MealType'
                     AND ((v.kind = 'code' AND m."Code" = v.meal) OR (v.kind = 'name' AND m."Code" <> 'OATS' AND m."DisplayName" ILIKE v.meal))
                CROSS JOIN "DictionaryEntries" p
                JOIN "DictionaryEntries" f ON f."DictionaryType" = 'Frequency' AND f."Code" IN ('WEEKLY', 'MONTHLY')
                WHERE p."DictionaryType" = 'Preference'
                ON CONFLICT DO NOTHING;

                INSERT INTO "AppSettings" ("Key", "Value", "CreatedAt", "UpdatedAt", "UpdatedBy")
                VALUES ('Money.DeliveryCostPerBox', '30', now(), now(), 'system'), ('Money.HelperCostPerBox', '25', now(), now(), 'system'),
                       ('Money.ProfitTarget.MONTHLY', '30', now(), now(), 'system'), ('Money.ProfitTarget.WEEKLY', '40', now(), now(), 'system'),
                       ('Money.ProfitTarget.TRAIL', '40', now(), now(), 'system')
                ON CONFLICT ("Key") DO NOTHING;

                UPDATE "CustomerPackages" c SET "Price" = pp."Price" * c."Quantity"
                FROM "PackagePrices" pp
                WHERE c."Price" IS NULL AND pp."MealTypeId" = c."MealTypeId" AND pp."PreferenceId" = c."PreferenceId" AND pp."FrequencyId" = c."FrequencyId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM "AppSettings" WHERE "Key" LIKE 'Money.%';""");

            migrationBuilder.DropTable(
                name: "PackagePrices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerPackages_Price",
                table: "CustomerPackages");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "CustomerPackages");
        }
    }
}
