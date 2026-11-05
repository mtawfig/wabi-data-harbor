using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructureUsersAndTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DefaultRoleTemplates",
                columns: table => new
                {
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefaultRoleTemplates", x => x.TemplateId);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DepartmentNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DepartmentNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.DepartmentId);
                    table.UniqueConstraint("AK_Departments_OrganizationId_DepartmentId", x => new { x.OrganizationId, x.DepartmentId });
                    table.ForeignKey(
                        name: "FK_Departments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    SectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SectionCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SectionNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SectionNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => x.SectionId);
                    table.UniqueConstraint("AK_Sections_OrganizationId_DepartmentId_SectionId", x => new { x.OrganizationId, x.DepartmentId, x.SectionId });
                    table.ForeignKey(
                        name: "FK_Sections_Departments_OrganizationId_DepartmentId",
                        columns: x => new { x.OrganizationId, x.DepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "OrganizationId", "DepartmentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sections_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "DefaultRoleTemplates",
                columns: new[] { "TemplateId", "Description", "IsActive", "IsSystem", "NameAr", "NameEn", "RoleCode", "SortOrder", "UpdatedAt", "UpdatedByAdministratorId", "Version" },
                values: new object[,]
                {
                    { new Guid("4f6a1a10-0000-7000-8000-000000000001"), "Manages the organization's users, structure, roles, applications, metadata and settings.", true, true, "مدير المؤسسة", "Organization Administrator", "OrganizationAdmin", 1, new DateTimeOffset(new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("4f6a1a10-0000-7000-8000-000000000002"), "Read-only access to all approved registered objects of the organization.", true, true, "الإدارة", "Management", "Management", 2, new DateTimeOffset(new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("4f6a1a10-0000-7000-8000-000000000003"), "Default business-data role; permissions are granted explicitly.", true, false, "محلّل", "Analyst", "Analyst", 3, new DateTimeOffset(new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUsers_OrganizationId_DepartmentId_SectionId",
                table: "OrganizationUsers",
                columns: new[] { "OrganizationId", "DepartmentId", "SectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_DefaultRoleTemplates_RoleCode",
                table: "DefaultRoleTemplates",
                column: "RoleCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_OrganizationId_DepartmentCode",
                table: "Departments",
                columns: new[] { "OrganizationId", "DepartmentCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sections_OrganizationId_DepartmentId_SectionCode",
                table: "Sections",
                columns: new[] { "OrganizationId", "DepartmentId", "SectionCode" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationUsers_Departments_OrganizationId_DepartmentId",
                table: "OrganizationUsers",
                columns: new[] { "OrganizationId", "DepartmentId" },
                principalTable: "Departments",
                principalColumns: new[] { "OrganizationId", "DepartmentId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationUsers_Sections_OrganizationId_DepartmentId_SectionId",
                table: "OrganizationUsers",
                columns: new[] { "OrganizationId", "DepartmentId", "SectionId" },
                principalTable: "Sections",
                principalColumns: new[] { "OrganizationId", "DepartmentId", "SectionId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationUsers_Departments_OrganizationId_DepartmentId",
                table: "OrganizationUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationUsers_Sections_OrganizationId_DepartmentId_SectionId",
                table: "OrganizationUsers");

            migrationBuilder.DropTable(
                name: "DefaultRoleTemplates");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationUsers_OrganizationId_DepartmentId_SectionId",
                table: "OrganizationUsers");
        }
    }
}
