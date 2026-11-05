using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <summary>Pause for everyone (owner's request, 2026-11-04): festive/emergency days and the link from each package pause to its day.</summary>
    public partial class FestivePauses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FestivePauseId",
                table: "PackagePauses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FestivePauses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FestivePauses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackagePauses_FestivePauseId",
                table: "PackagePauses",
                column: "FestivePauseId");

            migrationBuilder.CreateIndex(
                name: "IX_FestivePauses_Date",
                table: "FestivePauses",
                column: "Date",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PackagePauses_FestivePauses_FestivePauseId",
                table: "PackagePauses",
                column: "FestivePauseId",
                principalTable: "FestivePauses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackagePauses_FestivePauses_FestivePauseId",
                table: "PackagePauses");

            migrationBuilder.DropTable(
                name: "FestivePauses");

            migrationBuilder.DropIndex(
                name: "IX_PackagePauses_FestivePauseId",
                table: "PackagePauses");

            migrationBuilder.DropColumn(
                name: "FestivePauseId",
                table: "PackagePauses");
        }
    }
}
