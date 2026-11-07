using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportDeliveries",
                columns: table => new
                {
                    DeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ReportName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AnalysisTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IsManualRun = table.Column<bool>(type: "bit", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    EmailMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportDeliveries", x => x.DeliveryId);
                    table.ForeignKey(
                        name: "FK_ReportDeliveries_OrganizationUsers_OrganizationId_RecipientUserId",
                        columns: x => new { x.OrganizationId, x.RecipientUserId },
                        principalTable: "OrganizationUsers",
                        principalColumns: new[] { "OrganizationId", "UserId" });
                    table.ForeignKey(
                        name: "FK_ReportDeliveries_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportSubscriptions",
                columns: table => new
                {
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecurrenceKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TimeOfDay = table.Column<TimeOnly>(type: "time", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: true),
                    DayOfMonth = table.Column<int>(type: "int", nullable: true),
                    Format = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PausedReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    LastRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportSubscriptions", x => x.SubscriptionId);
                    table.UniqueConstraint("AK_ReportSubscriptions_OrganizationId_SubscriptionId", x => new { x.OrganizationId, x.SubscriptionId });
                    table.CheckConstraint("CK_ReportSubscriptions_Format", "[Format] IN ('Pdf','Xlsx')");
                    table.CheckConstraint("CK_ReportSubscriptions_Language", "[Language] IN ('en','ar','recipient')");
                    table.ForeignKey(
                        name: "FK_ReportSubscriptions_AnalysisHistories_HistoryId",
                        column: x => x.HistoryId,
                        principalTable: "AnalysisHistories",
                        principalColumn: "HistoryId");
                    table.ForeignKey(
                        name: "FK_ReportSubscriptions_OrganizationUsers_OrganizationId_CreatedByUserId",
                        columns: x => new { x.OrganizationId, x.CreatedByUserId },
                        principalTable: "OrganizationUsers",
                        principalColumns: new[] { "OrganizationId", "UserId" });
                    table.ForeignKey(
                        name: "FK_ReportSubscriptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportSubscriptionRecipients",
                columns: table => new
                {
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportSubscriptionRecipients", x => x.RecipientId);
                    table.ForeignKey(
                        name: "FK_ReportSubscriptionRecipients_OrganizationUsers_OrganizationId_UserId",
                        columns: x => new { x.OrganizationId, x.UserId },
                        principalTable: "OrganizationUsers",
                        principalColumns: new[] { "OrganizationId", "UserId" });
                    table.ForeignKey(
                        name: "FK_ReportSubscriptionRecipients_ReportSubscriptions_OrganizationId_SubscriptionId",
                        columns: x => new { x.OrganizationId, x.SubscriptionId },
                        principalTable: "ReportSubscriptions",
                        principalColumns: new[] { "OrganizationId", "SubscriptionId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportDeliveries_OrganizationId_RecipientUserId_CreatedAt",
                table: "ReportDeliveries",
                columns: new[] { "OrganizationId", "RecipientUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportDeliveries_OrganizationId_SubscriptionId_CreatedAt",
                table: "ReportDeliveries",
                columns: new[] { "OrganizationId", "SubscriptionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSubscriptionRecipients_OrganizationId_SubscriptionId_UserId",
                table: "ReportSubscriptionRecipients",
                columns: new[] { "OrganizationId", "SubscriptionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportSubscriptionRecipients_OrganizationId_UserId",
                table: "ReportSubscriptionRecipients",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSubscriptions_HistoryId",
                table: "ReportSubscriptions",
                column: "HistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportSubscriptions_OrganizationId_CreatedByUserId",
                table: "ReportSubscriptions",
                columns: new[] { "OrganizationId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSubscriptions_OrganizationId_Name",
                table: "ReportSubscriptions",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportDeliveries");

            migrationBuilder.DropTable(
                name: "ReportSubscriptionRecipients");

            migrationBuilder.DropTable(
                name: "ReportSubscriptions");
        }
    }
}
