using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoArtifactKey",
                table: "Organizations",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoContentType",
                table: "Organizations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportArtifactKey",
                table: "AnalysisExecutionMetrics",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportFileName",
                table: "AnalysisExecutionMetrics",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReportInputTokens",
                table: "AnalysisExecutionMetrics",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ReportOutputTokens",
                table: "AnalysisExecutionMetrics",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ReportSizeBytes",
                table: "AnalysisExecutionMetrics",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoArtifactKey",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "LogoContentType",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ReportArtifactKey",
                table: "AnalysisExecutionMetrics");

            migrationBuilder.DropColumn(
                name: "ReportFileName",
                table: "AnalysisExecutionMetrics");

            migrationBuilder.DropColumn(
                name: "ReportInputTokens",
                table: "AnalysisExecutionMetrics");

            migrationBuilder.DropColumn(
                name: "ReportOutputTokens",
                table: "AnalysisExecutionMetrics");

            migrationBuilder.DropColumn(
                name: "ReportSizeBytes",
                table: "AnalysisExecutionMetrics");
        }
    }
}
