using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrganizationUsers_OrganizationId_UserId",
                table: "OrganizationUsers",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApplicationNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApplicationCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DatabaseType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EncryptedConnectionString = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConnectionFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false),
                    BuiltInCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.ApplicationId);
                    table.CheckConstraint("CK_Applications_BuiltIn", "([IsBuiltIn] = 0 AND [BuiltInCode] IS NULL) OR ([IsBuiltIn] = 1 AND [BuiltInCode] IS NOT NULL AND [EncryptedConnectionString] IS NULL)");
                    table.CheckConstraint("CK_Applications_DatabaseType", "[DatabaseType] IN ('SqlServer','Oracle','PlatformInsights')");
                    table.ForeignKey(
                        name: "FK_Applications_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RoleId);
                    table.UniqueConstraint("AK_Roles_OrganizationId_RoleId", x => new { x.OrganizationId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_Roles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedByAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_OrganizationUsers_OrganizationId_UserId",
                        columns: x => new { x.OrganizationId, x.UserId },
                        principalTable: "OrganizationUsers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_OrganizationId_RoleId",
                        columns: x => new { x.OrganizationId, x.RoleId },
                        principalTable: "Roles",
                        principalColumns: new[] { "OrganizationId", "RoleId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_ConnectionFingerprint",
                table: "Applications",
                column: "ConnectionFingerprint",
                unique: true,
                filter: "[ConnectionFingerprint] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_OrganizationId_ApplicationCode",
                table: "Applications",
                columns: new[] { "OrganizationId", "ApplicationCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_OrganizationId_BuiltInCode",
                table: "Applications",
                columns: new[] { "OrganizationId", "BuiltInCode" },
                unique: true,
                filter: "[BuiltInCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_OrganizationId_IsActive",
                table: "Applications",
                columns: new[] { "OrganizationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_OrganizationId_RoleCode",
                table: "Roles",
                columns: new[] { "OrganizationId", "RoleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_OrganizationId_RoleId",
                table: "UserRoles",
                columns: new[] { "OrganizationId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_OrganizationId_UserId",
                table: "UserRoles",
                columns: new[] { "OrganizationId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrganizationUsers_OrganizationId_UserId",
                table: "OrganizationUsers");
        }
    }
}
