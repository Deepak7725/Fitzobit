using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fitzobit.Infrastructure.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgreSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OldValues = table.Column<string>(type: "text", nullable: true),
                    NewValues = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    PerformedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Term = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DisplayPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BillingPeriod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DurationText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    MealCount = table.Column<int>(type: "integer", nullable: true),
                    IncludedItems = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Availability = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SourceNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SheetName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    ValidRows = table.Column<int>(type: "integer", nullable: false),
                    InvalidRows = table.Column<int>(type: "integer", nullable: false),
                    WarningRows = table.Column<int>(type: "integer", nullable: false),
                    EmptyRowsSkipped = table.Column<int>(type: "integer", nullable: false),
                    ImportedCount = table.Column<int>(type: "integer", nullable: false),
                    CustomersCreated = table.Column<int>(type: "integer", nullable: false),
                    ExistingCustomersMatched = table.Column<int>(type: "integer", nullable: false),
                    GroupsCreated = table.Column<int>(type: "integer", nullable: false),
                    AssignmentsChanged = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FileMessages = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ValidationDurationMs = table.Column<long>(type: "bigint", nullable: false),
                    SaveDurationMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SecurityStamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false),
                    LockoutEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CataloguePricingTiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CatalogueItemId = table.Column<int>(type: "integer", nullable: false),
                    TierName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BillingPeriod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MealCount = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CataloguePricingTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CataloguePricingTiers_CatalogueItems_CatalogueItemId",
                        column: x => x.CatalogueItemId,
                        principalTable: "CatalogueItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CatalogueItemId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Plans_CatalogueItems_CatalogueItemId",
                        column: x => x.CatalogueItemId,
                        principalTable: "CatalogueItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ImportDraftGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportBatchId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedDeliveryGroupId = table.Column<int>(type: "integer", nullable: true)
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
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    NormalizedPhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NormalizedLocation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Pincode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Locality = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OriginalPlan = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PlanId = table.Column<int>(type: "integer", nullable: true),
                    PricingTierId = table.Column<int>(type: "integer", nullable: true),
                    SourceImportBatchId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_CataloguePricingTiers_PricingTierId",
                        column: x => x.PricingTierId,
                        principalTable: "CataloguePricingTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Customers_ImportBatches_SourceImportBatchId",
                        column: x => x.SourceImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Customers_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ImportPlanMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportBatchId = table.Column<int>(type: "integer", nullable: false),
                    PlanKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ExampleText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: true),
                    PricingTierId = table.Column<int>(type: "integer", nullable: true),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CandidatePlanIdsJson = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AliasKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OriginalText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    PricingTierId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportBatchId = table.Column<int>(type: "integer", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    RawCustomerId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RawCustomerName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RawPhoneNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RawLocation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RawPlan = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CustomerId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NormalizedPhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    NormalizedLocation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Pincode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Locality = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PlanKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PlanId = table.Column<int>(type: "integer", nullable: true),
                    PricingTierId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IssuesJson = table.Column<string>(type: "text", nullable: false),
                    IssueCodes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsExistingCustomer = table.Column<bool>(type: "boolean", nullable: false),
                    ExistingCustomerDbId = table.Column<int>(type: "integer", nullable: true),
                    ExistingGroupId = table.Column<int>(type: "integer", nullable: true),
                    IsPotentialDuplicate = table.Column<bool>(type: "boolean", nullable: false),
                    ExcludedByManager = table.Column<bool>(type: "boolean", nullable: false),
                    TargetDeliveryGroupId = table.Column<int>(type: "integer", nullable: true),
                    TargetDraftGroupId = table.Column<int>(type: "integer", nullable: true),
                    SuggestedDeliveryGroupId = table.Column<int>(type: "integer", nullable: true),
                    SuggestedDraftGroupId = table.Column<int>(type: "integer", nullable: true),
                    SuggestionReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "CustomerDeliveryGroupAssignmentHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    PreviousGroupId = table.Column<int>(type: "integer", nullable: true),
                    NewGroupId = table.Column<int>(type: "integer", nullable: true),
                    ChangedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDeliveryGroupAssignmentHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDeliveryGroupAssignmentHistory_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDeliveryGroupAssignmentHistory_DeliveryGroups_NewGr~",
                        column: x => x.NewGroupId,
                        principalTable: "DeliveryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDeliveryGroupAssignmentHistory_DeliveryGroups_Previ~",
                        column: x => x.PreviousGroupId,
                        principalTable: "DeliveryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerDeliveryGroupAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    DeliveryGroupId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDeliveryGroupAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDeliveryGroupAssignments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDeliveryGroupAssignments_DeliveryGroups_DeliveryGro~",
                        column: x => x.DeliveryGroupId,
                        principalTable: "DeliveryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    DeliveryGroupId = table.Column<int>(type: "integer", nullable: true),
                    ToPhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TemplateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ParametersJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessages_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessages_DeliveryGroups_DeliveryGroupId",
                        column: x => x.DeliveryGroupId,
                        principalTable: "DeliveryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityKey",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityKey" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredAt",
                table: "AuditLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueItems_Category",
                table: "CatalogueItems",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueItems_Name",
                table: "CatalogueItems",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CataloguePricingTiers_CatalogueItemId_TierName",
                table: "CataloguePricingTiers",
                columns: new[] { "CatalogueItemId", "TierName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryGroupAssignmentHistory_ChangedAt",
                table: "CustomerDeliveryGroupAssignmentHistory",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryGroupAssignmentHistory_CustomerId",
                table: "CustomerDeliveryGroupAssignmentHistory",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryGroupAssignmentHistory_NewGroupId",
                table: "CustomerDeliveryGroupAssignmentHistory",
                column: "NewGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryGroupAssignmentHistory_PreviousGroupId",
                table: "CustomerDeliveryGroupAssignmentHistory",
                column: "PreviousGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryGroupAssignments_CustomerId_IsActive",
                table: "CustomerDeliveryGroupAssignments",
                columns: new[] { "CustomerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryGroupAssignments_DeliveryGroupId_IsActive",
                table: "CustomerDeliveryGroupAssignments",
                columns: new[] { "DeliveryGroupId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_CustomerDeliveryGroupAssignments_ActiveCustomer",
                table: "CustomerDeliveryGroupAssignments",
                column: "CustomerId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedAt",
                table: "Customers",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerId",
                table: "Customers",
                column: "CustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_NormalizedPhoneNumber",
                table: "Customers",
                column: "NormalizedPhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Pincode",
                table: "Customers",
                column: "Pincode");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PlanId",
                table: "Customers",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PricingTierId",
                table: "Customers",
                column: "PricingTierId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_SourceImportBatchId",
                table: "Customers",
                column: "SourceImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Status",
                table: "Customers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryGroups_Name",
                table: "DeliveryGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryGroups_Status",
                table: "DeliveryGroups",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_CreatedAt",
                table: "ImportBatches",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_Status",
                table: "ImportBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_UploadedAt",
                table: "ImportBatches",
                column: "UploadedAt");

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

            migrationBuilder.CreateIndex(
                name: "IX_Plans_CatalogueItemId",
                table: "Plans",
                column: "CatalogueItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Code",
                table: "Plans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Name",
                table: "Plans",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Status",
                table: "Plans",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_CreatedAt",
                table: "WhatsAppMessages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_CustomerId",
                table: "WhatsAppMessages",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_DeliveryGroupId",
                table: "WhatsAppMessages",
                column: "DeliveryGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ProviderMessageId",
                table: "WhatsAppMessages",
                column: "ProviderMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CustomerDeliveryGroupAssignmentHistory");

            migrationBuilder.DropTable(
                name: "CustomerDeliveryGroupAssignments");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "ImportPlanMappings");

            migrationBuilder.DropTable(
                name: "ImportRows");

            migrationBuilder.DropTable(
                name: "PlanAliases");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "WhatsAppMessages");

            migrationBuilder.DropTable(
                name: "ImportDraftGroups");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "DeliveryGroups");

            migrationBuilder.DropTable(
                name: "CataloguePricingTiers");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "Plans");

            migrationBuilder.DropTable(
                name: "CatalogueItems");
        }
    }
}
