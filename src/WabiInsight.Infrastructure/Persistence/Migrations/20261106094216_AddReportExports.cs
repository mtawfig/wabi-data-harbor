using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportGenerationEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Format = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<double>(type: "float", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportGenerationEvents", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportGenerationEvents_OrganizationId_CreatedAt",
                table: "ReportGenerationEvents",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportGenerationEvents_OrganizationId_HistoryId",
                table: "ReportGenerationEvents",
                columns: new[] { "OrganizationId", "HistoryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportGenerationEvents");
        }
    }
}
