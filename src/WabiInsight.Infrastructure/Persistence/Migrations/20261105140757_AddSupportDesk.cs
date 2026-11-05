using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportDesk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NotificationType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DataJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                });

            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Impact = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssignedAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FirstOpenedByAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FirstOpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PendingResponseAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReopenedCount = table.Column<int>(type: "int", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolutionSummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RequiresSupportAccess = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.TicketId);
                    table.UniqueConstraint("AK_SupportTickets_OrganizationId_TicketId", x => new { x.OrganizationId, x.TicketId });
                    table.CheckConstraint("CK_SupportTickets_Status", "[Status] IN ('New','Read','Started','PendingUserResponse','Cancelled','Resolved')");
                    table.ForeignKey(
                        name: "FK_SupportTickets_Administrators_AssignedAdministratorId",
                        column: x => x.AssignedAdministratorId,
                        principalTable: "Administrators",
                        principalColumn: "AdministratorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTickets_Administrators_FirstOpenedByAdministratorId",
                        column: x => x.FirstOpenedByAdministratorId,
                        principalTable: "Administrators",
                        principalColumn: "AdministratorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTickets_OrganizationUsers_OrganizationId_CreatedByUserId",
                        columns: x => new { x.OrganizationId, x.CreatedByUserId },
                        principalTable: "OrganizationUsers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTickets_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TicketSequences",
                columns: table => new
                {
                    Year = table.Column<int>(type: "int", nullable: false),
                    NextValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketSequences", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "SupportTicketComments",
                columns: table => new
                {
                    CommentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AuthorDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketComments", x => x.CommentId);
                    table.ForeignKey(
                        name: "FK_SupportTicketComments_SupportTickets_OrganizationId_TicketId",
                        columns: x => new { x.OrganizationId, x.TicketId },
                        principalTable: "SupportTickets",
                        principalColumns: new[] { "OrganizationId", "TicketId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportTicketEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketEvents", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_SupportTicketEvents_SupportTickets_OrganizationId_TicketId",
                        columns: x => new { x.OrganizationId, x.TicketId },
                        principalTable: "SupportTickets",
                        principalColumns: new[] { "OrganizationId", "TicketId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportTicketAttachments",
                columns: table => new
                {
                    AttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(127)", maxLength: 127, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScanStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UploadedByType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UploadedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadedByDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketAttachments", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_SupportTicketAttachments_SupportTicketComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "SupportTicketComments",
                        principalColumn: "CommentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTicketAttachments_SupportTickets_OrganizationId_TicketId",
                        columns: x => new { x.OrganizationId, x.TicketId },
                        principalTable: "SupportTickets",
                        principalColumns: new[] { "OrganizationId", "TicketId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientType_RecipientId_ReadAt_CreatedAt",
                table: "Notifications",
                columns: new[] { "RecipientType", "RecipientId", "ReadAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketAttachments_CommentId",
                table: "SupportTicketAttachments",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketAttachments_OrganizationId_TicketId",
                table: "SupportTicketAttachments",
                columns: new[] { "OrganizationId", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketAttachments_StorageKey",
                table: "SupportTicketAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketAttachments_TicketId_CreatedAt",
                table: "SupportTicketAttachments",
                columns: new[] { "TicketId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketComments_OrganizationId_TicketId",
                table: "SupportTicketComments",
                columns: new[] { "OrganizationId", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketComments_TicketId_CreatedAt",
                table: "SupportTicketComments",
                columns: new[] { "TicketId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketEvents_OrganizationId_TicketId",
                table: "SupportTicketEvents",
                columns: new[] { "OrganizationId", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketEvents_TicketId_CreatedAt",
                table: "SupportTicketEvents",
                columns: new[] { "TicketId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AssignedAdministratorId_Status",
                table: "SupportTickets",
                columns: new[] { "AssignedAdministratorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_FirstOpenedByAdministratorId",
                table: "SupportTickets",
                column: "FirstOpenedByAdministratorId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_LastActivityAt",
                table: "SupportTickets",
                column: "LastActivityAt");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_OrganizationId_CreatedByUserId",
                table: "SupportTickets",
                columns: new[] { "OrganizationId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_OrganizationId_Status",
                table: "SupportTickets",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_ReferenceNumber",
                table: "SupportTickets",
                column: "ReferenceNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "SupportTicketAttachments");

            migrationBuilder.DropTable(
                name: "SupportTicketEvents");

            migrationBuilder.DropTable(
                name: "TicketSequences");

            migrationBuilder.DropTable(
                name: "SupportTicketComments");

            migrationBuilder.DropTable(
                name: "SupportTickets");
        }
    }
}
