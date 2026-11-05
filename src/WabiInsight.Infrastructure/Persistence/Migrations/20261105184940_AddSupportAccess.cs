using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireSecondAdminApprovalForSupportAccess",
                table: "Organizations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SupportAccessGrants",
                columns: table => new
                {
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedByAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedByOrganizationAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedPlatformAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AllowedOperationsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ApprovedObjectReferences = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedByType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RevokedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAccessGrants", x => x.GrantId);
                    table.UniqueConstraint("AK_SupportAccessGrants_OrganizationId_GrantId", x => new { x.OrganizationId, x.GrantId });
                    table.CheckConstraint("CK_SupportAccessGrants_Status", "[Status] IN ('Proposed','Approved','Active','Expired','Revoked','Completed','Declined')");
                    table.ForeignKey(
                        name: "FK_SupportAccessGrants_Administrators_AssignedPlatformAdministratorId",
                        column: x => x.AssignedPlatformAdministratorId,
                        principalTable: "Administrators",
                        principalColumn: "AdministratorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportAccessGrants_Administrators_ProposedByAdministratorId",
                        column: x => x.ProposedByAdministratorId,
                        principalTable: "Administrators",
                        principalColumn: "AdministratorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportAccessGrants_OrganizationUsers_OrganizationId_RequestedByUserId",
                        columns: x => new { x.OrganizationId, x.RequestedByUserId },
                        principalTable: "OrganizationUsers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportAccessGrants_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportAccessGrants_SupportTickets_OrganizationId_TicketId",
                        columns: x => new { x.OrganizationId, x.TicketId },
                        principalTable: "SupportTickets",
                        principalColumns: new[] { "OrganizationId", "TicketId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportSessions",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OperationsExecuted = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportSessions", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_SupportSessions_Administrators_AdministratorId",
                        column: x => x.AdministratorId,
                        principalTable: "Administrators",
                        principalColumn: "AdministratorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportSessions_SupportAccessGrants_OrganizationId_GrantId",
                        columns: x => new { x.OrganizationId, x.GrantId },
                        principalTable: "SupportAccessGrants",
                        principalColumns: new[] { "OrganizationId", "GrantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_AssignedPlatformAdministratorId_Status",
                table: "SupportAccessGrants",
                columns: new[] { "AssignedPlatformAdministratorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_ExpiresAt",
                table: "SupportAccessGrants",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_OrganizationId_RequestedByUserId",
                table: "SupportAccessGrants",
                columns: new[] { "OrganizationId", "RequestedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_OrganizationId_Status",
                table: "SupportAccessGrants",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_OrganizationId_TicketId",
                table: "SupportAccessGrants",
                columns: new[] { "OrganizationId", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_ProposedByAdministratorId",
                table: "SupportAccessGrants",
                column: "ProposedByAdministratorId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_TicketId_CreatedAt",
                table: "SupportAccessGrants",
                columns: new[] { "TicketId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_AdministratorId_EndedAt",
                table: "SupportSessions",
                columns: new[] { "AdministratorId", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_GrantId_StartedAt",
                table: "SupportSessions",
                columns: new[] { "GrantId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_OrganizationId_GrantId",
                table: "SupportSessions",
                columns: new[] { "OrganizationId", "GrantId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportSessions");

            migrationBuilder.DropTable(
                name: "SupportAccessGrants");

            migrationBuilder.DropColumn(
                name: "RequireSecondAdminApprovalForSupportAccess",
                table: "Organizations");
        }
    }
}
