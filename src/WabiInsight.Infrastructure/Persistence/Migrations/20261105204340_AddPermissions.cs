using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleColumnPermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ColumnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleColumnPermissions", x => new { x.RoleId, x.ColumnId });
                    table.ForeignKey(
                        name: "FK_RoleColumnPermissions_DatabaseColumns_OrganizationId_ColumnId",
                        columns: x => new { x.OrganizationId, x.ColumnId },
                        principalTable: "DatabaseColumns",
                        principalColumns: new[] { "OrganizationId", "ColumnId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleColumnPermissions_Roles_OrganizationId_RoleId",
                        columns: x => new { x.OrganizationId, x.RoleId },
                        principalTable: "Roles",
                        principalColumns: new[] { "OrganizationId", "RoleId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleRowPermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleRowPermissions", x => new { x.RoleId, x.ObjectId });
                    table.CheckConstraint("CK_RoleRowPermissions_Scope", "[Scope] IN ('All','Department','Section','Self')");
                    table.ForeignKey(
                        name: "FK_RoleRowPermissions_DatabaseObjects_OrganizationId_ObjectId",
                        columns: x => new { x.OrganizationId, x.ObjectId },
                        principalTable: "DatabaseObjects",
                        principalColumns: new[] { "OrganizationId", "ObjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleRowPermissions_Roles_OrganizationId_RoleId",
                        columns: x => new { x.OrganizationId, x.RoleId },
                        principalTable: "Roles",
                        principalColumns: new[] { "OrganizationId", "RoleId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleTablePermissions",
                columns: table => new
                {
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllColumns = table.Column<bool>(type: "bit", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GrantedByAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleTablePermissions", x => x.PermissionId);
                    table.ForeignKey(
                        name: "FK_RoleTablePermissions_DatabaseObjects_OrganizationId_ObjectId",
                        columns: x => new { x.OrganizationId, x.ObjectId },
                        principalTable: "DatabaseObjects",
                        principalColumns: new[] { "OrganizationId", "ObjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleTablePermissions_Roles_OrganizationId_RoleId",
                        columns: x => new { x.OrganizationId, x.RoleId },
                        principalTable: "Roles",
                        principalColumns: new[] { "OrganizationId", "RoleId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RowScopeColumnMappings",
                columns: table => new
                {
                    ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeColumnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepartmentColumnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SectionColumnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RowScopeColumnMappings", x => x.ObjectId);
                    table.ForeignKey(
                        name: "FK_RowScopeColumnMappings_DatabaseColumns_OrganizationId_DepartmentColumnId",
                        columns: x => new { x.OrganizationId, x.DepartmentColumnId },
                        principalTable: "DatabaseColumns",
                        principalColumns: new[] { "OrganizationId", "ColumnId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RowScopeColumnMappings_DatabaseColumns_OrganizationId_EmployeeColumnId",
                        columns: x => new { x.OrganizationId, x.EmployeeColumnId },
                        principalTable: "DatabaseColumns",
                        principalColumns: new[] { "OrganizationId", "ColumnId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RowScopeColumnMappings_DatabaseColumns_OrganizationId_SectionColumnId",
                        columns: x => new { x.OrganizationId, x.SectionColumnId },
                        principalTable: "DatabaseColumns",
                        principalColumns: new[] { "OrganizationId", "ColumnId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RowScopeColumnMappings_DatabaseObjects_OrganizationId_ObjectId",
                        columns: x => new { x.OrganizationId, x.ObjectId },
                        principalTable: "DatabaseObjects",
                        principalColumns: new[] { "OrganizationId", "ObjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoleColumnPermissions_OrganizationId_ColumnId",
                table: "RoleColumnPermissions",
                columns: new[] { "OrganizationId", "ColumnId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleColumnPermissions_OrganizationId_RoleId",
                table: "RoleColumnPermissions",
                columns: new[] { "OrganizationId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleColumnPermissions_RoleId_ObjectId",
                table: "RoleColumnPermissions",
                columns: new[] { "RoleId", "ObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleRowPermissions_OrganizationId_ObjectId",
                table: "RoleRowPermissions",
                columns: new[] { "OrganizationId", "ObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleRowPermissions_OrganizationId_RoleId",
                table: "RoleRowPermissions",
                columns: new[] { "OrganizationId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleTablePermissions_OrganizationId_ObjectId",
                table: "RoleTablePermissions",
                columns: new[] { "OrganizationId", "ObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleTablePermissions_OrganizationId_RoleId",
                table: "RoleTablePermissions",
                columns: new[] { "OrganizationId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleTablePermissions_RoleId_ObjectId",
                table: "RoleTablePermissions",
                columns: new[] { "RoleId", "ObjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RowScopeColumnMappings_OrganizationId_DepartmentColumnId",
                table: "RowScopeColumnMappings",
                columns: new[] { "OrganizationId", "DepartmentColumnId" });

            migrationBuilder.CreateIndex(
                name: "IX_RowScopeColumnMappings_OrganizationId_EmployeeColumnId",
                table: "RowScopeColumnMappings",
                columns: new[] { "OrganizationId", "EmployeeColumnId" });

            migrationBuilder.CreateIndex(
                name: "IX_RowScopeColumnMappings_OrganizationId_ObjectId",
                table: "RowScopeColumnMappings",
                columns: new[] { "OrganizationId", "ObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_RowScopeColumnMappings_OrganizationId_SectionColumnId",
                table: "RowScopeColumnMappings",
                columns: new[] { "OrganizationId", "SectionColumnId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleColumnPermissions");

            migrationBuilder.DropTable(
                name: "RoleRowPermissions");

            migrationBuilder.DropTable(
                name: "RoleTablePermissions");

            migrationBuilder.DropTable(
                name: "RowScopeColumnMappings");
        }
    }
}
