using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessLedgerAndScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MonitoringRetentionMonths",
                table: "Organizations",
                type: "int",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "SemanticRole",
                table: "DatabaseColumns",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccessBaselines",
                columns: table => new
                {
                    BaselineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectKind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RoleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    WindowDays = table.Column<int>(type: "int", nullable: false),
                    WindowEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    ActiveDays = table.Column<int>(type: "int", nullable: false),
                    QueriesPerDayMedian = table.Column<double>(type: "float", nullable: false),
                    QueriesPerDayP95 = table.Column<double>(type: "float", nullable: false),
                    RowsPerDayMedian = table.Column<double>(type: "float", nullable: false),
                    RowsPerDayP95 = table.Column<double>(type: "float", nullable: false),
                    RestrictedTouchesPerDayMedian = table.Column<double>(type: "float", nullable: false),
                    RestrictedTouchesPerDayP95 = table.Column<double>(type: "float", nullable: false),
                    ExportsPerDayMedian = table.Column<double>(type: "float", nullable: false),
                    ExportsPerDayP95 = table.Column<double>(type: "float", nullable: false),
                    UnauthorizedPerDayMedian = table.Column<double>(type: "float", nullable: false),
                    UnauthorizedPerDayP95 = table.Column<double>(type: "float", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessBaselines", x => x.BaselineId);
                    table.ForeignKey(
                        name: "FK_AccessBaselines_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisAccessFacts",
                columns: table => new
                {
                    FactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RoleCodes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HistoryVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MetricId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SchemaName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ObjectName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsPrimaryObject = table.Column<bool>(type: "bit", nullable: false),
                    AccessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    HighestClassification = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ColumnIdsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ColumnCount = table.Column<int>(type: "int", nullable: false),
                    FilterColumnIdsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FilterValueCount = table.Column<int>(type: "int", nullable: false),
                    IdentifierFilterValueCount = table.Column<int>(type: "int", nullable: false),
                    IsExport = table.Column<bool>(type: "bit", nullable: false),
                    ExportFormat = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisAccessFacts", x => x.FactId);
                    table.ForeignKey(
                        name: "FK_AnalysisAccessFacts_OrganizationUsers_OrganizationId_UserId",
                        columns: x => new { x.OrganizationId, x.UserId },
                        principalTable: "OrganizationUsers",
                        principalColumns: new[] { "OrganizationId", "UserId" });
                    table.ForeignKey(
                        name: "FK_AnalysisAccessFacts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledJobs",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecurrenceKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    TimeOfDay = table.Column<TimeOnly>(type: "time", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: true),
                    DayOfMonth = table.Column<int>(type: "int", nullable: true),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    NextRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastOutcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastDurationMs = table.Column<double>(type: "float", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledJobs", x => x.JobId);
                    table.ForeignKey(
                        name: "FK_ScheduledJobs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessBaselines_OrganizationId_SubjectKind_UserId_RoleCode",
                table: "AccessBaselines",
                columns: new[] { "OrganizationId", "SubjectKind", "UserId", "RoleCode" },
                unique: true,
                filter: "[UserId] IS NOT NULL AND [RoleCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisAccessFacts_OrganizationId_AccessedAt",
                table: "AnalysisAccessFacts",
                columns: new[] { "OrganizationId", "AccessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisAccessFacts_OrganizationId_ObjectId_AccessedAt",
                table: "AnalysisAccessFacts",
                columns: new[] { "OrganizationId", "ObjectId", "AccessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisAccessFacts_OrganizationId_Outcome_AccessedAt",
                table: "AnalysisAccessFacts",
                columns: new[] { "OrganizationId", "Outcome", "AccessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisAccessFacts_OrganizationId_UserId_AccessedAt",
                table: "AnalysisAccessFacts",
                columns: new[] { "OrganizationId", "UserId", "AccessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_IsEnabled_NextRunAt",
                table: "ScheduledJobs",
                columns: new[] { "IsEnabled", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_OrganizationId_JobType_TargetId",
                table: "ScheduledJobs",
                columns: new[] { "OrganizationId", "JobType", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessBaselines");

            migrationBuilder.DropTable(
                name: "AnalysisAccessFacts");

            migrationBuilder.DropTable(
                name: "ScheduledJobs");

            migrationBuilder.DropColumn(
                name: "MonitoringRetentionMonths",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "SemanticRole",
                table: "DatabaseColumns");
        }
    }
}
