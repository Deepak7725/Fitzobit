using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Part2CustomerImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignmentsChanged",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CustomersCreated",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EmptyRowsSkipped",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ExistingCustomersMatched",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "ImportBatches",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileMessages",
                table: "ImportBatches",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "GroupsCreated",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ImportedCount",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SaveDurationMs",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "SheetName",
                table: "ImportBatches",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ValidationDurationMs",
                table: "ImportBatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Customers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Locality",
                table: "Customers",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalPlan",
                table: "Customers",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PricingTierId",
                table: "Customers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceImportBatchId",
                table: "Customers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportDraftGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImportBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedDeliveryGroupId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportDraftGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportDraftGroups_DeliveryGroups_CreatedDeliveryGroupId",
                        column: x => x.CreatedDeliveryGroupId,
                        principalTable: "DeliveryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImportDraftGroups_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportPlanMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImportBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlanKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ExampleText = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    RowCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PlanId = table.Column<int>(type: "INTEGER", nullable: true),
                    PricingTierId = table.Column<int>(type: "INTEGER", nullable: true),
                    Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CandidatePlanIdsJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportPlanMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportPlanMappings_CataloguePricingTiers_PricingTierId",
                        column: x => x.PricingTierId,
                        principalTable: "CataloguePricingTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImportPlanMappings_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImportPlanMappings_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlanAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AliasKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    OriginalText = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PlanId = table.Column<int>(type: "INTEGER", nullable: false),
                    PricingTierId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanAliases_CataloguePricingTiers_PricingTierId",
                        column: x => x.PricingTierId,
                        principalTable: "CataloguePricingTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlanAliases_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImportBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    RowNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    RawCustomerId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RawCustomerName = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    RawPhoneNumber = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RawLocation = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RawPlan = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CustomerId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    NormalizedPhoneNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    NormalizedLocation = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Pincode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Locality = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PlanKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    PlanId = table.Column<int>(type: "INTEGER", nullable: true),
                    PricingTierId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IssuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    IssueCodes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IsExistingCustomer = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExistingCustomerDbId = table.Column<int>(type: "INTEGER", nullable: true),
                    ExistingGroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsPotentialDuplicate = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExcludedByManager = table.Column<bool>(type: "INTEGER", nullable: false),
                    TargetDeliveryGroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetDraftGroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    SuggestedDeliveryGroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    SuggestedDraftGroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    SuggestionReason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportRows_DeliveryGroups_SuggestedDeliveryGroupId",
                        column: x => x.SuggestedDeliveryGroupId,
                        principalTable: "DeliveryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImportRows_DeliveryGroups_TargetDeliveryGroupId",
                        column: x => x.TargetDeliveryGroupId,
                        principalTable: "DeliveryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImportRows_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImportRows_ImportDraftGroups_SuggestedDraftGroupId",
                        column: x => x.SuggestedDraftGroupId,
                        principalTable: "ImportDraftGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImportRows_ImportDraftGroups_TargetDraftGroupId",
                        column: x => x.TargetDraftGroupId,
                        principalTable: "ImportDraftGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PricingTierId",
                table: "Customers",
                column: "PricingTierId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_SourceImportBatchId",
                table: "Customers",
                column: "SourceImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportDraftGroups_CreatedDeliveryGroupId",
                table: "ImportDraftGroups",
                column: "CreatedDeliveryGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportDraftGroups_ImportBatchId",
                table: "ImportDraftGroups",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportPlanMappings_ImportBatchId_PlanKey",
                table: "ImportPlanMappings",
                columns: new[] { "ImportBatchId", "PlanKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportPlanMappings_PlanId",
                table: "ImportPlanMappings",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportPlanMappings_PricingTierId",
                table: "ImportPlanMappings",
                column: "PricingTierId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_ImportBatchId_Pincode",
                table: "ImportRows",
                columns: new[] { "ImportBatchId", "Pincode" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_ImportBatchId_PlanKey",
                table: "ImportRows",
                columns: new[] { "ImportBatchId", "PlanKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_ImportBatchId_RowNumber",
                table: "ImportRows",
                columns: new[] { "ImportBatchId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_ImportBatchId_Status",
                table: "ImportRows",
                columns: new[] { "ImportBatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_SuggestedDeliveryGroupId",
                table: "ImportRows",
                column: "SuggestedDeliveryGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_SuggestedDraftGroupId",
                table: "ImportRows",
                column: "SuggestedDraftGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_TargetDeliveryGroupId",
                table: "ImportRows",
                column: "TargetDeliveryGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_TargetDraftGroupId",
                table: "ImportRows",
                column: "TargetDraftGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanAliases_AliasKey",
                table: "PlanAliases",
                column: "AliasKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanAliases_PlanId",
                table: "PlanAliases",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanAliases_PricingTierId",
                table: "PlanAliases",
                column: "PricingTierId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_CataloguePricingTiers_PricingTierId",
                table: "Customers",
                column: "PricingTierId",
                principalTable: "CataloguePricingTiers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_ImportBatches_SourceImportBatchId",
                table: "Customers",
                column: "SourceImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_CataloguePricingTiers_PricingTierId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_ImportBatches_SourceImportBatchId",
                table: "Customers");

            migrationBuilder.DropTable(
                name: "ImportPlanMappings");

            migrationBuilder.DropTable(
                name: "ImportRows");

            migrationBuilder.DropTable(
                name: "PlanAliases");

            migrationBuilder.DropTable(
                name: "ImportDraftGroups");

            migrationBuilder.DropIndex(
                name: "IX_Customers_PricingTierId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_SourceImportBatchId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AssignmentsChanged",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "CustomersCreated",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "EmptyRowsSkipped",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ExistingCustomersMatched",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "FileMessages",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "GroupsCreated",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ImportedCount",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "SaveDurationMs",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "SheetName",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ValidationDurationMs",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Locality",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OriginalPlan",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PricingTierId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SourceImportBatchId",
                table: "Customers");
        }
    }
}
