using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInsightsMetricGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleMetricPermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InsightsMetricId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleMetricPermissions", x => new { x.RoleId, x.InsightsMetricId });
                    table.ForeignKey(
                        name: "FK_RoleMetricPermissions_InsightsMetrics_InsightsMetricId",
                        column: x => x.InsightsMetricId,
                        principalTable: "InsightsMetrics",
                        principalColumn: "InsightsMetricId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoleMetricPermissions_Roles_OrganizationId_RoleId",
                        columns: x => new { x.OrganizationId, x.RoleId },
                        principalTable: "Roles",
                        principalColumns: new[] { "OrganizationId", "RoleId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoleMetricPermissions_InsightsMetricId",
                table: "RoleMetricPermissions",
                column: "InsightsMetricId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleMetricPermissions_OrganizationId",
                table: "RoleMetricPermissions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleMetricPermissions_OrganizationId_RoleId",
                table: "RoleMetricPermissions",
                columns: new[] { "OrganizationId", "RoleId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleMetricPermissions");
        }
    }
}
