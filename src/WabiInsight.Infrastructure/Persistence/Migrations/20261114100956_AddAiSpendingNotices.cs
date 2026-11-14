using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiSpendingNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiSpendingNotices",
                columns: table => new
                {
                    NoticeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Month = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Threshold = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    MonthlyAiSpendLimit = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    MonthSpend = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSpendingNotices", x => x.NoticeId);
                    table.ForeignKey(
                        name: "FK_AiSpendingNotices_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiSpendingNotices_OrganizationId_Month_Threshold",
                table: "AiSpendingNotices",
                columns: new[] { "OrganizationId", "Month", "Threshold" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiSpendingNotices");
        }
    }
}
