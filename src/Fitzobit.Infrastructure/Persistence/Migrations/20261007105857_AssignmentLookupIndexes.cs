using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitzobit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssignmentLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerDeliveryGroupAssignments_DeliveryGroupId",
                table: "CustomerDeliveryGroupAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryGroupAssignments_CustomerId_IsActive",
                table: "CustomerDeliveryGroupAssignments",
                columns: new[] { "CustomerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryGroupAssignments_DeliveryGroupId_IsActive",
                table: "CustomerDeliveryGroupAssignments",
                columns: new[] { "DeliveryGroupId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerDeliveryGroupAssignments_CustomerId_IsActive",
                table: "CustomerDeliveryGroupAssignments");

            migrationBuilder.DropIndex(
                name: "IX_CustomerDeliveryGroupAssignments_DeliveryGroupId_IsActive",
                table: "CustomerDeliveryGroupAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryGroupAssignments_DeliveryGroupId",
                table: "CustomerDeliveryGroupAssignments",
                column: "DeliveryGroupId");
        }
    }
}
