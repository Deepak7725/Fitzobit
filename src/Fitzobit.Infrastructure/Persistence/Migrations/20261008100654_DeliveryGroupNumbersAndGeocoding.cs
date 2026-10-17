using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Delivery groups are identified by number; customers get a geocoding status; AppSettings stores the service city.
    /// Written by hand so no data is lost: existing groups are numbered 1, 2, 3 … in creation order and their old name is
    /// kept in the notes (Description); memberships and history are untouched. Customers start as "Pending" so the
    /// background geocoder looks them up once a service city is set.
    /// </summary>
    public partial class DeliveryGroupNumbersAndGeocoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- Delivery groups: Name → GroupNumber ----------
            migrationBuilder.AddColumn<int>(name: "GroupNumber", table: "DeliveryGroups", type: "integer", nullable: true);
            migrationBuilder.Sql("""
                UPDATE "DeliveryGroups" g
                SET "GroupNumber" = n.rn,
                    "Description" = left(CASE WHEN coalesce(trim(g."Description"), '') = '' THEN 'Previously named: ' || g."Name"
                                              ELSE g."Description" || ' (previously named: ' || g."Name" || ')' END, 1000)
                FROM (SELECT "Id", row_number() OVER (ORDER BY "Id") AS rn FROM "DeliveryGroups") n
                WHERE n."Id" = g."Id";
                """);
            migrationBuilder.AlterColumn<int>(name: "GroupNumber", table: "DeliveryGroups", type: "integer", nullable: false,
                oldClrType: typeof(int), oldType: "integer", oldNullable: true);
            migrationBuilder.DropIndex(name: "IX_DeliveryGroups_Name", table: "DeliveryGroups");
            migrationBuilder.DropColumn(name: "Name", table: "DeliveryGroups");
            migrationBuilder.CreateIndex(name: "IX_DeliveryGroups_GroupNumber", table: "DeliveryGroups", column: "GroupNumber", unique: true);
            migrationBuilder.AddCheckConstraint(name: "CK_DeliveryGroups_GroupNumber", table: "DeliveryGroups", sql: "\"GroupNumber\" > 0");

            // ---------- Customers: geocoding status ----------
            migrationBuilder.AddColumn<string>(name: "GeocodeStatus", table: "Customers", type: "character varying(20)", maxLength: 20,
                nullable: false, defaultValue: "Pending");
            migrationBuilder.AddColumn<string>(name: "GeocodeMatchLevel", table: "Customers", type: "character varying(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<int>(name: "GeocodeAttempts", table: "Customers", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<DateTime>(name: "GeocodeNextAttemptAt", table: "Customers", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "GeocodedAt", table: "Customers", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<string>(name: "GeocodeMessage", table: "Customers", type: "character varying(300)", maxLength: 300, nullable: true);
            migrationBuilder.AddColumn<string>(name: "GeocodeSource", table: "Customers", type: "character varying(30)", maxLength: 30, nullable: true);
            migrationBuilder.Sql("""
                UPDATE "Customers" SET "GeocodeStatus" = 'Located', "GeocodeMatchLevel" = 'Address', "GeocodeMessage" = 'Coordinates recorded before geocoding was added'
                WHERE "Latitude" IS NOT NULL AND "Longitude" IS NOT NULL;
                """);
            migrationBuilder.CreateIndex(name: "IX_Customers_GeocodeStatus_GeocodeNextAttemptAt", table: "Customers",
                columns: new[] { "GeocodeStatus", "GeocodeNextAttemptAt" });

            // ---------- Settings ----------
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });
        }

        /// <inheritdoc />
        /// <remarks>Group names come back as "Group N"; the geocoding status and settings are removed (coordinates stay).</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AppSettings");
            migrationBuilder.DropIndex(name: "IX_Customers_GeocodeStatus_GeocodeNextAttemptAt", table: "Customers");
            foreach (var column in new[] { "GeocodeStatus", "GeocodeMatchLevel", "GeocodeAttempts", "GeocodeNextAttemptAt", "GeocodedAt", "GeocodeMessage", "GeocodeSource" })
                migrationBuilder.DropColumn(name: column, table: "Customers");

            migrationBuilder.DropCheckConstraint(name: "CK_DeliveryGroups_GroupNumber", table: "DeliveryGroups");
            migrationBuilder.DropIndex(name: "IX_DeliveryGroups_GroupNumber", table: "DeliveryGroups");
            migrationBuilder.AddColumn<string>(name: "Name", table: "DeliveryGroups", type: "character varying(150)", maxLength: 150, nullable: false, defaultValue: "");
            migrationBuilder.Sql("""UPDATE "DeliveryGroups" SET "Name" = 'Group ' || "GroupNumber";""");
            migrationBuilder.DropColumn(name: "GroupNumber", table: "DeliveryGroups");
            migrationBuilder.CreateIndex(name: "IX_DeliveryGroups_Name", table: "DeliveryGroups", column: "Name", unique: true);
        }
    }
}
