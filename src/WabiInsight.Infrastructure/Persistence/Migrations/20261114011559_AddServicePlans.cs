using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServicePlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ServicePlanId",
                table: "Organizations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServicePlans",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MonthlyAiSpendLimit = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePlans", x => x.PlanId);
                    table.CheckConstraint("CK_ServicePlans_Limit", "[MonthlyAiSpendLimit] IS NULL OR [MonthlyAiSpendLimit] > 0");
                });

            migrationBuilder.InsertData(
                table: "ServicePlans",
                columns: new[] { "PlanId", "IsActive", "MonthlyAiSpendLimit", "NameAr", "NameEn", "PlanCode", "SortOrder", "UpdatedAt", "UpdatedByAdministratorId" },
                values: new object[,]
                {
                    { new Guid("5a7b2c10-0000-7000-8000-000000000001"), true, 50m, "الأساسية", "Starter", "STARTER", 10, new DateTimeOffset(new DateTime(2026, 11, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("5a7b2c10-0000-7000-8000-000000000002"), true, 250m, "الأعمال", "Business", "BUSINESS", 20, new DateTimeOffset(new DateTime(2026, 11, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("5a7b2c10-0000-7000-8000-000000000003"), true, 1000m, "المؤسسات", "Enterprise", "ENTERPRISE", 30, new DateTimeOffset(new DateTime(2026, 11, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_ServicePlanId",
                table: "Organizations",
                column: "ServicePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlans_PlanCode",
                table: "ServicePlans",
                column: "PlanCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Organizations_ServicePlans_ServicePlanId",
                table: "Organizations",
                column: "ServicePlanId",
                principalTable: "ServicePlans",
                principalColumn: "PlanId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Organizations_ServicePlans_ServicePlanId",
                table: "Organizations");

            migrationBuilder.DropTable(
                name: "ServicePlans");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_ServicePlanId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ServicePlanId",
                table: "Organizations");
        }
    }
}
