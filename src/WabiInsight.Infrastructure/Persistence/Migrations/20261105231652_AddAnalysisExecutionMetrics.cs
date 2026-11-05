using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisExecutionMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisExecutionMetrics",
                columns: table => new
                {
                    MetricId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FailureStage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PlanInputTokens = table.Column<long>(type: "bigint", nullable: false),
                    PlanOutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    InterpretInputTokens = table.Column<long>(type: "bigint", nullable: false),
                    InterpretOutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: false),
                    PlanMs = table.Column<double>(type: "float", nullable: false),
                    ExecuteMs = table.Column<double>(type: "float", nullable: false),
                    InterpretMs = table.Column<double>(type: "float", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    Truncated = table.Column<bool>(type: "bit", nullable: false),
                    ObjectCount = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisExecutionMetrics", x => x.MetricId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisExecutionMetrics_OrganizationId_CreatedAt",
                table: "AnalysisExecutionMetrics",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisExecutionMetrics_OrganizationId_UserId",
                table: "AnalysisExecutionMetrics",
                columns: new[] { "OrganizationId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalysisExecutionMetrics");
        }
    }
}
