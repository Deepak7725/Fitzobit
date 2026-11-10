using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Money cost history (owner, 2026-11-10): costs and profit targets are saved as versions with a "valid from" date, and the Manager can
    /// add cost lines by name (per box, all boxes). The values saved so far in AppSettings (Money.*) become the first version, valid from
    /// 1 Jan 2000 so every earlier day uses them; those AppSettings rows are then removed.
    /// </summary>
    public partial class MoneyCostHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MoneyCostVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    DeliveryCostPerBox = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    HelperCostPerBox = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoneyCostVersions", x => x.Id);
                    table.CheckConstraint("CK_MoneyCostVersions_DeliveryCost", "\"DeliveryCostPerBox\" >= 0");
                    table.CheckConstraint("CK_MoneyCostVersions_HelperCost", "\"HelperCostPerBox\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "MoneyCostLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VersionId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    AmountPerBox = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoneyCostLines", x => x.Id);
                    table.CheckConstraint("CK_MoneyCostLines_Amount", "\"AmountPerBox\" >= 0");
                    table.ForeignKey(
                        name: "FK_MoneyCostLines_MoneyCostVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "MoneyCostVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MoneyProfitTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VersionId = table.Column<int>(type: "integer", nullable: false),
                    FrequencyId = table.Column<int>(type: "integer", nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoneyProfitTargets", x => x.Id);
                    table.CheckConstraint("CK_MoneyProfitTargets_Percent", "\"Percent\" BETWEEN 0 AND 95");
                    table.ForeignKey(
                        name: "FK_MoneyProfitTargets_DictionaryEntries_FrequencyId",
                        column: x => x.FrequencyId,
                        principalTable: "DictionaryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyProfitTargets_MoneyCostVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "MoneyCostVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyCostLines_VersionId_SortOrder",
                table: "MoneyCostLines",
                columns: new[] { "VersionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyCostVersions_EffectiveFrom",
                table: "MoneyCostVersions",
                column: "EffectiveFrom",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MoneyProfitTargets_FrequencyId",
                table: "MoneyProfitTargets",
                column: "FrequencyId");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyProfitTargets_VersionId_FrequencyId",
                table: "MoneyProfitTargets",
                columns: new[] { "VersionId", "FrequencyId" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO "MoneyCostVersions" ("EffectiveFrom", "DeliveryCostPerBox", "HelperCostPerBox", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy")
                VALUES ('2000-01-01',
                        COALESCE((SELECT "Value"::numeric FROM "AppSettings" WHERE "Key" = 'Money.DeliveryCostPerBox'), 30),
                        COALESCE((SELECT "Value"::numeric FROM "AppSettings" WHERE "Key" = 'Money.HelperCostPerBox'), 25),
                        now(), now(), 'system', 'system');

                INSERT INTO "MoneyProfitTargets" ("VersionId", "FrequencyId", "Percent")
                SELECT v."Id", f."Id", COALESCE(s."Value"::numeric, 30)
                FROM "MoneyCostVersions" v
                CROSS JOIN "DictionaryEntries" f
                LEFT JOIN "AppSettings" s ON s."Key" = 'Money.ProfitTarget.' || f."Code"
                WHERE v."EffectiveFrom" = '2000-01-01' AND f."DictionaryType" = 'Frequency';

                DELETE FROM "AppSettings" WHERE "Key" LIKE 'Money.%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to AppSettings: the latest version's costs and targets (cost lines and history are lost).
            migrationBuilder.Sql("""
                INSERT INTO "AppSettings" ("Key", "Value", "CreatedAt", "UpdatedAt", "UpdatedBy")
                SELECT k.key, k.value, now(), now(), 'system'
                FROM (SELECT * FROM "MoneyCostVersions" ORDER BY "EffectiveFrom" DESC LIMIT 1) v
                CROSS JOIN LATERAL (VALUES ('Money.DeliveryCostPerBox', v."DeliveryCostPerBox"::text),
                                           ('Money.HelperCostPerBox', v."HelperCostPerBox"::text)) AS k(key, value)
                ON CONFLICT ("Key") DO UPDATE SET "Value" = EXCLUDED."Value";

                INSERT INTO "AppSettings" ("Key", "Value", "CreatedAt", "UpdatedAt", "UpdatedBy")
                SELECT 'Money.ProfitTarget.' || f."Code", t."Percent"::text, now(), now(), 'system'
                FROM (SELECT * FROM "MoneyCostVersions" ORDER BY "EffectiveFrom" DESC LIMIT 1) v
                JOIN "MoneyProfitTargets" t ON t."VersionId" = v."Id"
                JOIN "DictionaryEntries" f ON f."Id" = t."FrequencyId"
                ON CONFLICT ("Key") DO UPDATE SET "Value" = EXCLUDED."Value";
                """);

            migrationBuilder.DropTable(
                name: "MoneyCostLines");

            migrationBuilder.DropTable(
                name: "MoneyProfitTargets");

            migrationBuilder.DropTable(
                name: "MoneyCostVersions");
        }
    }
}
