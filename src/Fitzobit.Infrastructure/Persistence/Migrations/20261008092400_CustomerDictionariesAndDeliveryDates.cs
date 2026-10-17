using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// New business model: customer drop-downs from DictionaryEntries, package dates in delivery days, pauses, generated
    /// CustomerIds. Written by hand so that no customer data is lost:
    /// <list type="bullet">
    /// <item>Location/NormalizedLocation are renamed (not dropped) to Address/NormalizedAddress; CustomerIds are unchanged.</item>
    /// <item>Each customer's old plan is kept as readable text (LegacyPlan) and, where it is unambiguous, mapped to the new
    /// Preference / Meal type / Frequency values. Status Active/Inactive becomes the matching status entry.</item>
    /// <item>The retired Import, Catalogue and Plan tables, plus every customer's original plan/tier/import ids
    /// (legacy."CustomerPlans"), are moved into the "legacy" schema: preserved, but no longer used by the application.</item>
    /// </list>
    /// </summary>
    public partial class CustomerDictionariesAndDeliveryDates : Migration
    {
        private static readonly string[] RetiredTables =
        [
            "ImportPlanMappings", "ImportRows", "ImportDraftGroups", "ImportBatches", "PlanAliases", "Plans", "CataloguePricingTiers", "CatalogueItems"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- 1. Drop-down values ----------
            migrationBuilder.CreateTable(
                name: "DictionaryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DictionaryType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    NumericValue = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DictionaryEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DictionaryEntries_DictionaryType_Code",
                table: "DictionaryEntries",
                columns: new[] { "DictionaryType", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DictionaryEntries_DictionaryType_SortOrder",
                table: "DictionaryEntries",
                columns: new[] { "DictionaryType", "SortOrder" });

            // Codes follow Normalization.ToCode(DisplayName). Frequency NumericValue = delivery days (Monday–Saturday).
            migrationBuilder.Sql("""
                INSERT INTO "DictionaryEntries" ("DictionaryType", "Code", "DisplayName", "SortOrder", "IsActive", "IsSystem", "NumericValue", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy")
                VALUES
                    ('Preference',     'VEG',       'Veg',       10, TRUE, FALSE, NULL, now(), now(), 'system', 'system'),
                    ('Preference',     'WITH-EGG',  'With Egg',  20, TRUE, FALSE, NULL, now(), now(), 'system', 'system'),
                    ('Preference',     'NONE',      'None',      30, TRUE, FALSE, NULL, now(), now(), 'system', 'system'),
                    ('MealType',       'REGULAR',   'Regular',   10, TRUE, FALSE, NULL, now(), now(), 'system', 'system'),
                    ('MealType',       'MEDIUM',    'Medium',    20, TRUE, FALSE, NULL, now(), now(), 'system', 'system'),
                    ('MealType',       'LARGE',     'Large',     30, TRUE, FALSE, NULL, now(), now(), 'system', 'system'),
                    ('MealType',       'MEAL-BOWL', 'Meal bowl', 40, TRUE, FALSE, NULL, now(), now(), 'system', 'system'),
                    ('MealType',       'OATS',      'Oats',      50, TRUE, FALSE, NULL, now(), now(), 'system', 'system'),
                    ('Frequency',      'MONTHLY',   'Monthly',   10, TRUE, FALSE, 26,   now(), now(), 'system', 'system'),
                    ('Frequency',      'WEEKLY',    'Weekly',    20, TRUE, FALSE, 6,    now(), now(), 'system', 'system'),
                    ('CustomerStatus', 'ACTIVE',    'Active',    10, TRUE, TRUE,  NULL, now(), now(), 'system', 'system'),
                    ('CustomerStatus', 'INACTIVE',  'Inactive',  20, TRUE, TRUE,  NULL, now(), now(), 'system', 'system'),
                    ('CustomerStatus', 'PAUSED',    'Paused',    30, TRUE, TRUE,  NULL, now(), now(), 'system', 'system')
                ON CONFLICT ("DictionaryType", "Code") DO NOTHING;
                """);

            // ---------- 2. New customer columns (existing values are renamed, never dropped) ----------
            migrationBuilder.RenameColumn(name: "Location", table: "Customers", newName: "Address");
            migrationBuilder.RenameColumn(name: "NormalizedLocation", table: "Customers", newName: "NormalizedAddress");

            migrationBuilder.AddColumn<int>(name: "PreferenceId", table: "Customers", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "MealTypeId", table: "Customers", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "FrequencyId", table: "Customers", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "StatusId", table: "Customers", type: "integer", nullable: true);
            migrationBuilder.AddColumn<DateOnly>(name: "StartDate", table: "Customers", type: "date", nullable: true);
            migrationBuilder.AddColumn<DateOnly>(name: "EndDate", table: "Customers", type: "date", nullable: true);
            migrationBuilder.AddColumn<int>(name: "PausedCount", table: "Customers", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<decimal>(name: "Latitude", table: "Customers", type: "numeric(9,6)", precision: 9, scale: 6, nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "Longitude", table: "Customers", type: "numeric(9,6)", precision: 9, scale: 6, nullable: true);
            migrationBuilder.AddColumn<string>(name: "LegacyPlan", table: "Customers", type: "character varying(500)", maxLength: 500, nullable: true);

            // ---------- 3. Carry existing customers over ----------
            migrationBuilder.Sql("""
                CREATE SCHEMA IF NOT EXISTS legacy;

                -- Every customer's original plan, tier, import batch, plan text and status, exactly as stored.
                CREATE TABLE legacy."CustomerPlans" AS
                SELECT "Id" AS "CustomerId", "CustomerId" AS "BusinessCustomerId", "PlanId", "PricingTierId", "SourceImportBatchId", "OriginalPlan", "Status"
                FROM "Customers";
                ALTER TABLE legacy."CustomerPlans" ADD PRIMARY KEY ("CustomerId");

                -- Readable old plan, e.g. "Medium Fit Box Subscription (5 Items) - monthly · Veg Plan · from file: Medium Box Veg".
                UPDATE "Customers" c
                SET "LegacyPlan" = left(concat_ws(' · ',
                        (SELECT p."Name" FROM "Plans" p WHERE p."Id" = c."PlanId"),
                        (SELECT t."TierName" FROM "CataloguePricingTiers" t WHERE t."Id" = c."PricingTierId"),
                        CASE WHEN c."OriginalPlan" IS NOT NULL
                              AND c."OriginalPlan" IS DISTINCT FROM (SELECT p."Name" FROM "Plans" p WHERE p."Id" = c."PlanId")
                             THEN 'from file: ' || c."OriginalPlan" END), 500)
                WHERE c."PlanId" IS NOT NULL OR c."PricingTierId" IS NOT NULL OR c."OriginalPlan" IS NOT NULL;

                -- Status: Inactive stays Inactive, everything else becomes Active.
                UPDATE "Customers" c SET "StatusId" = d."Id"
                FROM "DictionaryEntries" d
                WHERE d."DictionaryType" = 'CustomerStatus'
                  AND d."Code" = CASE WHEN c."Status" = 'Inactive' THEN 'INACTIVE' ELSE 'ACTIVE' END;

                -- Preference from the pricing tier ("Veg Plan" / "Egg Plan"); other tiers are left for the user to complete.
                UPDATE "Customers" c SET "PreferenceId" = d."Id"
                FROM "CataloguePricingTiers" t, "DictionaryEntries" d
                WHERE t."Id" = c."PricingTierId" AND d."DictionaryType" = 'Preference'
                  AND d."Code" = CASE WHEN t."TierName" ILIKE '%egg%' THEN 'WITH-EGG' WHEN t."TierName" ILIKE '%veg%' THEN 'VEG' END;

                -- Meal type from the plan name (Regular / Medium / Large Fit Box, Oats & Muesli). Salads are not guessed.
                UPDATE "Customers" c SET "MealTypeId" = d."Id"
                FROM "Plans" p, "DictionaryEntries" d
                WHERE p."Id" = c."PlanId" AND d."DictionaryType" = 'MealType'
                  AND d."Code" = CASE
                        WHEN p."Name" ILIKE '%oats%' OR p."Name" ILIKE '%muesli%' THEN 'OATS'
                        WHEN p."Name" ILIKE '%large%' THEN 'LARGE'
                        WHEN p."Name" ILIKE '%medium%' THEN 'MEDIUM'
                        WHEN p."Name" ILIKE '%regular%' THEN 'REGULAR' END;

                -- Frequency from the tier's or catalogue item's billing period, else the plan name.
                WITH periods AS (
                    SELECT c."Id",
                           COALESCE(
                               NULLIF((SELECT t."BillingPeriod" FROM "CataloguePricingTiers" t WHERE t."Id" = c."PricingTierId"), 'Unspecified'),
                               NULLIF((SELECT i."BillingPeriod" FROM "Plans" p JOIN "CatalogueItems" i ON i."Id" = p."CatalogueItemId" WHERE p."Id" = c."PlanId"), 'Unspecified'),
                               (SELECT CASE WHEN p."Name" ILIKE '%monthly%' THEN 'Monthly'
                                            WHEN p."Name" ILIKE '%weekly%' OR p."Name" ILIKE '%6 days%' THEN 'Weekly' END
                                FROM "Plans" p WHERE p."Id" = c."PlanId")) AS "Period"
                    FROM "Customers" c
                    WHERE c."PlanId" IS NOT NULL)
                UPDATE "Customers" c SET "FrequencyId" = d."Id"
                FROM periods x JOIN "DictionaryEntries" d ON d."DictionaryType" = 'Frequency' AND d."Code" = upper(x."Period")
                WHERE x."Id" = c."Id";
                """);

            migrationBuilder.AlterColumn<int>(name: "StatusId", table: "Customers", type: "integer", nullable: false, oldClrType: typeof(int), oldType: "integer", oldNullable: true);

            // ---------- 4. Old plan columns (their values are in legacy."CustomerPlans") ----------
            migrationBuilder.DropForeignKey(name: "FK_Customers_CataloguePricingTiers_PricingTierId", table: "Customers");
            migrationBuilder.DropForeignKey(name: "FK_Customers_ImportBatches_SourceImportBatchId", table: "Customers");
            migrationBuilder.DropForeignKey(name: "FK_Customers_Plans_PlanId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_PlanId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_PricingTierId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_SourceImportBatchId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_Status", table: "Customers");
            migrationBuilder.DropColumn(name: "PlanId", table: "Customers");
            migrationBuilder.DropColumn(name: "PricingTierId", table: "Customers");
            migrationBuilder.DropColumn(name: "SourceImportBatchId", table: "Customers");
            migrationBuilder.DropColumn(name: "OriginalPlan", table: "Customers");
            migrationBuilder.DropColumn(name: "Status", table: "Customers");

            // ---------- 5. Retired tables move to the "legacy" schema (kept as they are, unused by the app) ----------
            migrationBuilder.DropForeignKey(name: "FK_ImportRows_DeliveryGroups_SuggestedDeliveryGroupId", table: "ImportRows");
            migrationBuilder.DropForeignKey(name: "FK_ImportRows_DeliveryGroups_TargetDeliveryGroupId", table: "ImportRows");
            migrationBuilder.DropForeignKey(name: "FK_ImportDraftGroups_DeliveryGroups_CreatedDeliveryGroupId", table: "ImportDraftGroups");
            foreach (var table in RetiredTables)
                migrationBuilder.Sql($"""ALTER TABLE "{table}" SET SCHEMA legacy;""");

            // ---------- 6. Pauses, customer numbers, keys and rules ----------
            migrationBuilder.CreateTable(
                name: "CustomerPauses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    PausedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NewEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
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
                name: "IX_CustomerPauses_CustomerId_PausedDate",
                table: "CustomerPauses",
                columns: new[] { "CustomerId", "PausedDate" },
                unique: true);

            migrationBuilder.CreateIndex(name: "IX_CustomerPauses_PausedDate", table: "CustomerPauses", column: "PausedDate");

            // New ids continue after any existing FITZ-nnnnnn id, so a generated id never collides with a kept one.
            migrationBuilder.CreateSequence(name: "CustomerNumberSequence");
            migrationBuilder.Sql("""
                SELECT setval('"CustomerNumberSequence"',
                              COALESCE((SELECT max(substring("CustomerId" FROM 6)::bigint) FROM "Customers" WHERE "CustomerId" ~ '^FITZ-[0-9]{1,15}$'), 0) + 1,
                              false);
                """);

            migrationBuilder.CreateIndex(name: "IX_Customers_PreferenceId", table: "Customers", column: "PreferenceId");
            migrationBuilder.CreateIndex(name: "IX_Customers_MealTypeId", table: "Customers", column: "MealTypeId");
            migrationBuilder.CreateIndex(name: "IX_Customers_FrequencyId", table: "Customers", column: "FrequencyId");
            migrationBuilder.CreateIndex(name: "IX_Customers_StatusId", table: "Customers", column: "StatusId");
            migrationBuilder.CreateIndex(name: "IX_Customers_EndDate", table: "Customers", column: "EndDate");

            migrationBuilder.AddForeignKey(name: "FK_Customers_DictionaryEntries_PreferenceId", table: "Customers", column: "PreferenceId",
                principalTable: "DictionaryEntries", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Customers_DictionaryEntries_MealTypeId", table: "Customers", column: "MealTypeId",
                principalTable: "DictionaryEntries", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Customers_DictionaryEntries_FrequencyId", table: "Customers", column: "FrequencyId",
                principalTable: "DictionaryEntries", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Customers_DictionaryEntries_StatusId", table: "Customers", column: "StatusId",
                principalTable: "DictionaryEntries", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddCheckConstraint(name: "CK_Customers_Dates", table: "Customers",
                sql: "\"StartDate\" IS NULL OR \"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
            migrationBuilder.AddCheckConstraint(name: "CK_Customers_Latitude", table: "Customers",
                sql: "\"Latitude\" IS NULL OR \"Latitude\" BETWEEN -90 AND 90");
            migrationBuilder.AddCheckConstraint(name: "CK_Customers_Longitude", table: "Customers",
                sql: "\"Longitude\" IS NULL OR \"Longitude\" BETWEEN -180 AND 180");
            migrationBuilder.AddCheckConstraint(name: "CK_Customers_PausedCount", table: "Customers",
                sql: "\"PausedCount\" BETWEEN 0 AND 3");
        }

        /// <inheritdoc />
        /// <remarks>Restores the previous schema and data (old plan links come back from legacy."CustomerPlans").
        /// Values entered under the new model (drop-downs, dates, pauses, coordinates) are removed.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_Customers_Dates", table: "Customers");
            migrationBuilder.DropCheckConstraint(name: "CK_Customers_Latitude", table: "Customers");
            migrationBuilder.DropCheckConstraint(name: "CK_Customers_Longitude", table: "Customers");
            migrationBuilder.DropCheckConstraint(name: "CK_Customers_PausedCount", table: "Customers");
            migrationBuilder.DropForeignKey(name: "FK_Customers_DictionaryEntries_PreferenceId", table: "Customers");
            migrationBuilder.DropForeignKey(name: "FK_Customers_DictionaryEntries_MealTypeId", table: "Customers");
            migrationBuilder.DropForeignKey(name: "FK_Customers_DictionaryEntries_FrequencyId", table: "Customers");
            migrationBuilder.DropForeignKey(name: "FK_Customers_DictionaryEntries_StatusId", table: "Customers");
            migrationBuilder.DropTable(name: "CustomerPauses");
            migrationBuilder.DropSequence(name: "CustomerNumberSequence");

            foreach (var table in RetiredTables)
                migrationBuilder.Sql($"""ALTER TABLE legacy."{table}" SET SCHEMA public;""");
            migrationBuilder.AddForeignKey(name: "FK_ImportRows_DeliveryGroups_SuggestedDeliveryGroupId", table: "ImportRows", column: "SuggestedDeliveryGroupId",
                principalTable: "DeliveryGroups", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(name: "FK_ImportRows_DeliveryGroups_TargetDeliveryGroupId", table: "ImportRows", column: "TargetDeliveryGroupId",
                principalTable: "DeliveryGroups", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(name: "FK_ImportDraftGroups_DeliveryGroups_CreatedDeliveryGroupId", table: "ImportDraftGroups",
                column: "CreatedDeliveryGroupId", principalTable: "DeliveryGroups", principalColumn: "Id", onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddColumn<int>(name: "PlanId", table: "Customers", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "PricingTierId", table: "Customers", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "SourceImportBatchId", table: "Customers", type: "integer", nullable: true);
            migrationBuilder.AddColumn<string>(name: "OriginalPlan", table: "Customers", type: "character varying(300)", maxLength: 300, nullable: true);
            migrationBuilder.AddColumn<string>(name: "Status", table: "Customers", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Active");
            migrationBuilder.Sql("""
                UPDATE "Customers" c
                SET "PlanId" = l."PlanId", "PricingTierId" = l."PricingTierId", "SourceImportBatchId" = l."SourceImportBatchId", "OriginalPlan" = l."OriginalPlan"
                FROM legacy."CustomerPlans" l
                WHERE l."CustomerId" = c."Id";

                UPDATE "Customers" c SET "Status" = CASE WHEN d."Code" = 'INACTIVE' THEN 'Inactive' ELSE 'Active' END
                FROM "DictionaryEntries" d
                WHERE d."Id" = c."StatusId";

                DROP TABLE legacy."CustomerPlans";
                DROP SCHEMA IF EXISTS legacy;
                """);

            migrationBuilder.DropIndex(name: "IX_Customers_PreferenceId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_MealTypeId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_FrequencyId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_StatusId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_EndDate", table: "Customers");
            foreach (var column in new[] { "PreferenceId", "MealTypeId", "FrequencyId", "StatusId", "StartDate", "EndDate", "PausedCount", "Latitude", "Longitude", "LegacyPlan" })
                migrationBuilder.DropColumn(name: column, table: "Customers");
            migrationBuilder.RenameColumn(name: "Address", table: "Customers", newName: "Location");
            migrationBuilder.RenameColumn(name: "NormalizedAddress", table: "Customers", newName: "NormalizedLocation");
            migrationBuilder.DropTable(name: "DictionaryEntries");

            migrationBuilder.CreateIndex(name: "IX_Customers_PlanId", table: "Customers", column: "PlanId");
            migrationBuilder.CreateIndex(name: "IX_Customers_PricingTierId", table: "Customers", column: "PricingTierId");
            migrationBuilder.CreateIndex(name: "IX_Customers_SourceImportBatchId", table: "Customers", column: "SourceImportBatchId");
            migrationBuilder.CreateIndex(name: "IX_Customers_Status", table: "Customers", column: "Status");
            migrationBuilder.AddForeignKey(name: "FK_Customers_CataloguePricingTiers_PricingTierId", table: "Customers", column: "PricingTierId",
                principalTable: "CataloguePricingTiers", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(name: "FK_Customers_ImportBatches_SourceImportBatchId", table: "Customers", column: "SourceImportBatchId",
                principalTable: "ImportBatches", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(name: "FK_Customers_Plans_PlanId", table: "Customers", column: "PlanId",
                principalTable: "Plans", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
        }
    }
}
