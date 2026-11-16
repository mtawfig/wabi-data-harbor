using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVerifiedAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnswerFeedback",
                columns: table => new
                {
                    FeedbackId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Verdict = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerFeedback", x => x.FeedbackId);
                    table.ForeignKey(
                        name: "FK_AnswerFeedback_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerifiedAnswers",
                columns: table => new
                {
                    VerifiedAnswerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    AskedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    PlanJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Terms = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LatestVerdict = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LatestReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LatestFeedbackAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SetAsideReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UseCount = table.Column<int>(type: "int", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerifiedAnswers", x => x.VerifiedAnswerId);
                    table.ForeignKey(
                        name: "FK_VerifiedAnswers_AnalysisHistories_HistoryId",
                        column: x => x.HistoryId,
                        principalTable: "AnalysisHistories",
                        principalColumn: "HistoryId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VerifiedAnswers_Applications_OrganizationId_ApplicationId",
                        columns: x => new { x.OrganizationId, x.ApplicationId },
                        principalTable: "Applications",
                        principalColumns: new[] { "OrganizationId", "ApplicationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerifiedAnswers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnswerFeedback_OrganizationId_HistoryId_VersionNumber_CreatedAt",
                table: "AnswerFeedback",
                columns: new[] { "OrganizationId", "HistoryId", "VersionNumber", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedAnswers_HistoryId_VersionNumber",
                table: "VerifiedAnswers",
                columns: new[] { "HistoryId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedAnswers_OrganizationId_ApplicationId_Status",
                table: "VerifiedAnswers",
                columns: new[] { "OrganizationId", "ApplicationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedAnswers_OrganizationId_Status_LatestFeedbackAt",
                table: "VerifiedAnswers",
                columns: new[] { "OrganizationId", "Status", "LatestFeedbackAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnswerFeedback");

            migrationBuilder.DropTable(
                name: "VerifiedAnswers");
        }
    }
}
