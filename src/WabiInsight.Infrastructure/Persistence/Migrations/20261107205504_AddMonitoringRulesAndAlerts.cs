using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringRulesAndAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "MonitoringDigestTime",
                table: "Organizations",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(7, 0, 0));

            migrationBuilder.AddColumn<string>(
                name: "WorkingDays",
                table: "Organizations",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "12345");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WorkingHoursEnd",
                table: "Organizations",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(18, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WorkingHoursStart",
                table: "Organizations",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(8, 0, 0));

            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    AlertId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    GroupKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SummaryJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Occurrences = table.Column<int>(type: "int", nullable: false),
                    StatusChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StatusChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.AlertId);
                    table.UniqueConstraint("AK_Alerts_OrganizationId_AlertId", x => new { x.OrganizationId, x.AlertId });
                    table.ForeignKey(
                        name: "FK_Alerts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringRules",
                columns: table => new
                {
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RecurrenceKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    TimeOfDay = table.Column<TimeOnly>(type: "time", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: true),
                    CooldownHours = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastEvaluatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastOutcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringRules", x => x.RuleId);
                    table.UniqueConstraint("AK_MonitoringRules_OrganizationId_RuleId", x => new { x.OrganizationId, x.RuleId });
                    table.ForeignKey(
                        name: "FK_MonitoringRules_OrganizationUsers_OrganizationId_OwnerUserId",
                        columns: x => new { x.OrganizationId, x.OwnerUserId },
                        principalTable: "OrganizationUsers",
                        principalColumns: new[] { "OrganizationId", "UserId" });
                    table.ForeignKey(
                        name: "FK_MonitoringRules_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuleEvaluations",
                columns: table => new
                {
                    EvaluationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsTest = table.Column<bool>(type: "bit", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    MatchCount = table.Column<int>(type: "int", nullable: false),
                    AlertsRaised = table.Column<int>(type: "int", nullable: false),
                    RowsExamined = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleEvaluations", x => x.EvaluationId);
                    table.ForeignKey(
                        name: "FK_RuleEvaluations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AlertEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertEvents", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_AlertEvents_Alerts_OrganizationId_AlertId",
                        columns: x => new { x.OrganizationId, x.AlertId },
                        principalTable: "Alerts",
                        principalColumns: new[] { "OrganizationId", "AlertId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringRuleRecipients",
                columns: table => new
                {
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringRuleRecipients", x => x.RecipientId);
                    table.CheckConstraint("CK_MonitoringRuleRecipients_OneTarget", "([UserId] IS NULL AND [RoleId] IS NOT NULL) OR ([UserId] IS NOT NULL AND [RoleId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_MonitoringRuleRecipients_MonitoringRules_OrganizationId_RuleId",
                        columns: x => new { x.OrganizationId, x.RuleId },
                        principalTable: "MonitoringRules",
                        principalColumns: new[] { "OrganizationId", "RuleId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MonitoringRuleRecipients_OrganizationUsers_OrganizationId_UserId",
                        columns: x => new { x.OrganizationId, x.UserId },
                        principalTable: "OrganizationUsers",
                        principalColumns: new[] { "OrganizationId", "UserId" });
                    table.ForeignKey(
                        name: "FK_MonitoringRuleRecipients_Roles_OrganizationId_RoleId",
                        columns: x => new { x.OrganizationId, x.RoleId },
                        principalTable: "Roles",
                        principalColumns: new[] { "OrganizationId", "RoleId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_OrganizationId_AlertId_CreatedAt",
                table: "AlertEvents",
                columns: new[] { "OrganizationId", "AlertId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_OrganizationId_RuleId_GroupKey_Status",
                table: "Alerts",
                columns: new[] { "OrganizationId", "RuleId", "GroupKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_OrganizationId_Status_LastSeenAt",
                table: "Alerts",
                columns: new[] { "OrganizationId", "Status", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringRuleRecipients_OrganizationId_RoleId",
                table: "MonitoringRuleRecipients",
                columns: new[] { "OrganizationId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringRuleRecipients_OrganizationId_RuleId_UserId_RoleId",
                table: "MonitoringRuleRecipients",
                columns: new[] { "OrganizationId", "RuleId", "UserId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringRuleRecipients_OrganizationId_UserId",
                table: "MonitoringRuleRecipients",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringRules_OrganizationId_Name",
                table: "MonitoringRules",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringRules_OrganizationId_OwnerUserId",
                table: "MonitoringRules",
                columns: new[] { "OrganizationId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RuleEvaluations_OrganizationId_RuleId_StartedAt",
                table: "RuleEvaluations",
                columns: new[] { "OrganizationId", "RuleId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertEvents");

            migrationBuilder.DropTable(
                name: "MonitoringRuleRecipients");

            migrationBuilder.DropTable(
                name: "RuleEvaluations");

            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "MonitoringRules");

            migrationBuilder.DropColumn(
                name: "MonitoringDigestTime",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "WorkingDays",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "WorkingHoursEnd",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "WorkingHoursStart",
                table: "Organizations");
        }
    }
}
