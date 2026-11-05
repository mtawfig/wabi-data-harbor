using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationsAndMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionHint",
                table: "Applications",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastConnectionTestAt",
                table: "Applications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LastConnectionTestSucceeded",
                table: "Applications",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastMetadataRefreshAt",
                table: "Applications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "Applications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Applications_OrganizationId_ApplicationId",
                table: "Applications",
                columns: new[] { "OrganizationId", "ApplicationId" });

            migrationBuilder.CreateTable(
                name: "DatabaseObjects",
                columns: table => new
                {
                    ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchemaName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ObjectName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ObjectType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DisplayNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DisplayNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BusinessDescription = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsPresent = table.Column<bool>(type: "bit", nullable: false),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseObjects", x => x.ObjectId);
                    table.UniqueConstraint("AK_DatabaseObjects_OrganizationId_ObjectId", x => new { x.OrganizationId, x.ObjectId });
                    table.ForeignKey(
                        name: "FK_DatabaseObjects_Applications_OrganizationId_ApplicationId",
                        columns: x => new { x.OrganizationId, x.ApplicationId },
                        principalTable: "Applications",
                        principalColumns: new[] { "OrganizationId", "ApplicationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DatabaseObjects_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseColumns",
                columns: table => new
                {
                    ColumnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ColumnName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MaxLength = table.Column<int>(type: "int", nullable: true),
                    Precision = table.Column<int>(type: "int", nullable: true),
                    Scale = table.Column<int>(type: "int", nullable: true),
                    IsNullable = table.Column<bool>(type: "bit", nullable: false),
                    IsPrimaryKey = table.Column<bool>(type: "bit", nullable: false),
                    OrdinalPosition = table.Column<int>(type: "int", nullable: false),
                    DisplayNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DisplayNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BusinessDescription = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Classification = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsPresent = table.Column<bool>(type: "bit", nullable: false),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseColumns", x => x.ColumnId);
                    table.UniqueConstraint("AK_DatabaseColumns_OrganizationId_ColumnId", x => new { x.OrganizationId, x.ColumnId });
                    table.CheckConstraint("CK_DatabaseColumns_Classification", "[Classification] IN ('Internal','Confidential','Restricted')");
                    table.ForeignKey(
                        name: "FK_DatabaseColumns_DatabaseObjects_OrganizationId_ObjectId",
                        columns: x => new { x.OrganizationId, x.ObjectId },
                        principalTable: "DatabaseObjects",
                        principalColumns: new[] { "OrganizationId", "ObjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseRelationships",
                columns: table => new
                {
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConstraintName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FromObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromColumnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToColumnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPresent = table.Column<bool>(type: "bit", nullable: false),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseRelationships", x => x.RelationshipId);
                    table.ForeignKey(
                        name: "FK_DatabaseRelationships_Applications_OrganizationId_ApplicationId",
                        columns: x => new { x.OrganizationId, x.ApplicationId },
                        principalTable: "Applications",
                        principalColumns: new[] { "OrganizationId", "ApplicationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DatabaseRelationships_DatabaseColumns_OrganizationId_FromColumnId",
                        columns: x => new { x.OrganizationId, x.FromColumnId },
                        principalTable: "DatabaseColumns",
                        principalColumns: new[] { "OrganizationId", "ColumnId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DatabaseRelationships_DatabaseColumns_OrganizationId_ToColumnId",
                        columns: x => new { x.OrganizationId, x.ToColumnId },
                        principalTable: "DatabaseColumns",
                        principalColumns: new[] { "OrganizationId", "ColumnId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DatabaseRelationships_DatabaseObjects_OrganizationId_FromObjectId",
                        columns: x => new { x.OrganizationId, x.FromObjectId },
                        principalTable: "DatabaseObjects",
                        principalColumns: new[] { "OrganizationId", "ObjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DatabaseRelationships_DatabaseObjects_OrganizationId_ToObjectId",
                        columns: x => new { x.OrganizationId, x.ToObjectId },
                        principalTable: "DatabaseObjects",
                        principalColumns: new[] { "OrganizationId", "ObjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseColumns_ObjectId_ColumnName",
                table: "DatabaseColumns",
                columns: new[] { "ObjectId", "ColumnName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseColumns_OrganizationId_ObjectId",
                table: "DatabaseColumns",
                columns: new[] { "OrganizationId", "ObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseObjects_ApplicationId_SchemaName_ObjectName",
                table: "DatabaseObjects",
                columns: new[] { "ApplicationId", "SchemaName", "ObjectName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseObjects_OrganizationId_ApplicationId_IsApproved",
                table: "DatabaseObjects",
                columns: new[] { "OrganizationId", "ApplicationId", "IsApproved" });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseRelationships_ApplicationId_ConstraintName_FromColumnId",
                table: "DatabaseRelationships",
                columns: new[] { "ApplicationId", "ConstraintName", "FromColumnId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseRelationships_OrganizationId_ApplicationId",
                table: "DatabaseRelationships",
                columns: new[] { "OrganizationId", "ApplicationId" });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseRelationships_OrganizationId_FromColumnId",
                table: "DatabaseRelationships",
                columns: new[] { "OrganizationId", "FromColumnId" });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseRelationships_OrganizationId_FromObjectId",
                table: "DatabaseRelationships",
                columns: new[] { "OrganizationId", "FromObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseRelationships_OrganizationId_ToColumnId",
                table: "DatabaseRelationships",
                columns: new[] { "OrganizationId", "ToColumnId" });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseRelationships_OrganizationId_ToObjectId",
                table: "DatabaseRelationships",
                columns: new[] { "OrganizationId", "ToObjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatabaseRelationships");

            migrationBuilder.DropTable(
                name: "DatabaseColumns");

            migrationBuilder.DropTable(
                name: "DatabaseObjects");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Applications_OrganizationId_ApplicationId",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ConnectionHint",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "LastConnectionTestAt",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "LastConnectionTestSucceeded",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "LastMetadataRefreshAt",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Applications");
        }
    }
}
