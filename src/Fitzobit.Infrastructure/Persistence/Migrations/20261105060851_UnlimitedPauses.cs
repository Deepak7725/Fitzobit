using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// No paused days limit (owner's decision, 2026-11-05): every paused day moves the package's end date, not only the first 3.
    /// The PausedCount check becomes "&gt;= 0", and current packages (not past ones) are recalculated: each own paused day inside the package
    /// that did not move the end date now moves it one delivery day (Sundays skipped), in date order, and its pause row records the move.
    /// PausedCount = own paused days inside the package. One audit entry per affected customer ("system"). Days paused for everyone are
    /// left as they are (they always extended). The data change is not undone by Down.
    /// </summary>
    public partial class UnlimitedPauses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerPackages_PausedCount",
                table: "CustomerPackages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerPackages_PausedCount",
                table: "CustomerPackages",
                sql: "\"PausedCount\" >= 0");

            migrationBuilder.Sql("""
                CREATE TEMP TABLE "_UnlimitedPausesFix" ("PackageId" integer PRIMARY KEY, "OldEndDate" date NOT NULL);

                DO $$
                DECLARE
                    r record;
                    old_end date;
                    new_end date;
                BEGIN
                    FOR r IN
                        SELECT pp."Id", pp."PackageId"
                        FROM "PackagePauses" pp
                        JOIN "CustomerPackages" p ON p."Id" = pp."PackageId"
                        WHERE p."ArchivedAt" IS NULL
                          AND pp."FestivePauseId" IS NULL
                          AND pp."NewEndDate" = pp."PreviousEndDate"
                          AND pp."PausedDate" BETWEEN p."StartDate" AND p."EndDate"
                        ORDER BY pp."PackageId", pp."PausedDate", pp."Id"
                    LOOP
                        SELECT "EndDate" INTO old_end FROM "CustomerPackages" WHERE "Id" = r."PackageId";
                        INSERT INTO "_UnlimitedPausesFix" VALUES (r."PackageId", old_end) ON CONFLICT DO NOTHING;
                        new_end := old_end + 1;
                        IF extract(isodow FROM new_end) = 7 THEN new_end := new_end + 1; END IF; -- Sundays are not delivery days
                        UPDATE "PackagePauses" SET "PreviousEndDate" = old_end, "NewEndDate" = new_end WHERE "Id" = r."Id";
                        UPDATE "CustomerPackages" SET "EndDate" = new_end WHERE "Id" = r."PackageId";
                    END LOOP;
                END $$;

                UPDATE "CustomerPackages" p
                SET "PausedCount" = (SELECT count(*) FROM "PackagePauses" pp
                                     WHERE pp."PackageId" = p."Id" AND pp."FestivePauseId" IS NULL
                                       AND pp."PausedDate" BETWEEN p."StartDate" AND p."EndDate")
                WHERE p."ArchivedAt" IS NULL;

                INSERT INTO "AuditLogs" ("OccurredAt", "Action", "EntityType", "EntityKey", "Summary", "OldValues", "NewValues", "PerformedBy")
                SELECT now(), 'CustomerPackageUpdated', 'Customer', c."CustomerId",
                       left('Package end date recalculated: ' || c."CustomerId" || ' — every paused day now moves the end date ('
                            || string_agg(to_char(f."OldEndDate", 'DD Mon YYYY') || ' → ' || to_char(p."EndDate", 'DD Mon YYYY'), '; ' ORDER BY p."Id") || ')', 1000),
                       json_object_agg('#' || p."Id", json_build_object('EndDate', f."OldEndDate"))::text,
                       json_object_agg('#' || p."Id", json_build_object('EndDate', p."EndDate", 'PausedCount', p."PausedCount"))::text,
                       'system'
                FROM "_UnlimitedPausesFix" f
                JOIN "CustomerPackages" p ON p."Id" = f."PackageId"
                JOIN "Customers" c ON c."Id" = p."CustomerId"
                GROUP BY c."CustomerId";

                DROP TABLE "_UnlimitedPausesFix";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerPackages_PausedCount",
                table: "CustomerPackages");

            migrationBuilder.Sql("""UPDATE "CustomerPackages" SET "PausedCount" = 3 WHERE "PausedCount" > 3;""");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerPackages_PausedCount",
                table: "CustomerPackages",
                sql: "\"PausedCount\" BETWEEN 0 AND 3");
        }
    }
}
