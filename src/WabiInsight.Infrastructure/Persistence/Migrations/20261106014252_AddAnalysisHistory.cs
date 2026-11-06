using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisCollections",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisCollections", x => x.CollectionId);
                    table.ForeignKey(
                        name: "FK_AnalysisCollections_OrganizationUsers_OrganizationId_OwnerUserId",
                        columns: x => new { x.OrganizationId, x.OwnerUserId },
                        principalTable: "OrganizationUsers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnalysisCollections_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisHistories",
                columns: table => new
                {
                    HistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OriginalPrompt = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    PromptLanguage = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    OutputLanguage = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    CurrentVersionNumber = table.Column<int>(type: "int", nullable: false),
                    AnalysisType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DataClassification = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsFavorite = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisHistories", x => x.HistoryId);
                    table.ForeignKey(
                        name: "FK_AnalysisHistories_Applications_OrganizationId_ApplicationId",
                        columns: x => new { x.OrganizationId, x.ApplicationId },
                        principalTable: "Applications",
                        principalColumns: new[] { "OrganizationId", "ApplicationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnalysisHistories_OrganizationUsers_OrganizationId_OwnerUserId",
                        columns: x => new { x.OrganizationId, x.OwnerUserId },
                        principalTable: "OrganizationUsers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnalysisHistories_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisCollectionItems",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisCollectionItems", x => new { x.CollectionId, x.HistoryId });
                    table.ForeignKey(
                        name: "FK_AnalysisCollectionItems_AnalysisCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "AnalysisCollections",
                        principalColumn: "CollectionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnalysisCollectionItems_AnalysisHistories_HistoryId",
                        column: x => x.HistoryId,
                        principalTable: "AnalysisHistories",
                        principalColumn: "HistoryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisHistoryVersions",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    QueryPlanJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompiledSqlPreview = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultSummary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReportArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MetricId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModelProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModelName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: false),
                    DurationMs = table.Column<double>(type: "float", nullable: false),
                    PanelCount = table.Column<int>(type: "int", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FailureStage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RunByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisHistoryVersions", x => x.VersionId);
                    table.ForeignKey(
                        name: "FK_AnalysisHistoryVersions_AnalysisHistories_HistoryId",
                        column: x => x.HistoryId,
                        principalTable: "AnalysisHistories",
                        principalColumn: "HistoryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisArtifacts",
                columns: table => new
                {
                    ArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisArtifacts", x => x.ArtifactId);
                    table.ForeignKey(
                        name: "FK_AnalysisArtifacts_AnalysisHistoryVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "AnalysisHistoryVersions",
                        principalColumn: "VersionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisArtifacts_OrganizationId_HistoryId",
                table: "AnalysisArtifacts",
                columns: new[] { "OrganizationId", "HistoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisArtifacts_VersionId",
                table: "AnalysisArtifacts",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisCollectionItems_HistoryId",
                table: "AnalysisCollectionItems",
                column: "HistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisCollectionItems_OrganizationId_HistoryId",
                table: "AnalysisCollectionItems",
                columns: new[] { "OrganizationId", "HistoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisCollections_OrganizationId_OwnerUserId_Name",
                table: "AnalysisCollections",
                columns: new[] { "OrganizationId", "OwnerUserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisHistories_OrganizationId_ApplicationId",
                table: "AnalysisHistories",
                columns: new[] { "OrganizationId", "ApplicationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisHistories_OrganizationId_OwnerUserId_IsDeleted_IsArchived_UpdatedAt",
                table: "AnalysisHistories",
                columns: new[] { "OrganizationId", "OwnerUserId", "IsDeleted", "IsArchived", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisHistoryVersions_HistoryId_VersionNumber",
                table: "AnalysisHistoryVersions",
                columns: new[] { "HistoryId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisHistoryVersions_OrganizationId_CreatedAt",
                table: "AnalysisHistoryVersions",
                columns: new[] { "OrganizationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalysisArtifacts");

            migrationBuilder.DropTable(
                name: "AnalysisCollectionItems");

            migrationBuilder.DropTable(
                name: "AnalysisHistoryVersions");

            migrationBuilder.DropTable(
                name: "AnalysisCollections");

            migrationBuilder.DropTable(
                name: "AnalysisHistories");
        }
    }
}
