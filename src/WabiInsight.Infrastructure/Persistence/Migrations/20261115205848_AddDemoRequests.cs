using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DemoRequests",
                columns: table => new
                {
                    DemoRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Industry = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    OrganizationSize = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    DatabaseSystems = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DemoLanguage = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    PreferredTime = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PageLanguage = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    ConsentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RepeatCount = table.Column<int>(type: "int", nullable: false),
                    LastRepeatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Remark = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AssignedAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MeetingLocation = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PerformedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PresentedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AttendeeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AttendeePosition = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AttendeeEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    AttendeePhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AttendeeMobile = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Impression = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    PerformedRemark = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NextStep = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FollowUpDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoRequests", x => x.DemoRequestId);
                    table.CheckConstraint("CK_DemoRequests_Impression", "[Impression] IS NULL OR [Impression] IN ('VeryPositive','Positive','Neutral','Negative')");
                    table.CheckConstraint("CK_DemoRequests_Status", "[Status] IN ('New','Contacted','Scheduled','Performed','Converted','NotInterested','Cancelled','Spam')");
                    table.ForeignKey(
                        name: "FK_DemoRequests_Administrators_AssignedAdministratorId",
                        column: x => x.AssignedAdministratorId,
                        principalTable: "Administrators",
                        principalColumn: "AdministratorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DemoRequests_Administrators_UpdatedByAdministratorId",
                        column: x => x.UpdatedByAdministratorId,
                        principalTable: "Administrators",
                        principalColumn: "AdministratorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DemoRequestEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DemoRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PreviousStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    NewStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActorAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoRequestEvents", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_DemoRequestEvents_DemoRequests_DemoRequestId",
                        column: x => x.DemoRequestId,
                        principalTable: "DemoRequests",
                        principalColumn: "DemoRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DemoRequestEvents_DemoRequestId_CreatedAt",
                table: "DemoRequestEvents",
                columns: new[] { "DemoRequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DemoRequests_AssignedAdministratorId",
                table: "DemoRequests",
                column: "AssignedAdministratorId");

            migrationBuilder.CreateIndex(
                name: "IX_DemoRequests_CreatedAt",
                table: "DemoRequests",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DemoRequests_IpAddress_CreatedAt",
                table: "DemoRequests",
                columns: new[] { "IpAddress", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DemoRequests_NormalizedEmail_CreatedAt",
                table: "DemoRequests",
                columns: new[] { "NormalizedEmail", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DemoRequests_Status_CreatedAt",
                table: "DemoRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DemoRequests_UpdatedByAdministratorId",
                table: "DemoRequests",
                column: "UpdatedByAdministratorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DemoRequestEvents");

            migrationBuilder.DropTable(
                name: "DemoRequests");
        }
    }
}
