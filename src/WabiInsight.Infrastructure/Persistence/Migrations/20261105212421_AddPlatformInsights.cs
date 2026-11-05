using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InsightsObjects",
                columns: table => new
                {
                    InsightsObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViewName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsPresent = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsightsObjects", x => x.InsightsObjectId);
                });

            migrationBuilder.CreateTable(
                name: "InsightsColumns",
                columns: table => new
                {
                    InsightsColumnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InsightsObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ColumnName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsNullable = table.Column<bool>(type: "bit", nullable: false),
                    OrdinalPosition = table.Column<int>(type: "int", nullable: false),
                    DisplayNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsPresent = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsightsColumns", x => x.InsightsColumnId);
                    table.ForeignKey(
                        name: "FK_InsightsColumns_InsightsObjects_InsightsObjectId",
                        column: x => x.InsightsObjectId,
                        principalTable: "InsightsObjects",
                        principalColumn: "InsightsObjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InsightsMetrics",
                columns: table => new
                {
                    InsightsMetricId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetricCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InsightsObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Aggregate = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ColumnName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DateColumn = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FiltersJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsightsMetrics", x => x.InsightsMetricId);
                    table.CheckConstraint("CK_InsightsMetrics_Aggregate", "[Aggregate] IN ('Count','CountDistinct','Sum','Average','Min','Max')");
                    table.ForeignKey(
                        name: "FK_InsightsMetrics_InsightsObjects_InsightsObjectId",
                        column: x => x.InsightsObjectId,
                        principalTable: "InsightsObjects",
                        principalColumn: "InsightsObjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InsightsColumns_InsightsObjectId_ColumnName",
                table: "InsightsColumns",
                columns: new[] { "InsightsObjectId", "ColumnName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsightsMetrics_InsightsObjectId",
                table: "InsightsMetrics",
                column: "InsightsObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_InsightsMetrics_MetricCode",
                table: "InsightsMetrics",
                column: "MetricCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsightsObjects_ViewName",
                table: "InsightsObjects",
                column: "ViewName",
                unique: true);

            migrationBuilder.Sql("IF SCHEMA_ID(N'insights') IS NULL EXEC(N'CREATE SCHEMA [insights]');");
            foreach (var (viewName, sql) in Insights.InsightsSchema.Views)
            {
                migrationBuilder.Sql($"CREATE OR ALTER VIEW [insights].[{viewName}] AS\n{sql}");
            }

            InsightsSeed.Apply(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (viewName, _) in Insights.InsightsSchema.Views)
            {
                migrationBuilder.Sql($"IF OBJECT_ID(N'[insights].[{viewName}]', N'V') IS NOT NULL DROP VIEW [insights].[{viewName}];");
            }

            migrationBuilder.Sql("IF SCHEMA_ID(N'insights') IS NOT NULL DROP SCHEMA [insights];");
            migrationBuilder.DropTable(
                name: "InsightsColumns");

            migrationBuilder.DropTable(
                name: "InsightsMetrics");

            migrationBuilder.DropTable(
                name: "InsightsObjects");
        }
    }
}
