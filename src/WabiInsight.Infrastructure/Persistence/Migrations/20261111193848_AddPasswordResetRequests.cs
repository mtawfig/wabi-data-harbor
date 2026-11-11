using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PasswordResetRequests",
                columns: table => new
                {
                    PasswordResetRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrincipalType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PrincipalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RequestedFromIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetRequests", x => x.PasswordResetRequestId);
                    table.CheckConstraint("CK_PasswordResetRequests_Organization", "([PrincipalType] = 'OrganizationUser' AND [OrganizationId] IS NOT NULL) OR ([PrincipalType] = 'PlatformAdministrator' AND [OrganizationId] IS NULL)");
                    table.CheckConstraint("CK_PasswordResetRequests_PrincipalType", "[PrincipalType] IN ('PlatformAdministrator','OrganizationUser')");
                    table.ForeignKey(
                        name: "FK_PasswordResetRequests_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetRequests_ExpiresAt",
                table: "PasswordResetRequests",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetRequests_OrganizationId",
                table: "PasswordResetRequests",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetRequests_PrincipalType_PrincipalId_CreatedAt",
                table: "PasswordResetRequests",
                columns: new[] { "PrincipalType", "PrincipalId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetRequests_TokenHash",
                table: "PasswordResetRequests",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordResetRequests");
        }
    }
}
